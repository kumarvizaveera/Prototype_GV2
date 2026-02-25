using UnityEngine;
using TMPro;
using VSX.Weapons;
using VSX.Engines3D;
using VSX.Health;
using Fusion;
using GV.Scripts;

namespace GV.Scripts
{
    public class AstraMissileRefill : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("The label of the missile to refill (must match the label in MissileCycleControllerDynamic).")]
        public string missileLabel = "Astra";

        [Tooltip("The amount of ammo to add per interval.")]
        public int amountPerInterval = 1;

        [Tooltip("Time in seconds between refills while inside the zone.")]
        public float refillInterval = 2.0f;

        [Tooltip("Sound to play on each refill (optional).")]
        public AudioClip refillSound;

        [Header("UI")]
        [Tooltip("Text component to display recharging status and countdown.")]
        public TMP_Text feedbackText;

        [Tooltip("Message to display before the countdown.")]
        public string refillMessage = "Astra Energy is recharging...";

        [Tooltip("Text component to display when ammo capacity is full.")]
        public TMP_Text capacityFullText;

        [Tooltip("Message to display when ammo is at full capacity.")]
        public string capacityFullMessage = "Character Astra energy is full";

        [Header("Boost Fuel Refill")]
        [Tooltip("Enable boost fuel refill in this zone.")]
        public bool enableBoostRefill = true;

        [Tooltip("Amount of boost fuel added per second (fills gradually).")]
        public float boostFuelPerSecond = 5f;

        [Tooltip("Sound to play on each boost refill (optional). Uses refillSound if not set.")]
        public AudioClip boostRefillSound;

        [Header("Boost Fuel UI")]
        [Tooltip("Text component to display boost recharging status.")]
        public TMP_Text boostFeedbackText;

        [Tooltip("Message to display during boost fuel recharge.")]
        public string boostRefillMessage = "Boost fuel recharging...";

        [Tooltip("Text component to display when boost fuel is full.")]
        public TMP_Text boostCapacityFullText;

        [Tooltip("Message to display when boost fuel is at full capacity.")]
        public string boostCapacityFullMessage = "Boost fuel is full";

        [Header("Health Refill")]
        [Tooltip("Enable hull health refill in this zone.")]
        public bool enableHealthRefill = true;

        [Tooltip("Amount of hull health restored per second (fills gradually).")]
        public float healthPerSecond = 10f;

        [Tooltip("Sound to play on each health refill tick (optional).")]
        public AudioClip healthRefillSound;

        [Header("Health Refill UI")]
        [Tooltip("Text component to display health recharging status.")]
        public TMP_Text healthFeedbackText;

        [Tooltip("Message to display during health recharge.")]
        public string healthRefillMessage = "Hull repairing...";

        [Tooltip("Text component to display when health is full.")]
        public TMP_Text healthCapacityFullText;

        [Tooltip("Message to display when health is at full capacity.")]
        public string healthCapacityFullMessage = "Hull integrity is full";

        private MissileCycleControllerDynamic activeController;
        private float timer;
        private Collider triggerCollider;

        // Aircraft swap detection — Astra refill only applies to Vimana (B), not Spaceship (A)
        private AircraftMeshSwapWithFX activeSwapController;

        // Boost fuel refill state
        private VehicleEngines3D activeEngines;

        // Health refill state — hull Damageables (excludes shield)
        private Damageable[] activeHullDamageables;
        private NetworkObject activeHealthNetObj;

        private void OnValidate()
        {
            // Try to find an existing trigger
            if (triggerCollider == null)
            {
                Collider[] colliders = GetComponents<Collider>();
                foreach (var col in colliders)
                {
                    if (col.isTrigger)
                    {
                        triggerCollider = col;
                        break;
                    }
                }
            }
        }

        private void Awake()
        {
             // Find any existing trigger collider
            Collider[] colliders = GetComponents<Collider>();
            foreach (var col in colliders)
            {
                if (col.isTrigger)
                {
                    triggerCollider = col;
                    break;
                }
            }

            // If no trigger exists, add a default SphereCollider
            if (triggerCollider == null)
            {
                triggerCollider = gameObject.AddComponent<SphereCollider>();
                triggerCollider.isTrigger = true;
            }
            
            if (feedbackText != null) feedbackText.gameObject.SetActive(false);
            if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);
            if (boostFeedbackText != null) boostFeedbackText.gameObject.SetActive(false);
            if (boostCapacityFullText != null) boostCapacityFullText.gameObject.SetActive(false);
            if (healthFeedbackText != null) healthFeedbackText.gameObject.SetActive(false);
            if (healthCapacityFullText != null) healthCapacityFullText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Checks if the colliding object belongs to the local player's ship.
        /// Only the local player's ship has InputAuthority on this machine.
        /// </summary>
        private bool IsLocalPlayer(Collider other)
        {
            // Search from root to find the NetworkObject (ships are complex hierarchies)
            Transform root = other.transform.root;
            NetworkObject netObj = root.GetComponent<NetworkObject>();
            if (netObj == null && other.attachedRigidbody != null)
            {
                netObj = other.attachedRigidbody.GetComponent<NetworkObject>();
            }
            if (netObj == null)
            {
                netObj = other.GetComponentInParent<NetworkObject>();
            }

            // If no NetworkObject found, allow it (offline/local testing)
            if (netObj == null) return true;

            return netObj.HasInputAuthority;
        }

        private void OnTriggerEnter(Collider other)
        {
            // ── Health refill: works for ANY ship, not just local player ──
            // The host needs to detect all ships for authoritative healing.
            // The local player's client uses this for UI display only.
            if (enableHealthRefill)
            {
                Transform root = other.transform.root;
                NetworkObject netObj = root.GetComponent<NetworkObject>();
                if (netObj == null && other.attachedRigidbody != null)
                    netObj = other.attachedRigidbody.GetComponent<NetworkObject>();
                if (netObj == null)
                    netObj = other.GetComponentInParent<NetworkObject>();

                // Only activate if we are the host (StateAuthority) for this ship,
                // OR if this is the local player (for UI display).
                bool isHost = netObj != null && netObj.HasStateAuthority;
                bool isLocal = IsLocalPlayer(other);

                if (isHost || isLocal)
                {
                    Damageable[] allDamageables = root.GetComponentsInChildren<Damageable>(true);

                    var hullList = new System.Collections.Generic.List<Damageable>();
                    foreach (var d in allDamageables)
                    {
                        var shield = d.GetComponent<EnergyShieldController>();
                        if (shield == null) shield = d.GetComponentInParent<EnergyShieldController>();
                        if (shield != null) continue;

                        hullList.Add(d);
                    }

                    if (hullList.Count > 0)
                    {
                        activeHullDamageables = hullList.ToArray();
                        activeHealthNetObj = netObj;
                        if (isLocal) UpdateHealthFeedbackUI();
                    }
                }
            }

            // ── Missile, boost, and UI: only for the local player's ship ──
            if (!IsLocalPlayer(other)) return;

            MissileCycleControllerDynamic controller = null;

            // Method 1: Check Attached Rigidbody (Most reliable for vehicles)
            if (other.attachedRigidbody != null)
            {
                controller = other.attachedRigidbody.GetComponent<MissileCycleControllerDynamic>();
                if (controller == null) controller = other.attachedRigidbody.GetComponentInChildren<MissileCycleControllerDynamic>();
            }

            // Method 2: Check Component directly
            if (controller == null)
            {
                controller = other.GetComponent<MissileCycleControllerDynamic>();
            }

            // Method 3: Check Parent
            if (controller == null)
            {
                controller = other.GetComponentInParent<MissileCycleControllerDynamic>();
            }

             // Method 4: Check Root (Last resort)
            if (controller == null && other.transform.root != other.transform)
            {
                controller = other.transform.root.GetComponentInChildren<MissileCycleControllerDynamic>();
            }

            if (controller != null)
            {
                activeController = controller;
                timer = refillInterval;

                // Cache swap controller to check which aircraft is active each frame
                activeSwapController = other.transform.root.GetComponentInChildren<AircraftMeshSwapWithFX>();

                UpdateFeedbackUI();
            }
            // Boost fuel: find VehicleEngines3D on the vehicle
            if (enableBoostRefill)
            {
                VehicleEngines3D engines = null;

                if (other.attachedRigidbody != null)
                {
                    engines = other.attachedRigidbody.GetComponent<VehicleEngines3D>();
                    if (engines == null) engines = other.attachedRigidbody.GetComponentInChildren<VehicleEngines3D>();
                }
                if (engines == null) engines = other.GetComponent<VehicleEngines3D>();
                if (engines == null) engines = other.GetComponentInParent<VehicleEngines3D>();
                if (engines == null && other.transform.root != other.transform)
                    engines = other.transform.root.GetComponentInChildren<VehicleEngines3D>();

                if (engines != null && engines.BoostResourceHandlers != null && engines.BoostResourceHandlers.Count > 0)
                {
                    activeEngines = engines;
                    UpdateBoostFeedbackUI();
                }
            }
        }

        private void OnTriggerExit(Collider other)
        {
            // ── Health refill exit: works for any ship (matches OnTriggerEnter) ──
            if (enableHealthRefill && activeHullDamageables != null)
            {
                Transform root = other.transform.root;
                Damageable[] check = root.GetComponentsInChildren<Damageable>(true);
                if (check.Length > 0)
                {
                    activeHullDamageables = null;
                    activeHealthNetObj = null;
                    if (healthFeedbackText != null) healthFeedbackText.gameObject.SetActive(false);
                    if (healthCapacityFullText != null) healthCapacityFullText.gameObject.SetActive(false);
                }
            }

            // ── Missile, boost: only for local player ──
            if (!IsLocalPlayer(other)) return;

            MissileCycleControllerDynamic controller = null;

            if (other.attachedRigidbody != null)
            {
                controller = other.attachedRigidbody.GetComponent<MissileCycleControllerDynamic>();
                if (controller == null) controller = other.attachedRigidbody.GetComponentInChildren<MissileCycleControllerDynamic>();
            }

            if (controller == null) controller = other.GetComponent<MissileCycleControllerDynamic>();
            if (controller == null) controller = other.GetComponentInParent<MissileCycleControllerDynamic>();
             if (controller == null && other.transform.root != other.transform) controller = other.transform.root.GetComponentInChildren<MissileCycleControllerDynamic>();

            if (controller != null && controller == activeController)
            {
                activeController = null;
                activeSwapController = null;
                if (feedbackText != null) feedbackText.gameObject.SetActive(false);
                if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);
            }

            // Clear boost refill on exit
            if (enableBoostRefill)
            {
                VehicleEngines3D engines = null;

                if (other.attachedRigidbody != null)
                {
                    engines = other.attachedRigidbody.GetComponent<VehicleEngines3D>();
                    if (engines == null) engines = other.attachedRigidbody.GetComponentInChildren<VehicleEngines3D>();
                }
                if (engines == null) engines = other.GetComponent<VehicleEngines3D>();
                if (engines == null) engines = other.GetComponentInParent<VehicleEngines3D>();
                if (engines == null && other.transform.root != other.transform)
                    engines = other.transform.root.GetComponentInChildren<VehicleEngines3D>();

                if (engines != null && engines == activeEngines)
                {
                    activeEngines = null;
                    if (boostFeedbackText != null) boostFeedbackText.gameObject.SetActive(false);
                    if (boostCapacityFullText != null) boostCapacityFullText.gameObject.SetActive(false);
                }
            }

        }

        private void Update()
        {
            if (activeController != null)
            {
                // Astra refill only applies to Vimana (B). When Spaceship (A) is active, hide UI and skip.
                bool isSpaceshipActive = activeSwapController != null && activeSwapController.IsAActive;

                if (isSpaceshipActive)
                {
                    if (feedbackText != null) feedbackText.gameObject.SetActive(false);
                    if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);
                }
                else if (IsAmmoFull())
                {
                    // Capacity is full — hide refill text, show full text
                    if (feedbackText != null) feedbackText.gameObject.SetActive(false);
                    if (capacityFullText != null)
                    {
                        capacityFullText.gameObject.SetActive(true);
                        capacityFullText.text = capacityFullMessage;
                    }
                }
                else
                {
                    // Still refilling — show refill text, hide full text
                    if (capacityFullText != null) capacityFullText.gameObject.SetActive(false);

                    timer -= Time.deltaTime;
                    UpdateFeedbackUI();

                    if (timer <= 0f)
                    {
                        PerformRefill();
                        timer = refillInterval;
                    }
                }
            }

            // Boost fuel refill — adds fuel gradually every frame
            if (enableBoostRefill && activeEngines != null)
            {
                if (IsBoostFuelFull())
                {
                    if (boostFeedbackText != null) boostFeedbackText.gameObject.SetActive(false);
                    if (boostCapacityFullText != null)
                    {
                        boostCapacityFullText.gameObject.SetActive(true);
                        boostCapacityFullText.text = boostCapacityFullMessage;
                    }
                }
                else
                {
                    if (boostCapacityFullText != null) boostCapacityFullText.gameObject.SetActive(false);

                    PerformBoostRefill();
                    UpdateBoostFeedbackUI();
                }
            }

            // Hull health refill — heals gradually every frame
            if (enableHealthRefill && activeHullDamageables != null)
            {
                if (IsHullHealthFull())
                {
                    if (healthFeedbackText != null) healthFeedbackText.gameObject.SetActive(false);
                    if (healthCapacityFullText != null)
                    {
                        healthCapacityFullText.gameObject.SetActive(true);
                        healthCapacityFullText.text = healthCapacityFullMessage;
                    }
                }
                else
                {
                    if (healthCapacityFullText != null) healthCapacityFullText.gameObject.SetActive(false);

                    PerformHealthRefill();
                    UpdateHealthFeedbackUI();
                }
            }
        }

        /// <summary>
        /// Checks if the ammo resource container for the target missile is at full capacity.
        /// </summary>
        private bool IsAmmoFull()
        {
            if (activeController == null) return false;

            foreach (var entry in activeController.missileMounts)
            {
                if (entry.label.Equals(missileLabel, System.StringComparison.OrdinalIgnoreCase))
                {
                    if (entry.mount != null && entry.mount.MountedModule() != null)
                    {
                        Weapon weapon = entry.mount.MountedModule().GetComponent<Weapon>();
                        if (weapon != null)
                        {
                            foreach (var handler in weapon.ResourceHandlers)
                            {
                                if (handler.unitResourceChange < 0 && !handler.perSecond && handler.resourceContainer != null)
                                {
                                    return handler.resourceContainer.IsFull;
                                }
                            }
                        }
                    }
                    break;
                }
            }
            return false;
        }

        private void UpdateFeedbackUI()
        {
            if (feedbackText != null)
            {
                feedbackText.gameObject.SetActive(true);
                // "Astra Energy is recharging... 2"
                feedbackText.text = $"{refillMessage} {Mathf.Ceil(timer)}";
            }
        }

        private void PerformRefill()
        {
            if (activeController != null)
            {
                activeController.AddAmmo(missileLabel, amountPerInterval);

                if (refillSound != null)
                {
                    AudioSource.PlayClipAtPoint(refillSound, transform.position);
                }
            }
        }

        // ── Boost Fuel Refill ──────────────────────────────────────────

        /// <summary>
        /// Checks if all boost fuel containers on the vehicle are full.
        /// </summary>
        private bool IsBoostFuelFull()
        {
            if (activeEngines == null) return false;

            foreach (var handler in activeEngines.BoostResourceHandlers)
            {
                if (handler != null && handler.resourceContainer != null)
                {
                    if (!handler.resourceContainer.IsFull)
                        return false;
                }
            }
            return true;
        }

        private void UpdateBoostFeedbackUI()
        {
            if (boostFeedbackText != null && activeEngines != null)
            {
                boostFeedbackText.gameObject.SetActive(true);

                // Show current / capacity for the first boost resource container
                foreach (var handler in activeEngines.BoostResourceHandlers)
                {
                    if (handler != null && handler.resourceContainer != null)
                    {
                        float current = handler.resourceContainer.CurrentAmountFloat;
                        float capacity = handler.resourceContainer.CapacityFloat;
                        int percent = Mathf.RoundToInt((current / capacity) * 100f);
                        boostFeedbackText.text = $"{boostRefillMessage} {percent}%";
                        break;
                    }
                }
            }
        }

        private void PerformBoostRefill()
        {
            if (activeEngines == null) return;

            float amount = boostFuelPerSecond * Time.deltaTime;

            foreach (var handler in activeEngines.BoostResourceHandlers)
            {
                if (handler != null && handler.resourceContainer != null)
                {
                    handler.resourceContainer.AddRemove(amount);
                }
            }
        }

        // ── Hull Health Refill ───────────────────────────────────────────

        /// <summary>
        /// Checks if all hull Damageables are at full health.
        /// </summary>
        private bool IsHullHealthFull()
        {
            if (activeHullDamageables == null) return false;

            foreach (var d in activeHullDamageables)
            {
                if (d != null && d.CurrentHealth < d.HealthCapacity)
                    return false;
            }
            return true;
        }

        private void UpdateHealthFeedbackUI()
        {
            if (healthFeedbackText != null && activeHullDamageables != null)
            {
                healthFeedbackText.gameObject.SetActive(true);

                // Show aggregate health percentage across all hull Damageables
                float totalCurrent = 0f;
                float totalCapacity = 0f;
                foreach (var d in activeHullDamageables)
                {
                    if (d != null)
                    {
                        totalCurrent += d.CurrentHealth;
                        totalCapacity += d.HealthCapacity;
                    }
                }
                int percent = totalCapacity > 0 ? Mathf.RoundToInt((totalCurrent / totalCapacity) * 100f) : 100;
                healthFeedbackText.text = $"{healthRefillMessage} {percent}%";
            }
        }

        private void PerformHealthRefill()
        {
            if (activeHullDamageables == null) return;

            // Only perform actual healing on the host (StateAuthority).
            // The client shows UI but doesn't modify health — NetworkedHealthSync
            // will propagate the host's health changes to the client automatically.
            if (activeHealthNetObj != null && !activeHealthNetObj.HasStateAuthority) return;

            float healAmount = healthPerSecond * Time.deltaTime;

            foreach (var d in activeHullDamageables)
            {
                if (d != null && d.CurrentHealth < d.HealthCapacity)
                {
                    d.Heal(healAmount);
                }
            }
        }

    }
}
