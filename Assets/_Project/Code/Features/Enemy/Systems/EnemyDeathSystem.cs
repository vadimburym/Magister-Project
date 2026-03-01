using _ExampleProject.Code.Features._Core.Requests;
using _Project.Code.Core.Keys;
using _Project.Code.Features.Test;
using _Project.Infrastructure;
using Infrastructure.MemoryPool.Service;
using Leopotam.EcsLite;
using Leopotam.EcsLite.Di;

namespace _ExampleProject.Code.Features.Enemy.Systems
{
    public sealed class EnemyDeathSystem : IEcsRunSystem, IEcsInitSystem
    {
        private readonly EcsFilterInject<Inc<DeathRequest, EnemyTag>> _filter;
        private readonly EcsPoolInject<EnemyTag> _enemyPool;
        private readonly EcsWorldInject _world;
        
        private IMemoryPoolService _memoryPoolService;
        
        public void Init(IEcsSystems systems)
        {
            _memoryPoolService = ServiceLocator.Resolve<IMemoryPoolService>();
        }
        
        public void Run(IEcsSystems systems)
        {
            foreach (var entity in _filter.Value)
            {
                ref var enemy = ref _enemyPool.Value.Get(entity);
                _memoryPoolService.UnspawnGameObject(MemoryPoolId.Enemy, enemy.GameObjectRef);
                _world.Value.DelEntity(entity);
            }
        }
    }
}