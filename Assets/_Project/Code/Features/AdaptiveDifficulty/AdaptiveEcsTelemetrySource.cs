using System.Collections.Generic;
using AdaptiveDifficulty.Runtime;
using _ExampleProject.Code.Features._Core.Behaviours;
using _ExampleProject.Code.Features._Core.Components;
using _ExampleProject.Code.Features.Enemy.Facade;
using _ExampleProject.Code.Features.Player.Components;
using _ExampleProject.Code.Features.Player.Facade;
using _ExampleProject.Code.Features.Projectile.Components;
using _ExampleProject.Code.Features.Projectile.Facade;
using _ExampleProject.Code.Infrastructure.StaticData.Enemy;
using _Project.Code.Features.Test;
using _Project.Code.Infrastructure;
using _ExampleProject.Code.Features._Core.Requests;
using _Project.Infrastructure;
using Leopotam.EcsLite;
using UnityEngine;

namespace _Project.Code.Features.AdaptiveDifficulty
{
    public sealed class AdaptiveEcsTelemetrySource : IAdaptiveTelemetrySource
    {
        private readonly AdaptiveDifficultySettings _settings;
        private readonly AdaptiveFrameTelemetry _frame = new();

        private readonly HashSet<int> _knownEnemyEntities = new();
        private readonly HashSet<int> _knownPlayerEntities = new();
        private readonly HashSet<int> _knownProjectileEntities = new();

        private readonly Dictionary<int, int> _enemyHealthCache = new();
        private readonly Dictionary<int, int> _playerHealthCache = new();
        private readonly Dictionary<int, AdaptiveProjectileOwnerInfo> _projectileOwners = new();
        private readonly HashSet<int> _processedProjectileCollisions = new();

        private EcsWorld _world;
        private StaticDataService _staticDataService;
        private EnemyStaticData _enemyStaticData;

        private float _comboTimer;
        private int _currentCombo;

        public AdaptiveEcsTelemetrySource(AdaptiveDifficultySettings settings)
        {
            _settings = settings;
        }

        public bool IsReady => _world != null && _staticDataService != null;

        public void Tick(float dt)
        {
            if (TryInitialize() == false)
                return;

            _comboTimer += dt;
            if (_comboTimer >= _settings.ComboResetDelay)
                _currentCombo = 0;

            EnsureEnemyEntities();
            EnsurePlayerEntities();
            EnsureProjectileEntities();
            SyncEnemyHealthChanges();
            SyncPlayerHealthChanges();
            DetectEnemyDeaths();
            DetectPlayerDeaths();
            CleanupDestroyedProjectiles();
        }

        public AdaptiveFrameTelemetry ConsumeFrameTelemetry()
        {
            AdaptiveFrameTelemetry output = new AdaptiveFrameTelemetry
            {
                Kills = _frame.Kills,
                DamageDealt = _frame.DamageDealt,
                DamageTaken = _frame.DamageTaken,
                Shots = _frame.Shots,
                Hits = _frame.Hits,
                Combo = _frame.Combo,
                AmmoPicked = _frame.AmmoPicked,
                HealthPicked = _frame.HealthPicked
            };

            _frame.Reset();
            return output;
        }

        public void ReportPickup(AdaptivePickupType pickupType)
        {
            switch (pickupType)
            {
                case AdaptivePickupType.Ammo:
                    _frame.AmmoPicked += 1;
                    break;
                case AdaptivePickupType.Health:
                    _frame.HealthPicked += 1;
                    break;
            }
        }

