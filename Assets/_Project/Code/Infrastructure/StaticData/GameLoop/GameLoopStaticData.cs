using UnityEngine;

namespace _Project.Code.Infrastructure.StaticData.GameLoop
{
    [CreateAssetMenu(fileName = nameof(GameLoopStaticData), menuName = "_Project/StaticData/New GameLoopStaticData")]
    public sealed class GameLoopStaticData : ScriptableObject
    {
        [Header("Enemies")]
        public int BaseEnemyCount = 2;
        public int AdditionalEnemiesPerLevel = 1;
        public int MaxEnemiesPerLevel = 10;

        [Header("Resources")]
        public int BaseAmmoPickups = 1;
        public int BaseHealthPickups = 1;
        public int ResourceReductionEachTwoLevels = 1;

        [Header("Flow")]
        public float NextLevelDelay = 2f;
    }
}
