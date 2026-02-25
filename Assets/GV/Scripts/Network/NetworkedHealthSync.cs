using UnityEngine;
using Fusion;
using VSX.VehicleCombatKits;
using VSX.Health;

namespace GV.Network
{
    /// <summary>
    /// Synchronizes the VehicleHealth state across the network.
    /// Syncs ALL Damageables (shield, hull, etc.) not just the first one.
    /// Assumes VehicleHealth is authoritative on the State Authority (Host).
    ///
    /// Uses a fixed-size Networked array to sync up to MAX_DAMAGEABLES health values.
    /// On the host: reads local Damageable health values → writes to networked array.
    /// On clients: reads networked array → writes to local Damageables via SetHealth().
    ///
    /// Also handles destroy/restore state transitions:
    /// - When networked health → 0 and local isn't destroyed → call Destroy()
    /// - When networked health > 0 and local is destroyed → call Restore()
    /// </summary>
    public class NetworkedHealthSync : NetworkBehaviour
    {
        private const int MAX_DAMAGEABLES = 8; // Supports up to 8 health components per ship

        [SerializeField] private VehicleHealth vehicleHealth;

        // Networked array for all damageable health values.
        // Fusion [Networked] arrays must use fixed-size Capacity.
        [Networked, Capacity(8)]
        public NetworkArray<float> NetworkedHealthValues { get; }

        [Networked]
        public int NetworkedDamageableCount { get; set; }

        // Keep old property for backward compatibility (reads index 0)
        public float NetworkedCurrentHealth
        {
            get => NetworkedHealthValues.Length > 0 ? NetworkedHealthValues[0] : 0f;
        }

        private void Awake()
        {
            if (vehicleHealth == null)
                vehicleHealth = GetComponent<VehicleHealth>();

            if (vehicleHealth == null)
                vehicleHealth = GetComponentInChildren<VehicleHealth>(true);
        }

        public override void Spawned()
        {
            if (vehicleHealth == null)
            {
                Debug.LogWarning($"[NetworkedHealthSync] No VehicleHealth found on {gameObject.name}!");
                return;
            }

            int count = Mathf.Min(vehicleHealth.Damageables.Count, MAX_DAMAGEABLES);

            if (Object.HasStateAuthority)
            {
                // Initialize networked vars from local state
                NetworkedDamageableCount = count;
                for (int i = 0; i < count; i++)
                {
                    NetworkedHealthValues.Set(i, vehicleHealth.Damageables[i].CurrentHealth);
                }
            }
            else
            {
                // On clients, disable restoreOnEnable on all damageables to prevent
                // local health resets from fighting the networked health values.
                // Without this, toggling a damageable's gameObject triggers OnEnable →
                // Restore(true) → health = healthCapacity, overwriting the synced value.
                for (int i = 0; i < count; i++)
                {
                    DisableRestoreOnEnable(vehicleHealth.Damageables[i]);
                }

                // Initial sync for clients
                ApplyHealthToLocal();
            }
        }

        /// <summary>
        /// Disable restoreOnEnable on a Damageable to prevent it from resetting health
        /// when its gameObject is toggled. This is only done on clients where health
        /// is authoritative from the network, not from local state.
        /// </summary>
        private void DisableRestoreOnEnable(Damageable damageable)
        {
            // restoreOnEnable is a serialized field, we use reflection to disable it
            // since there's no public setter. This prevents local health resets on clients.
            var field = typeof(Damageable).GetField("restoreOnEnable",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(damageable, false);
            }
        }

