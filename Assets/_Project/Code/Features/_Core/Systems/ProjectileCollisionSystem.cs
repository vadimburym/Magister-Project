using _ExampleProject.Code.Features._Core.Behaviours;
using _ExampleProject.Code.Features._Core.Requests;
using _ExampleProject.Code.Features.Enemy.Facade;
using _ExampleProject.Code.Features.Player.Facade;
using _ExampleProject.Code.Features.Projectile.Components;
using _Project.Code.Features.Test;
using _Project.Infrastructure;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace _ExampleProject.Code.Features._Core.Systems
{
    public sealed class ProjectileCollisionSystem : IEcsRunSystem
    {
        private readonly EcsFilterInject<Inc<CollisionEvent>> _filter = EcsWorlds.EVENTS;
        private readonly EcsPoolInject<CollisionEvent> _eventPool = EcsWorlds.EVENTS;
        private readonly EcsPoolInject<ProjectileTag> _projPool;
        private readonly EcsPoolInject<ProjectileDamage> _damagePool;
        private readonly EcsPoolInject<CombatTeam> _teamPool;
        private readonly EcsPoolInject<Health> _healthPool;
        private readonly EcsPoolInject<Armor> _armorPool;
        private readonly EcsPoolInject<DeathRequest> _deathRequestPool;

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var eventData = ref _eventPool.Value.Get(entity);
                if (!_projPool.Value.Has(eventData.Entity))
                    continue;

                var projectileEntity = eventData.Entity;
                var projectileTeam = _teamPool.Value.Has(projectileEntity)
                    ? _teamPool.Value.Get(projectileEntity).Value
                    : CombatTeamId.Neutral;
                var projectileDamage = _damagePool.Value.Has(projectileEntity)
                    ? _damagePool.Value.Get(projectileEntity).Value
                    : 0;

                bool shouldDestroyProjectile = true;

                if (eventData.CollisionRef.TryGetComponent(out PlayerFacade playerFacade))
                {
                    var targetEntity = playerFacade.EcsEntity.Index;
                    if (projectileTeam == CombatTeamId.Player)
                        shouldDestroyProjectile = false;
                    else
                        ApplyDamage(targetEntity, projectileDamage);
                }
                else if (eventData.CollisionRef.TryGetComponent(out EnemyFacade enemyFacade))
                {
                    var targetEntity = enemyFacade.EcsEntity.Index;
                    if (projectileTeam == CombatTeamId.Enemy)
                        shouldDestroyProjectile = false;
                    else
                        ApplyDamage(targetEntity, projectileDamage);
                }

                if (shouldDestroyProjectile && !_deathRequestPool.Value.Has(projectileEntity))
                    _deathRequestPool.Value.Add(projectileEntity);
            }
        }

        private void ApplyDamage(int entity, int rawDamage)
        {
            if (!_healthPool.Value.Has(entity))
                return;

            ref var health = ref _healthPool.Value.Get(entity);
            int armor = _armorPool.Value.Has(entity) ? _armorPool.Value.Get(entity).Value : 0;
            int finalDamage = Mathf.Max(1, rawDamage - armor);
            health.CurrentValue -= finalDamage;

            if (health.CurrentValue <= 0 && !_deathRequestPool.Value.Has(entity))
                _deathRequestPool.Value.Add(entity);
        }
    }
}
