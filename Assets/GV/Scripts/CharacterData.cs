using UnityEngine;

namespace VSX.Engines3D
{
    [CreateAssetMenu(fileName = "New Character Data", menuName = "VSX/Engines3D/Character Data")]
    public class CharacterData : ScriptableObject
    {
        [Header("Identity")]
        public string characterName;
        // Icon is optional as user has sprites in scene, but keeping it for reference if needed
        [Tooltip("Optional icon for UI if needed later")]
        public Sprite icon; 

        [Header("Bonus Multipliers")]
        [Tooltip("Multiplier for movement forces. 1.1 = +10% bonus.")]
        public float speedMultiplier = 1.1f;

        [Tooltip("Multiplier for steering forces. 1.1 = +10% bonus.")]
        public float steeringMultiplier = 1.1f;

        [Tooltip("Multiplier for boost forces. 1.1 = +10% bonus.")]
        public float boostMultiplier = 1.1f;
        [Header("Weapons (Projectile/Laser)")]
        [Tooltip("Multiplier for projectile damage.")]
        public float projectileDamageMultiplier = 1f;
        [Tooltip("Multiplier for projectile range (max distance/lifetime).")]
        public float projectileRangeMultiplier = 1f;
        [Tooltip("Multiplier for projectile speed.")]
        public float projectileSpeedMultiplier = 1f;
        [Tooltip("Multiplier for fire rate (reduces interval).")]
        public float projectileFireRateMultiplier = 1f;
        [Tooltip("Multiplier for reload time (reduces burst interval).")]
        public float projectileReloadMultiplier = 1f; // Affects BurstInterval

        [Header("Weapons (Missile)")]
        [Tooltip("Multiplier for missile damage.")]
        public float missileDamageMultiplier = 1f;
        [Tooltip("Multiplier for missile range (lock range).")]
        public float missileRangeMultiplier = 1f;
        [Tooltip("Multiplier for missile speed.")]
        public float missileSpeedMultiplier = 1f;
        [Tooltip("Multiplier for fire rate (reduces interval).")]
        public float missileFireRateMultiplier = 1f;
        [Tooltip("Multiplier for reload time (reduces burst interval).")]
        public float missileReloadMultiplier = 1f;
    }
}
