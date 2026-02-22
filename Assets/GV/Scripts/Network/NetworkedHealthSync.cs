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

                Debug.Log($"[NetworkedHealthSync] HOST: Initialized {count} damageables on {gameObject.name}");
                for (int i = 0; i < count; i++)
                {
                    var d = vehicleHealth.Damageables[i];
                    Debug.Log($"  [{i}] {d.name} type={d.HealthType?.name ?? "NULL"} health={d.CurrentHealth}/{d.HealthCapacity} destroyed={d.Destroyed}");
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

                Debug.Log($"[NetworkedHealthSync] CLIENT: Initialized {count} damageables on {gameObject.name}");
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

        // Debug: throttle logging to avoid spam
        private float _lastHealthDebugLog = 0f;
        private const float HEALTH_LOG_INTERVAL = 1.0f;

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
                    Debug.Log($"[HealthSync] HOST WRITE [{i}] {vehicleHealth.Damageables[i].name} " +
                              $"type={vehicleHealth.Damageables[i].HealthType?.name ?? "NULL"}: " +
                              $"{NetworkedHealthValues[i]:F1} → {currentLocalHealth:F1} " +
                              $"(capacity={vehicleHealth.Damageables[i].HealthCapacity:F1}) " +
                              $"destroyed={vehicleHealth.Damageables[i].Destroyed} on {gameObject.name}");
                    NetworkedHealthValues.Set(i, currentLocalHealth);
                }
            }

            // Periodic full state dump
            if (Time.time - _lastHealthDebugLog > HEALTH_LOG_INTERVAL)
            {
                _lastHealthDebugLog = Time.time;
                string healthDump = "";
                for (int i = 0; i < count; i++)
                {
                    var d = vehicleHealth.Damageables[i];
                    healthDump += $"[{i}]{d.HealthType?.name ?? "?"}: {d.CurrentHealth:F0}/{d.HealthCapacity:F0}(net={NetworkedHealthValues[i]:F0}) dest={d.Destroyed} ";
                }
                Debug.Log($"[HealthSync] HOST STATE on {gameObject.name}: count={count} | {healthDump}");
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
                    Debug.Log($"[HealthSync] CLIENT DESTROY [{i}] {damageable.name} " +
                              $"type={damageable.HealthType?.name ?? "NULL"} on {gameObject.name}");
                    damageable.SetHealth(0);
                    damageable.Destroy();
                    continue;
                }

                // Case 2: Networked health is > 0 but local damageable is destroyed
                // → The host restored this damageable, we need to mirror that.
                if (networkedHealth > 0 && damageable.Destroyed)
                {
                    Debug.Log($"[HealthSync] CLIENT RESTORE [{i}] {damageable.name} " +
                              $"type={damageable.HealthType?.name ?? "NULL"}: " +
                              $"networked={networkedHealth:F1} on {gameObject.name}");
                    damageable.Restore(false); // Don't reset to full — we'll set exact value below
                }

                // Apply the networked health value
                if (!Mathf.Approximately(networkedHealth, damageable.CurrentHealth))
                {
                    Debug.Log($"[HealthSync] CLIENT APPLY [{i}] {damageable.name} " +
                              $"type={damageable.HealthType?.name ?? "NULL"}: " +
                              $"local={damageable.CurrentHealth:F1} → networked={networkedHealth:F1} on {gameObject.name}");
                    damageable.SetHealth(networkedHealth);
                }
            }

            // Periodic full state dump for client too
            if (Time.time - _lastHealthDebugLog > HEALTH_LOG_INTERVAL)
            {
                _lastHealthDebugLog = Time.time;
                string healthDump = "";
                for (int i = 0; i < count; i++)
                {
                    var d = vehicleHealth.Damageables[i];
                    healthDump += $"[{i}]{d.HealthType?.name ?? "?"}: local={d.CurrentHealth:F0} net={NetworkedHealthValues[i]:F0}/{d.HealthCapacity:F0} dest={d.Destroyed} ";
                }
                Debug.Log($"[HealthSync] CLIENT STATE on {gameObject.name}: netCount={NetworkedDamageableCount} localCount={vehicleHealth.Damageables.Count} | {healthDump}");
            }
        }
    }
}
