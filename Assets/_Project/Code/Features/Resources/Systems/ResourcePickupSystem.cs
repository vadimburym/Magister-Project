using _ExampleProject.Code.Features._Core.Components;
using _ExampleProject.Code.Features.Player.Components;
using _Project.Code.Core.Keys;
using _Project.Code.Features.Resources.Components;
using _Project.Code.Features.Test;
using _Project.Infrastructure;
using Infrastructure.MemoryPool.Service;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;
using UnityEngine;

namespace _Project.Code.Features.Resources.Systems
{
    public sealed class ResourcePickupSystem : IEcsRunSystem, IEcsInitSystem
    {
        private readonly EcsFilterInject<Inc<PlayerTag, UnityTransform, Health, Weapon>> _playerFilter;
        private readonly EcsFilterInject<Inc<ResourceTag, UnityTransform>> _resourceFilter;
        private readonly EcsPoolInject<UnityTransform> _transformPool;
        private readonly EcsPoolInject<ResourceTag> _resourcePool;
        private readonly EcsPoolInject<Health> _healthPool;
        private readonly EcsPoolInject<Weapon> _weaponPool;
        private readonly EcsWorldInject _world;

        private IMemoryPoolService _memoryPoolService;

        public void Init(IEcsSystems systems)
        {
            _memoryPoolService = ServiceLocator.Resolve<IMemoryPoolService>();
        }

        public void Run(IEcsSystems systems)
        {
            int playerEntity = -1;
            Vector3 playerPosition = Vector3.zero;

            foreach (var entity in _playerFilter.Value)
            {
                playerEntity = entity;
                playerPosition = _transformPool.Value.Get(entity).Ref.position;
                break;
            }

            if (playerEntity == -1)
                return;

            foreach (var entity in _resourceFilter.Value)
            {
                ref var resource = ref _resourcePool.Value.Get(entity);
                var resourcePosition = _transformPool.Value.Get(entity).Ref.position;
                if ((resourcePosition - playerPosition).sqrMagnitude > resource.PickupRadius * resource.PickupRadius)
                    continue;

                switch (resource.ResourceId)
                {
                    case ResourceId.HealthSmall:
                        ref var health = ref _healthPool.Value.Get(playerEntity);
                        health.CurrentValue = Mathf.Min(health.MaxValue, health.CurrentValue + resource.Amount);
                        _memoryPoolService.UnspawnGameObject(MemoryPoolId.HealthPickup, resource.GameObjectRef);
                        break;
                    case ResourceId.AmmoSmall:
                        ref var weapon = ref _weaponPool.Value.Get(playerEntity);
                        weapon.TotalAmmo += resource.Amount;
                        _memoryPoolService.UnspawnGameObject(MemoryPoolId.AmmoPickup, resource.GameObjectRef);
                        break;
                }

                _world.Value.DelEntity(entity);
            }
        }
    }
}