        public void RegisterProjectileCollision(int projectileEntity, GameObject collisionRef)
        {
            if (TryInitialize() == false)
                return;

            if (_processedProjectileCollisions.Add(projectileEntity) == false)
                return;

            if (IsEntityAlive(projectileEntity) == false)
                return;

            EcsPool<Health> healthPool = _world.GetPool<Health>();
            EcsPool<Armor> armorPool = _world.GetPool<Armor>();
            EcsPool<DeathRequest> deathRequestPool = _world.GetPool<DeathRequest>();

            EcsEntity targetEntityRef = collisionRef.GetComponentInParent<EcsEntity>();
            if (targetEntityRef == null)
                return;

            int targetEntity = targetEntityRef.Index;
            if (IsEntityAlive(targetEntity) == false)
                return;

            if (healthPool.Has(targetEntity) == false)
                return;

            AdaptiveProjectileOwnerInfo ownerInfo = ResolveProjectileOwner(projectileEntity);

            bool isTargetEnemy = _world.GetPool<EnemyTag>().Has(targetEntity);
            bool isTargetPlayer = _world.GetPool<PlayerTag>().Has(targetEntity);

            int armorValue = armorPool.Has(targetEntity) ? armorPool.Get(targetEntity).Value : 0;
            int damage = Mathf.Max(1, _settings.DefaultProjectileDamage - armorValue);

            ref Health health = ref healthPool.Get(targetEntity);
            int previousHealth = health.CurrentValue;
            health.CurrentValue = Mathf.Max(0, health.CurrentValue - damage);

            bool shouldCountAsPlayerDamage =
                ownerInfo.Kind == AdaptiveProjectileOwnerKind.Player && isTargetEnemy;

            bool shouldCountAsEnemyDamage =
                ownerInfo.Kind == AdaptiveProjectileOwnerKind.Enemy && isTargetPlayer;

            if (shouldCountAsPlayerDamage)
            {
                _frame.DamageDealt += damage;
                _frame.Hits += 1;
            }
            else if (shouldCountAsEnemyDamage)
            {
                _frame.DamageTaken += damage;
            }
            else if (_settings.CountAnyEnemyHealthLossAsPlayerDamage && isTargetEnemy && previousHealth > health.CurrentValue)
            {
                _frame.DamageDealt += damage;
                _frame.Hits += 1;
            }

            if (isTargetEnemy)
                _enemyHealthCache[targetEntity] = health.CurrentValue;
            else if (isTargetPlayer)
                _playerHealthCache[targetEntity] = health.CurrentValue;

            if (health.CurrentValue <= 0 && deathRequestPool.Has(targetEntity) == false)
                deathRequestPool.Add(targetEntity);
        }

        private bool TryInitialize()
        {
            if (_world != null && _staticDataService != null)
                return true;

            if (ServiceLocator.TryResolve(out StaticDataService staticDataService) == false || staticDataService == null)
                return false;

            _staticDataService = staticDataService;
            _enemyStaticData = _staticDataService.EnemyStaticData;
            _world = EcsWorlds.GetWorld(EcsWorlds.DEFAULT);
            return _world != null;
        }

        private void EnsureEnemyEntities()
        {
            EcsPool<EnemyTag> enemyPool = _world.GetPool<EnemyTag>();
            EcsPool<Health> healthPool = _world.GetPool<Health>();
            EcsPool<Armor> armorPool = _world.GetPool<Armor>();
            EcsFilter filter = _world.Filter<EnemyTag>().End();

            foreach (int entity in filter)
            {
                ref EnemyTag enemy = ref enemyPool.Get(entity);
                EnemyConfig config = _enemyStaticData.GetEnemyConfig(enemy.EnemyId);
                if (config == null)
                    continue;

                if (healthPool.Has(entity) == false)
                {
                    ref Health health = ref healthPool.Add(entity);
                    health.MaxValue = config.MaxHealth;
                    health.CurrentValue = config.MaxHealth;
                }

                if (armorPool.Has(entity) == false)
                {
                    ref Armor armor = ref armorPool.Add(entity);
                    armor.Value = config.Armour;
                }

                _knownEnemyEntities.Add(entity);
                if (_enemyHealthCache.ContainsKey(entity) == false)
                    _enemyHealthCache[entity] = healthPool.Get(entity).CurrentValue;
            }
        }

        private void EnsurePlayerEntities()
        {
            EcsPool<PlayerTag> playerPool = _world.GetPool<PlayerTag>();
            EcsPool<Health> healthPool = _world.GetPool<Health>();
            EcsPool<Armor> armorPool = _world.GetPool<Armor>();
            EcsFilter filter = _world.Filter<PlayerTag>().End();

            foreach (int entity in filter)
            {
                if (healthPool.Has(entity) == false)
                {
                    ref Health health = ref healthPool.Add(entity);
                    health.MaxValue = _settings.DefaultPlayerMaxHealth;
                    health.CurrentValue = _settings.DefaultPlayerMaxHealth;
                }

                if (armorPool.Has(entity) == false)
                {
                    ref Armor armor = ref armorPool.Add(entity);
                    armor.Value = _settings.DefaultPlayerArmor;
                }

                _knownPlayerEntities.Add(entity);
                if (_playerHealthCache.ContainsKey(entity) == false)
                    _playerHealthCache[entity] = healthPool.Get(entity).CurrentValue;
            }
        }

