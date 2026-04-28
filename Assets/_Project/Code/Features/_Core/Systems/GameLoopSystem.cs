using _ExampleProject.Code.Features.Enemy.Factory;
using _ExampleProject.Code.Features.Player.Components;
using _ExampleProject.Code.Features.Player.Factory;
using _ExampleProject.Code.Scene.SpawnPoints;
using _Project.Code.Core.Abstractions.Contracts;
using _Project.Code.Core.Keys;
using _Project.Code.Features.Resources.Components;
using _Project.Code.Features.Resources.Factory;
using _Project.Code.Features.Test;
using _Project.Code.Infrastructure;
using _Project.Code.Infrastructure.StaticData.GameLoop;
using _Project.Code.Scene.SpawnPoints;
using _Project.Infrastructure;
using Infrastructure.MemoryPool.Service;
using Leopotam.EcsLite;
using UnityEngine;

namespace _ExampleProject.Code.Features._Core.Systems
{
    public sealed class GameLoopSystem : IConstruct, ITick
    {
        private IEnemyFactory _enemyFactory;
        private IResourceFactory _resourceFactory;
        private IPlayerFactory _playerFactory;
        private PlayerSpawnPosition _playerSpawnPosition;
        private EnemySpawnPointsProvider _enemySpawnPoints;
        private ResourceSpawnPointsProvider _resourceSpawnPoints;
        private GameLoopStaticData _settings;
        private IMemoryPoolService _memoryPoolService;
        private StaticDataService _staticData;
        private EcsWorld _world;

        private int _currentLevel;
        private float _nextLevelTick;
        private bool _isWaitingNextLevel;
        private bool _isWaitingRespawn;
        private float _respawnTick;
        private bool _initialWaveSpawned;

        public void Construct()
        {
            _enemyFactory = ServiceLocator.Resolve<IEnemyFactory>();
            _resourceFactory = ServiceLocator.Resolve<IResourceFactory>();
            _playerFactory = ServiceLocator.Resolve<IPlayerFactory>();
            _staticData = ServiceLocator.Resolve<StaticDataService>();
            _settings = _staticData.GameLoopStaticData;
            _memoryPoolService = ServiceLocator.Resolve<IMemoryPoolService>();
            _world = EcsWorlds.GetWorld(EcsWorlds.DEFAULT);

            _playerSpawnPosition = Object.FindFirstObjectByType<PlayerSpawnPosition>();
            _enemySpawnPoints = Object.FindFirstObjectByType<EnemySpawnPointsProvider>();
            _resourceSpawnPoints = Object.FindFirstObjectByType<ResourceSpawnPointsProvider>();
        }

        public void Tick()
        {
            if (!_initialWaveSpawned)
            {
                SpawnCurrentLevel();
                _initialWaveSpawned = true;
                return;
            }

            if (CountEntities<PlayerTag>() == 0)
            {
                if (!_isWaitingRespawn)
                {
                    _isWaitingRespawn = true;
                    _respawnTick = 2f;
                }
                else
                {
                    _respawnTick -= Time.deltaTime;
                    if (_respawnTick <= 0f)
                    {
                        _isWaitingRespawn = false;
                        ResetRun();
                    }
                }
                return;
            }

            if (CountEntities<EnemyTag>() > 0)
                return;

            if (!_isWaitingNextLevel)
            {
                _isWaitingNextLevel = true;
                _nextLevelTick = _settings.NextLevelDelay;
                return;
            }

            _nextLevelTick -= Time.deltaTime;
            if (_nextLevelTick > 0f)
                return;

            _isWaitingNextLevel = false;
            _currentLevel += 1;
            ClearResources();
            SpawnCurrentLevel();
        }

        private int CountEntities<T>() where T : struct
        {
            var filter = EcsWorlds.GetFilter<T>(EcsWorlds.DEFAULT);
            return filter.GetEntitiesCount();
        }

        private void ResetRun()
        {
            ClearEnemies();
            ClearResources();
            _currentLevel = 0;
            if (_playerSpawnPosition != null)
                _playerFactory.Create(_playerSpawnPosition.SpawnPosition);
            SpawnCurrentLevel();
        }

        private void SpawnCurrentLevel()
        {
            SpawnEnemies();
            SpawnResources();
        }

        private void SpawnEnemies()
        {
            if (_enemySpawnPoints == null || _enemySpawnPoints.SpawnPoints.Count == 0)
                return;

            int enemyCount = Mathf.Min(_settings.BaseEnemyCount + _currentLevel * _settings.AdditionalEnemiesPerLevel,
                Mathf.Min(_settings.MaxEnemiesPerLevel, _enemySpawnPoints.SpawnPoints.Count));
            var enemies = _staticData.EnemyStaticData.Enemies;
            int availableTypes = Mathf.Max(1, Mathf.Min(enemies.Length, 1 + _currentLevel / 2));

            for (int i = 0; i < enemyCount; i++)
            {
                var spawnPoint = _enemySpawnPoints.SpawnPoints[i % _enemySpawnPoints.SpawnPoints.Count];
                var enemyConfig = enemies[Random.Range(0, availableTypes)];
                _enemyFactory.Create(enemyConfig.Id, spawnPoint.position);
            }
        }

        private void SpawnResources()
        {
            if (_resourceSpawnPoints == null || _resourceSpawnPoints.SpawnPoints.Count == 0)
                return;

            int ammoCount = Mathf.Max(0, _settings.BaseAmmoPickups - (_currentLevel / 2) * _settings.ResourceReductionEachTwoLevels);
            int healthCount = Mathf.Max(0, _settings.BaseHealthPickups - (_currentLevel / 2) * _settings.ResourceReductionEachTwoLevels);

            int cursor = 0;
            for (int i = 0; i < ammoCount && cursor < _resourceSpawnPoints.SpawnPoints.Count; i++, cursor++)
                _resourceFactory.Create(ResourceId.AmmoSmall, _resourceSpawnPoints.SpawnPoints[cursor].position);

            for (int i = 0; i < healthCount && cursor < _resourceSpawnPoints.SpawnPoints.Count; i++, cursor++)
                _resourceFactory.Create(ResourceId.HealthSmall, _resourceSpawnPoints.SpawnPoints[cursor].position);
        }

        private void ClearEnemies()
        {
            var filter = EcsWorlds.GetFilter<EnemyTag>(EcsWorlds.DEFAULT);
            var pool = _world.GetPool<EnemyTag>();
            var entities = filter.GetRawEntities();
            int count = filter.GetEntitiesCount();
            for (int i = 0; i < count; i++)
            {
                int entity = entities[i];
                var enemy = pool.Get(entity);
                _memoryPoolService.UnspawnGameObject(MemoryPoolId.Enemy, enemy.GameObjectRef);
                _world.DelEntity(entity);
            }
        }

        private void ClearResources()
        {
            var filter = EcsWorlds.GetFilter<ResourceTag>(EcsWorlds.DEFAULT);
            var pool = _world.GetPool<ResourceTag>();
            var entities = filter.GetRawEntities();
            int count = filter.GetEntitiesCount();
            for (int i = 0; i < count; i++)
            {
                int entity = entities[i];
                var resource = pool.Get(entity);
                var poolId = resource.ResourceId == ResourceId.HealthSmall ? MemoryPoolId.HealthPickup : MemoryPoolId.AmmoPickup;
                _memoryPoolService.UnspawnGameObject(poolId, resource.GameObjectRef);
                _world.DelEntity(entity);
            }
        }
    }
}
