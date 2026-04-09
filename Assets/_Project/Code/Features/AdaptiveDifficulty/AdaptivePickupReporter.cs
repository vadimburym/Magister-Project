using AdaptiveDifficulty.Runtime;
using UnityEngine;

namespace _Project.Code.Features.AdaptiveDifficulty
{
    public sealed class AdaptivePickupReporter : MonoBehaviour
    {
        [SerializeField] private AdaptivePickupType _pickupType = AdaptivePickupType.None;
        [SerializeField] private bool _destroyAfterReport = false;

        private bool _isReported;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isReported || _pickupType == AdaptivePickupType.None)
                return;

            ProjectAdaptiveDifficultyBootstrap bootstrap = ProjectAdaptiveDifficultyBootstrap.Instance;
            if (bootstrap == null)
                return;

            _isReported = true;
            bootstrap.ReportPickup(_pickupType);

            if (_destroyAfterReport)
                Destroy(gameObject);
        }
    }
}