        private void EnsureProjectileEntities()
        {
            EcsPool<ProjectileTag> projectilePool = _world.GetPool<ProjectileTag>();
            EcsFilter filter = _world.Filter<ProjectileTag>().End();

            foreach (int entity in filter)
            {
                if (_knownProjectileEntities.Add(entity) == false)
                    continue;

                _frame.Shots += 1;
                _projectileOwners[entity] = ResolveProjectileOwner(entity);

                ref ProjectileTag projectile = ref projectilePool.Get(entity);
                if (projectile.GameObjectRef == null)
                    continue;

                AdaptiveProjectileCollisionRelay relay = projectile.GameObjectRef.GetComponent<AdaptiveProjectileCollisionRelay>();
                if (relay == null)
                    relay = projectile.GameObjectRef.AddComponent<AdaptiveProjectileCollisionRelay>();

                relay.Construct(this, entity);
            }
        }

        private void SyncEnemyHealthChanges()
        {
            EcsPool<EnemyTag> enemyPool = _world.GetPool<EnemyTag>();
            EcsPool<Health> healthPool = _world.GetPool<Health>();
            EcsFilter filter = _world.Filter<EnemyTag>().End();

            foreach (int entity in filter)
            {
                if (healthPool.Has(entity) == false)
                    continue;

                int currentHealth = healthPool.Get(entity).CurrentValue;
                if (_enemyHealthCache.TryGetValue(entity, out int cachedHealth) == false)
                {
                    _enemyHealthCache[entity] = currentHealth;
                    continue;
                }

                if (currentHealth < cachedHealth && _settings.CountAnyEnemyHealthLossAsPlayerDamage)
                {
                    int delta = cachedHealth - currentHealth;
                    _frame.DamageDealt += delta;
                    _frame.Hits += 1;
                }

                _enemyHealthCache[entity] = currentHealth;
            }
        }

        private void SyncPlayerHealthChanges()
        {
            EcsPool<PlayerTag> playerPool = _world.GetPool<PlayerTag>();
            EcsPool<Health> healthPool = _world.GetPool<Health>();
            EcsFilter filter = _world.Filter<PlayerTag>().End();

            foreach (int entity in filter)
            {
                if (healthPool.Has(entity) == false)
                    continue;

                int currentHealth = healthPool.Get(entity).CurrentValue;
                if (_playerHealthCache.TryGetValue(entity, out int cachedHealth) == false)
                {
                    _playerHealthCache[entity] = currentHealth;
                    continue;
                }

                if (currentHealth < cachedHealth)
                {
                    int delta = cachedHealth - currentHealth;
                    _frame.DamageTaken += delta;
                }

                _playerHealthCache[entity] = currentHealth;
            }
        }

        private void DetectEnemyDeaths()
        {
            EcsPool<EnemyTag> enemyPool = _world.GetPool<EnemyTag>();
            EcsFilter filter = _world.Filter<EnemyTag>().End();

            HashSet<int> aliveEntities = new HashSet<int>();
            foreach (int entity in filter)
                aliveEntities.Add(entity);

            if (_knownEnemyEntities.Count == 0)
            {
                _knownEnemyEntities.UnionWith(aliveEntities);
                return;
            }

            List<int> removedEntities = new List<int>();
            foreach (int entity in _knownEnemyEntities)
            {
                if (aliveEntities.Contains(entity))
                    continue;

                removedEntities.Add(entity);
            }

            for (int i = 0; i < removedEntities.Count; i++)
            {
                int entity = removedEntities[i];
                _knownEnemyEntities.Remove(entity);
                _enemyHealthCache.Remove(entity);

                if (_settings.CountEnemyDespawnAsKill == false)
                    continue;

                _frame.Kills += 1;
                RegisterComboKill();
            }

            _knownEnemyEntities.UnionWith(aliveEntities);
        }

