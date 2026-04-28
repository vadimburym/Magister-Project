using _ExampleProject.Code.Features._Core.Components;
using _ExampleProject.Code.Features.Projectile.Factory;
using _ExampleProject.Code.Infrastructure.StaticData.Weapons;
using _Project.Code.Features.Test;
using _Project.Code.Infrastructure;
using _Project.Infrastructure;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace _ExampleProject.Code.Features._Core.Systems
{
    public sealed class WeaponShootSystem : IEcsRunSystem, IEcsInitSystem
    {
        private readonly EcsFilterInject<Inc<WeaponShootRequest, Weapon>> _filter;
        private readonly EcsPoolInject<WeaponShootRequest> _requestPool;
        private readonly EcsPoolInject<Weapon> _weaponPool;
        private readonly EcsPoolInject<CombatTeam> _teamPool;

        private WeaponsStaticData _weaponsStaticData;
        private ProjectileFactory _projectileFactory;

        public void Init(IEcsSystems systems)
        {
            _weaponsStaticData = ServiceLocator.Resolve<StaticDataService>().WeaponsStaticData;
            _projectileFactory = ServiceLocator.Resolve<ProjectileFactory>();
        }

        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var requestData = ref _requestPool.Value.Get(entity);
                ref var weapon = ref _weaponPool.Value.Get(entity);
                var weaponData = _weaponsStaticData.GetWeaponData(weapon.WeaponId);
                requestData.BurstTickTime += Time.deltaTime;
                if (requestData.BurstTickTime < weaponData.BurstInterval)
                    continue;

                requestData.BurstTickTime -= weaponData.BurstInterval;
                var position = (Vector2)weapon.FirePointRef.position;
                var baseDirection = (requestData.ShootPosition - position).normalized;
                var team = _teamPool.Value.Has(entity) ? _teamPool.Value.Get(entity).Value : CombatTeamId.Neutral;

                int projectileCount = Mathf.Max(1, weaponData.ProjectilesPerBurst);
                float totalSpread = projectileCount > 1 ? 10f : 0f;
                float step = projectileCount > 1 ? totalSpread / (projectileCount - 1) : 0f;
                float start = -totalSpread * 0.5f;

                for (int i = 0; i < projectileCount; i++)
                {
                    var angle = start + step * i;
                    var direction = Quaternion.Euler(0f, 0f, angle) * baseDirection;
                    _projectileFactory.Create(weaponData.ProjectileId, position, direction, team);
                }

                requestData.BurstCount += 1;
                if (requestData.BurstCount >= weaponData.BurstsPerShot)
                    _requestPool.Value.Del(entity);
            }
        }
    }
}
