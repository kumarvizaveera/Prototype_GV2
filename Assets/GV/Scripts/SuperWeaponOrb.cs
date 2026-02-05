using UnityEngine;
using GV;

namespace VSX.Engines3D
{
    public class SuperWeaponOrb : MonoBehaviour
    {
        [Header("Power Up Settings")]
        public bool manualTriggerOnly = false;
        public PowerUpType powerUpType = PowerUpType.SuperWeapon;

        [Header("Duration")]
        [Tooltip("How long the super weapon effect lasts in seconds.")]
        public float duration = 10f;

        [Header("Projectile/Laser Bonuses")]
        [Tooltip("Multiplier for projectile damage.")]
        public float projectileDamageMultiplier = 2f;
        [Tooltip("Multiplier for projectile range (max distance/lifetime).")]
        public float projectileRangeMultiplier = 2f;
        [Tooltip("Multiplier for projectile speed.")]
        public float projectileSpeedMultiplier = 2f;
        [Tooltip("Multiplier for projectile fire rate (reduces interval).")]
        public float projectileFireRateMultiplier = 2f;
        [Tooltip("Multiplier for projectile reload time (reduces burst interval).")]
        public float projectileReloadMultiplier = 2f;

        [Header("Missile Bonuses")]
        [Tooltip("Multiplier for missile damage.")]
        public float missileDamageMultiplier = 2f;
        [Tooltip("Multiplier for missile range (lock range).")]
        public float missileRangeMultiplier = 2f;
        [Tooltip("Multiplier for missile speed.")]
        public float missileSpeedMultiplier = 2f;
        [Tooltip("Multiplier for missile fire rate (reduces interval).")]
        public float missileFireRateMultiplier = 2f;
        [Tooltip("Multiplier for missile reload time (reduces burst interval).")]
        public float missileReloadMultiplier = 2f;

        [Header("Visuals")]
        [Tooltip("Optional sound to play on pickup (using AudioSource at position).")]
        public AudioClip pickupSound;
        [Tooltip("Optional: Instantiate this GameObject when collected (e.g. for audio prefab).")]
        public GameObject pickupSoundObject;

        [Tooltip("Optional effect to spawn on pickup.")]
        public GameObject pickupEffect;

        private void OnTriggerEnter(Collider other)
        {
            if (manualTriggerOnly) return;

            // Try to find the AircraftCharacterManager on the object that hit us
            // Use GetComponentInParent to handle hitting a collider child of the ship
            AircraftCharacterManager manager = other.GetComponentInParent<AircraftCharacterManager>();

            if (manager == null && other.attachedRigidbody != null)
            {
                manager = other.attachedRigidbody.GetComponent<AircraftCharacterManager>();
            }

            if (manager != null)
            {
               Apply(manager.gameObject);
            }
        }

        public void Apply(GameObject target)
        {
             AircraftCharacterManager manager = target.GetComponent<AircraftCharacterManager>();
             if (manager == null) manager = target.GetComponentInChildren<AircraftCharacterManager>();
             if (manager == null) manager = target.GetComponentInParent<AircraftCharacterManager>();

             if (manager != null)
             {
                // Register Collection
                if (PowerUpManager.Instance != null)
                {
                    PowerUpManager.Instance.RegisterCollection(powerUpType);
                }

                // Create the bonus object
                AircraftCharacterManager.SuperWeaponBonuses bonuses = new AircraftCharacterManager.SuperWeaponBonuses();
                
                bonuses.projectileDamage = projectileDamageMultiplier;
                bonuses.projectileRange = projectileRangeMultiplier;
                bonuses.projectileSpeed = projectileSpeedMultiplier;
                bonuses.projectileFireRate = projectileFireRateMultiplier;
                bonuses.projectileReload = projectileReloadMultiplier;

                bonuses.missileDamage = missileDamageMultiplier;
                bonuses.missileRange = missileRangeMultiplier;
                bonuses.missileSpeed = missileSpeedMultiplier;
                bonuses.missileFireRate = missileFireRateMultiplier;
                bonuses.missileReload = missileReloadMultiplier;

                // Apply
                manager.SetSuperWeapon(bonuses, duration);

                Debug.Log($"[SuperWeaponOrb] Applied Super Weapon bonuses for {duration} seconds.");
                
                // Audio
                if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
                if (pickupSoundObject != null) Instantiate(pickupSoundObject, transform.position, Quaternion.identity);

                // Visuals
                if (pickupEffect != null)
                {
                    Instantiate(pickupEffect, transform.position, Quaternion.identity);
                }

                // Destroy the orb (only if not manual)
                if (!manualTriggerOnly)
                {
                    Destroy(gameObject);
                }
             }
        }
    }
}