        public override void Render()
        {
            // Always apply health on non-authority every Render frame.
            // ChangeDetector can miss rapid changes; direct polling is more reliable.
            if (!Object.HasStateAuthority)
            {
                ApplyHealthToLocal();
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            if (vehicleHealth == null) return;

            int count = Mathf.Min(vehicleHealth.Damageables.Count, MAX_DAMAGEABLES);

            // Update damageable count if it changed (modules mounted/unmounted)
            if (count != NetworkedDamageableCount)
            {
                NetworkedDamageableCount = count;
            }

            // Check each damageable for health changes and sync
            for (int i = 0; i < count; i++)
            {
                float currentLocalHealth = vehicleHealth.Damageables[i].CurrentHealth;
                if (!Mathf.Approximately(currentLocalHealth, NetworkedHealthValues[i]))
                {
                    NetworkedHealthValues.Set(i, currentLocalHealth);
                }
            }
        }

        private void ApplyHealthToLocal()
        {
            // If we are NOT the state authority, we receive the health update
            if (Object.HasStateAuthority) return;
            if (vehicleHealth == null) return;

            int count = Mathf.Min(NetworkedDamageableCount, vehicleHealth.Damageables.Count);
            count = Mathf.Min(count, MAX_DAMAGEABLES);

            for (int i = 0; i < count; i++)
            {
                float networkedHealth = NetworkedHealthValues[i];
                Damageable damageable = vehicleHealth.Damageables[i];
                float localHealth = damageable.CurrentHealth;

                // Handle destroy/restore state transitions.
                // The host controls the authoritative state; we mirror it on the client.

                // Case 1: Networked health is 0 but local damageable is not destroyed
                // → The host destroyed this damageable, we need to mirror that.
                // Note: Shield damageables have health=0 when inactive (not destroyed),
                // so only call Destroy() if the damageable is actually damageable (not shield-inactive).
                if (Mathf.Approximately(networkedHealth, 0) && !damageable.Destroyed
                    && localHealth > 0 && damageable.IsDamageable)
                {
                    // Set health to 0 directly, then fire onDamaged for VFX/audio,
                    // then call Destroy() for the state transition.
                    // IMPORTANT: Do NOT use Damage() here — it triggers shield interception
                    // which would incorrectly route "sync damage" through the shield first.
                    damageable.SetHealth(0);

                    HealthEffectInfo info = new HealthEffectInfo();
                    info.amount = localHealth;
                    info.worldPosition = damageable.transform.position;
                    info.sourceRootTransform = transform;
                    damageable.onDamaged.Invoke(info);

                    // Trigger the destroyed state (fires onDestroyed, disables GO, etc.)
                    damageable.Destroy();

                    continue;
                }

                // Case 2: Networked health is > 0 but local damageable is destroyed
                // → The host restored this damageable, we need to mirror that.
                if (networkedHealth > 0 && damageable.Destroyed)
                {
                    damageable.Restore(false); // Don't reset to full — we'll set exact value below
                }

                // Apply the networked health value directly via SetHealth().
                // IMPORTANT: Never use Damage()/Heal() for network sync — those methods
                // trigger shield interception, overflow logic, and other gameplay systems
                // that should only run on the host. The host has already computed the
                // correct final health for each Damageable; we just mirror it.
                if (!Mathf.Approximately(networkedHealth, damageable.CurrentHealth))
                {
                    float healthDelta = localHealth - networkedHealth;

                    damageable.SetHealth(networkedHealth);

                    // Fire the appropriate event so VFX/audio still plays on the client
                    if (healthDelta > 0 && damageable.IsDamageable)
                    {
                        // Health decreased → fire onDamaged for hit VFX/audio
                        HealthEffectInfo info = new HealthEffectInfo();
                        info.amount = healthDelta;
                        info.worldPosition = damageable.transform.position;
                        info.sourceRootTransform = transform;
                        damageable.onDamaged.Invoke(info);
                    }
                    else if (healthDelta < 0)
                    {
                        // Health increased → fire onHealed for heal VFX
                        HealthEffectInfo info = new HealthEffectInfo();
                        info.amount = -healthDelta;
                        info.worldPosition = damageable.transform.position;
                        damageable.onHealed.Invoke(info);
                    }
                }
            }
        }
    }
}
