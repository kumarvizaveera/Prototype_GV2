using UnityEngine;
using GV;
using GV.Network;
using Fusion;
using TMPro;
// NetworkAudioHelper already available via GV.Network

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
        
        [Header("UI")]
        public TMP_Text timerText;
        public string timerFormat = "Weapon: {0:0.0}";

        [Tooltip("Optional effect to spawn on pickup.")]
        public GameObject pickupEffect;

        private void Start()
        {
            if (timerText != null) timerText.gameObject.SetActive(false);
        }

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
             // Register Collection
             if (PowerUpManager.Instance != null)
             {
                 PowerUpManager.Instance.RegisterCollection(powerUpType);
             }

             // Build bonuses
             AircraftCharacterManager.SuperWeaponBonuses bonuses = new AircraftCharacterManager.SuperWeaponBonuses();
             float dur = duration;

             if (PowerSphereMasterController.Instance != null)
             {
                 var settings = PowerSphereMasterController.Instance.superWeaponSettings;
                 dur = settings.duration;

                 bonuses.projectileDamage = settings.projectileDamageMultiplier;
                 bonuses.projectileRange = settings.projectileRangeMultiplier;
                 bonuses.projectileSpeed = settings.projectileSpeedMultiplier;
                 bonuses.projectileFireRate = settings.projectileFireRateMultiplier;
                 bonuses.projectileReload = settings.projectileReloadMultiplier;

                 bonuses.missileDamage = settings.missileDamageMultiplier;
                 bonuses.missileRange = settings.missileRangeMultiplier;
                 bonuses.missileSpeed = settings.missileSpeedMultiplier;
                 bonuses.missileFireRate = settings.missileFireRateMultiplier;
                 bonuses.missileReload = settings.missileReloadMultiplier;
             }
             else
             {
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
             }

             // Route through NetworkSuperWeaponHandler so it syncs to all clients
             NetworkObject netObj = target.GetComponent<NetworkObject>();
             if (netObj == null) netObj = target.GetComponentInParent<NetworkObject>();

             if (netObj != null)
             {
                 var swHandler = netObj.GetComponentInChildren<NetworkSuperWeaponHandler>();
                 if (swHandler != null)
                 {
                     swHandler.ActivateSuperWeapon(dur, bonuses);
                     Debug.Log($"[SuperWeaponOrb] Applied Super Weapon via NetworkSuperWeaponHandler for {dur}s.");
                 }
                 else
                 {
                     // Fallback: apply directly if handler is missing (non-networked testing)
                     AircraftCharacterManager manager = target.GetComponent<AircraftCharacterManager>();
                     if (manager == null) manager = target.GetComponentInChildren<AircraftCharacterManager>();
                     if (manager == null) manager = target.GetComponentInParent<AircraftCharacterManager>();
                     if (manager != null)
                     {
                         manager.SetSuperWeapon(bonuses, dur);
                         Debug.LogWarning($"[SuperWeaponOrb] NetworkSuperWeaponHandler missing — applied locally only for {dur}s.");
                     }
                 }
             }
             else
             {
                 // No NetworkObject — pure local fallback
                 AircraftCharacterManager manager = target.GetComponent<AircraftCharacterManager>();
                 if (manager == null) manager = target.GetComponentInChildren<AircraftCharacterManager>();
                 if (manager == null) manager = target.GetComponentInParent<AircraftCharacterManager>();
                 if (manager != null)
                 {
                     manager.SetSuperWeapon(bonuses, dur);
                     Debug.LogWarning($"[SuperWeaponOrb] No NetworkObject — applied locally only for {dur}s.");
                 }
             }

             // Audio/Visuals — only for local player to prevent double sounds in multiplayer
             if (NetworkAudioHelper.IsLocalPlayer(target))
             {
                 if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
                 if (pickupSoundObject != null) Instantiate(pickupSoundObject, transform.position, Quaternion.identity);

                 if (pickupEffect != null)
                 {
                     Instantiate(pickupEffect, transform.position, Quaternion.identity);
                 }
             }
        }
    }
}