        private void DetectPlayerDeaths()
        {
            EcsFilter filter = _world.Filter<PlayerTag>().End();
            HashSet<int> aliveEntities = new HashSet<int>();
            foreach (int entity in filter)
                aliveEntities.Add(entity);

            List<int> removedEntities = new List<int>();
            foreach (int entity in _knownPlayerEntities)
            {
                if (aliveEntities.Contains(entity))
                    continue;

                removedEntities.Add(entity);
            }

            for (int i = 0; i < removedEntities.Count; i++)
            {
                int entity = removedEntities[i];
                _knownPlayerEntities.Remove(entity);
                _playerHealthCache.Remove(entity);
            }

            _knownPlayerEntities.UnionWith(aliveEntities);
        }

        private void CleanupDestroyedProjectiles()
        {
            EcsFilter filter = _world.Filter<ProjectileTag>().End();
            HashSet<int> aliveEntities = new HashSet<int>();
            foreach (int entity in filter)
                aliveEntities.Add(entity);

            List<int> removedEntities = new List<int>();
            foreach (int entity in _knownProjectileEntities)
            {
                if (aliveEntities.Contains(entity))
                    continue;

                removedEntities.Add(entity);
            }

            for (int i = 0; i < removedEntities.Count; i++)
            {
                int entity = removedEntities[i];
                _knownProjectileEntities.Remove(entity);
                _projectileOwners.Remove(entity);
                _processedProjectileCollisions.Remove(entity);
            }
        }

        private void RegisterComboKill()
        {
            if (_comboTimer <= _settings.ComboResetDelay)
                _currentCombo += 1;
            else
                _currentCombo = 1;

            _comboTimer = 0f;
            _frame.Combo = Mathf.Max(_frame.Combo, _currentCombo);
        }



        private bool IsEntityAlive(int entity)
        {
            if (_world == null)
                return false;

            if (entity < 0)
                return false;

            try
            {
                return _world.GetEntityGen(entity) > 0;
            }
            catch
            {
                return false;
            }
        }

        private AdaptiveProjectileOwnerInfo ResolveProjectileOwner(int projectileEntity)
        {
            if (_projectileOwners.TryGetValue(projectileEntity, out AdaptiveProjectileOwnerInfo cachedOwner))
                return cachedOwner;

            EcsPool<ProjectileTag> projectilePool = _world.GetPool<ProjectileTag>();
            if (projectilePool.Has(projectileEntity) == false)
                return new AdaptiveProjectileOwnerInfo(-1, AdaptiveProjectileOwnerKind.Unknown);

            Vector3 projectilePosition = projectilePool.Get(projectileEntity).GameObjectRef != null
                ? projectilePool.Get(projectileEntity).GameObjectRef.transform.position
                : Vector3.zero;

            EcsPool<Weapon> weaponPool = _world.GetPool<Weapon>();
            EcsFilter weaponFilter = _world.Filter<Weapon>().End();

            float bestSqrDistance = _settings.ProjectileOwnerSearchRadius * _settings.ProjectileOwnerSearchRadius;
            int bestEntity = -1;
            AdaptiveProjectileOwnerKind bestKind = AdaptiveProjectileOwnerKind.Unknown;

            foreach (int entity in weaponFilter)
            {
                ref Weapon weapon = ref weaponPool.Get(entity);
                if (weapon.FirePointRef == null)
                    continue;

                float sqrDistance = (weapon.FirePointRef.position - projectilePosition).sqrMagnitude;
                if (sqrDistance > bestSqrDistance)
                    continue;

                bestSqrDistance = sqrDistance;
                bestEntity = entity;

                if (_world.GetPool<PlayerTag>().Has(entity))
                    bestKind = AdaptiveProjectileOwnerKind.Player;
                else if (_world.GetPool<EnemyTag>().Has(entity))
                    bestKind = AdaptiveProjectileOwnerKind.Enemy;
                else
                    bestKind = AdaptiveProjectileOwnerKind.Unknown;
            }

            AdaptiveProjectileOwnerInfo ownerInfo = new AdaptiveProjectileOwnerInfo(bestEntity, bestKind);
            _projectileOwners[projectileEntity] = ownerInfo;
            return ownerInfo;
        }
    }
}