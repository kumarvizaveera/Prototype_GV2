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
                    Debug.Log($"  [{i}] {d.name} type={d.HealthType?.name ?? "NULL"} health={d.CurrentHealth}/{d.HealthCapacity}");
                }
            }
            else
            {
                // Initial sync for clients
                ApplyHealthToLocal();
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
            bool anyChanged = false;
            for (int i = 0; i < count; i++)
            {
                float currentLocalHealth = vehicleHealth.Damageables[i].CurrentHealth;
                if (!Mathf.Approximately(currentLocalHealth, NetworkedHealthValues[i]))
                {
                    Debug.Log($"[HealthSync] HOST WRITE [{i}] {vehicleHealth.Damageables[i].name} " +
                              $"type={vehicleHealth.Damageables[i].HealthType?.name ?? "NULL"}: " +
                              $"{NetworkedHealthValues[i]:F1} → {currentLocalHealth:F1} " +
                              $"(capacity={vehicleHealth.Damageables[i].HealthCapacity:F1}) on {gameObject.name}");
                    NetworkedHealthValues.Set(i, currentLocalHealth);
                    anyChanged = true;
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
                    healthDump += $"[{i}]{d.HealthType?.name ?? "?"}: {d.CurrentHealth:F0}/{d.HealthCapacity:F0}(net={NetworkedHealthValues[i]:F0}) ";
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

            bool anyChanged = false;
            for (int i = 0; i < count; i++)
            {
                float networkedHealth = NetworkedHealthValues[i];
                float localHealth = vehicleHealth.Damageables[i].CurrentHealth;

                if (!Mathf.Approximately(networkedHealth, localHealth))
                {
                    Debug.Log($"[HealthSync] CLIENT APPLY [{i}] {vehicleHealth.Damageables[i].name} " +
                              $"type={vehicleHealth.Damageables[i].HealthType?.name ?? "NULL"}: " +
                              $"local={localHealth:F1} → networked={networkedHealth:F1} on {gameObject.name}");
                    vehicleHealth.Damageables[i].SetHealth(networkedHealth);
                    anyChanged = true;
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
                    healthDump += $"[{i}]{d.HealthType?.name ?? "?"}: local={d.CurrentHealth:F0} net={NetworkedHealthValues[i]:F0}/{d.HealthCapacity:F0} ";
                }
                Debug.Log($"[HealthSync] CLIENT STATE on {gameObject.name}: netCount={NetworkedDamageableCount} localCount={vehicleHealth.Damageables.Count} | {healthDump}");
            }
        }
    }
}
