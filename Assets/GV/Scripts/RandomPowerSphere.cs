using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GV;
using GV.PowerUps;
using GV.Network; // Added namespace for handlers
using VSX.Engines3D;
using Fusion;

namespace GV
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(NetworkObject))]
    public class RandomPowerSphere : NetworkBehaviour
    {
        [Header("Power Up Components")]
        [Tooltip("Reference to the Teleport component on this object.")]
        public TeleportPowerUp teleport;
        [Tooltip("Reference to the Invisibility component on this object.")]
        public InvisibilityPowerUp invisibility;
        [Tooltip("Reference to the Shield component on this object.")]
        public ShieldPowerUp shield;
        [Tooltip("Reference to the Super Boost component on this object.")]
        public SuperBoostOrb superBoost;
        [Tooltip("Reference to the Super Weapon component on this object.")]
        public SuperWeaponOrb superWeapon;

        [Header("Settings")]
        [Tooltip("Cooldown in seconds before the sphere can be collected again after pickup.")]
        public float cooldownAfterPickup = 5f;
        [Tooltip("Per-player cooldown to prevent the same player from collecting again too quickly.")]
        public float perPlayerCooldown = 2f;

        [Header("Cycling Settings")]
        [Tooltip("If true, powers cycle every few seconds instead of being random.")]
        public bool cyclePowers = true;
        [Tooltip("Time in seconds between power switches.")]
        public float cycleInterval = 5f;

        [Tooltip("List of text objects to display the current power.")]
        public TMPro.TMP_Text[] powerLabels;
        
        // Static list to ensure all spheres share the same random order per game.
        private static List<PowerUpType> s_GlobalCycleOrder;

        private List<PowerUpType> m_AvailableCyclePowers = new List<PowerUpType>();
        private int m_CurrentCycleIndex = 0;
        private Coroutine m_CycleRoutine;

        [Header("Feedback")]
        public AudioClip mysteryPickupSound;
        [Tooltip("Optional: Instantiate this GameObject when collected (e.g. for audio prefab).")]
        public GameObject mysteryPickupSoundObject;
        public GameObject mysteryPickupEffect;

        private Collider m_Collider;
        private Renderer[] m_Renderers;

        // Track per-player cooldowns to prevent spam collection
        private Dictionary<NetworkId, float> m_PlayerCooldowns = new Dictionary<NetworkId, float>();

        [Networked] public NetworkBool IsActive { get; set; } = true;
        [Networked] public PowerUpType CurrentCyclePower { get; set; }

        private ChangeDetector _changes;

        public override void Spawned()
        {
            _changes = GetChangeDetector(ChangeDetector.Source.SimulationState);
            
            // Initialize Visuals based on current network state
            SetVisuals(IsActive);
            UpdatePowerLabel();

            if (Object.HasStateAuthority)
            {
                IsActive = true;
                
                // Initialize Cycle Logic on Server
                InitializeCycleLogic();
            }
        }

        public override void Render()
        {
            foreach (var change in _changes.DetectChanges(this))
            {
                if (change == nameof(IsActive))
                {
                    SetVisuals(IsActive);
                }
                if (change == nameof(CurrentCyclePower))
                {
                    UpdatePowerLabel();
                }
            }
        }

        private void Awake()
        {
            m_Collider = GetComponent<Collider>();
            m_Renderers = GetComponentsInChildren<Renderer>();
            
            // Validate components
            if (!teleport && !invisibility && !shield && !superBoost && !superWeapon)
            {
                Debug.LogWarning("[RandomPowerSphere] No power-up components assigned! Trying to find them on this object.");
                teleport = GetComponent<TeleportPowerUp>();
                invisibility = GetComponent<InvisibilityPowerUp>();
                shield = GetComponent<ShieldPowerUp>();
                superBoost = GetComponent<SuperBoostOrb>();
                superWeapon = GetComponent<SuperWeaponOrb>();
            }

            // Force trigger
            if (m_Collider) m_Collider.isTrigger = true;

            // Ensure they are all set to manual trigger only to avoid double activation
            if (teleport) teleport.manualTriggerOnly = true;
            if (invisibility) invisibility.manualTriggerOnly = true;
            if (shield) shield.manualTriggerOnly = true;
            if (superBoost) superBoost.manualTriggerOnly = true;
            if (superWeapon) superWeapon.manualTriggerOnly = true;
        }

        private void InitializeCycleLogic()
        {
            if (cyclePowers)
            {
                InitGlobalOrder();

                // Populate available powers based on the shared global order
                m_AvailableCyclePowers.Clear();
                
                // Check if Teleport is allowed globally
                bool teleportAllowed = true;
                if (PowerSphereMasterController.Instance != null)
                {
                    teleportAllowed = PowerSphereMasterController.Instance.teleportSettings.allowCycling;
                }

                foreach (PowerUpType type in s_GlobalCycleOrder)
                {
                   switch (type)
                   {
                       case PowerUpType.Teleport:
                           if (teleport && teleportAllowed) m_AvailableCyclePowers.Add(type);
                           break;
                       case PowerUpType.Invisibility:
                           if (invisibility) m_AvailableCyclePowers.Add(type);
                           break;
                       case PowerUpType.Shield:
                           if (shield) m_AvailableCyclePowers.Add(type);
                           break;
                       case PowerUpType.SuperBoost:
                           if (superBoost) m_AvailableCyclePowers.Add(type);
                           break;
                       case PowerUpType.SuperWeapon:
                           if (superWeapon) m_AvailableCyclePowers.Add(type);
                           break;
                   }
                }

                if (m_AvailableCyclePowers.Count > 0)
                {
                    m_CurrentCycleIndex = Random.Range(0, m_AvailableCyclePowers.Count);
                    CurrentCyclePower = m_AvailableCyclePowers[m_CurrentCycleIndex];

                    m_CycleRoutine = StartCoroutine(CyclePowerRoutine());
                }
            }
        }

        private void InitGlobalOrder()
        {
            if (s_GlobalCycleOrder != null) return;

            s_GlobalCycleOrder = new List<PowerUpType>
            {
                PowerUpType.Teleport,
                PowerUpType.Invisibility,
                PowerUpType.Shield,
                PowerUpType.SuperBoost,
                PowerUpType.SuperWeapon
            };

            // Fisher-Yates Shuffle
            for (int i = 0; i < s_GlobalCycleOrder.Count; i++)
            {
                PowerUpType temp = s_GlobalCycleOrder[i];
                int randomIndex = Random.Range(i, s_GlobalCycleOrder.Count);
                s_GlobalCycleOrder[i] = s_GlobalCycleOrder[randomIndex];
                s_GlobalCycleOrder[randomIndex] = temp;
            }
        }

        private IEnumerator CyclePowerRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(cycleInterval);
                m_CurrentCycleIndex = (m_CurrentCycleIndex + 1) % m_AvailableCyclePowers.Count;
                CurrentCyclePower = m_AvailableCyclePowers[m_CurrentCycleIndex];
            }
        }

        private void UpdatePowerLabel()
        {
            if (Object != null && !Object.IsValid) return;

            PowerUpType typeToDisplay = cyclePowers ? CurrentCyclePower : PowerUpType.None; 

            if (cyclePowers && powerLabels != null)
            {
                string text = typeToDisplay.ToString();
                foreach (var label in powerLabels)
                {
                    if (label != null) label.text = text;
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            // Debug Log for detection
            Debug.Log($"[RandomPowerSphere] OnTriggerEnter hit by {other.name} on {(Object != null && Object.HasStateAuthority ? "SERVER" : "CLIENT")}");

            // Only the server authorities trigger pickups
            if (!Object || !Object.HasStateAuthority) return;

            if (!IsActive) return;

            // Find the vehicle root
            Rigidbody rb = other.attachedRigidbody;
            if (!rb) return;

            GameObject target = rb.gameObject;

            // Check if player
            if (!target.CompareTag("Player") && !target.transform.root.CompareTag("Player"))
            {
                 return;
            }

            // PowerUpManager Logic (Optional for Server, mainly for local tracking/unlocks)
            // But we can invoke it if it exists.
            if (PowerUpManager.Instance == null)
            {
                 // Create one? 
                 // If we rely on PowerUpManager for randomness, we need it.
            }
            // For now assume logic is self-contained or handled via cycling.

            // Determine candidates if not cycling
            List<PowerUpType> candidates = new List<PowerUpType>();
            if (teleport) candidates.Add(PowerUpType.Teleport);
            if (invisibility) candidates.Add(PowerUpType.Invisibility);
            if (shield) candidates.Add(PowerUpType.Shield);
            if (superBoost) candidates.Add(PowerUpType.SuperBoost);
            if (superWeapon) candidates.Add(PowerUpType.SuperWeapon);

            PowerUpType selected = PowerUpType.None;

            if (cyclePowers)
            {
                selected = CurrentCyclePower;
                if (selected == PowerUpType.None && m_AvailableCyclePowers.Count > 0) 
                    selected = m_AvailableCyclePowers[0];
            }
            else
            {
                if (PowerUpManager.Instance != null)
                    selected = PowerUpManager.Instance.GetRandomUncollectedPower(candidates);
                else 
                    // Fallback random
                    if (candidates.Count > 0) selected = candidates[Random.Range(0, candidates.Count)];
            }

            if (selected != PowerUpType.None)
            {
                // Check per-player cooldown
                NetworkObject targetNetObj = target.GetComponent<NetworkObject>();
                if (targetNetObj == null) targetNetObj = target.GetComponentInParent<NetworkObject>();
                if (targetNetObj != null)
                {
                    NetworkId playerId = targetNetObj.Id;
                    if (m_PlayerCooldowns.ContainsKey(playerId) && Time.time < m_PlayerCooldowns[playerId])
                    {
                        return; // This player is still on cooldown
                    }
                    m_PlayerCooldowns[playerId] = Time.time + perPlayerCooldown;
                }

                Debug.Log($"[RandomPowerSphere] Mystery Sphere granted: {selected}");

                // IMPORTANT: ApplyPower calls Network Handler methods on the target
                ApplyPower(selected, target);

                // Temporarily deactivate the sphere, then reactivate after cooldown
                // The sphere is NEVER destroyed — it always comes back
                IsActive = false;
                StartCoroutine(CooldownRoutine());
            }
        }

        private void ApplyPower(PowerUpType type, GameObject target)
        {
            // ---------------------------------------------------------------
            // NETWORK-AWARE COLLECTION: Send an RPC so the collecting player's
            // machine (InputAuthority) gets the CollectPower call on their
            // local PowerSphereMasterController, not just the host's.
            // ---------------------------------------------------------------
            NetworkObject targetNetObj = target.GetComponent<NetworkObject>();
            if (targetNetObj == null) targetNetObj = target.GetComponentInParent<NetworkObject>();

            if (targetNetObj != null && PowerSphereMasterController.Instance != null)
            {
                // RPC to all machines — each checks InputAuthority to decide if it's "their" player
                RPC_NotifyPowerCollected(type, targetNetObj.Id);
                Debug.Log($"[RandomPowerSphere] Sent RPC_NotifyPowerCollected({type}) for player {targetNetObj.Id}");
                return;
            }

            // ---------------------------------------------------------------
            // FALLBACK: If no PowerSphereMasterController exists, activate
            // immediately (legacy behavior for backwards compatibility).
            // ---------------------------------------------------------------
            Debug.LogWarning("[RandomPowerSphere] No PowerSphereMasterController found — falling back to immediate activation.");

            NetworkObject netObj = target.GetComponent<NetworkObject>();
            if (netObj == null) netObj = target.GetComponentInParent<NetworkObject>();

            if (netObj != null)
            {
                Debug.Log($"[RandomPowerSphere] Applying {type} to NetworkObject {netObj.name} (ID: {netObj.Id})");

                switch (type)
                {
                    case PowerUpType.Teleport:
                        if (teleport) teleport.Apply(netObj.gameObject);
                        break;

                    case PowerUpType.Invisibility:
                        if (invisibility)
                        {
                            var invHandler = netObj.GetComponentInChildren<InvisibilityHandler>();
                            if (invHandler)
                            {
                                invHandler.ActivateInvisibility(invisibility.glassMaterial, invisibility.duration, invisibility.revertOnExit);
                            }
                        }
                        break;

                    case PowerUpType.Shield:
                        if (shield)
                        {
                            var shieldHandler = netObj.GetComponentInChildren<NetworkShieldHandler>();
                            if (shieldHandler) shieldHandler.ActivateShield(shield.duration);
                        }
                        break;

                    case PowerUpType.SuperBoost:
                        if (superBoost)
                        {
                            var boostHandler = netObj.GetComponentInChildren<AircraftSuperBoostHandler>();
                            if (boostHandler) boostHandler.ActivateSuperBoost(superBoost.speedMultiplier, superBoost.steeringMultiplier, superBoost.boostMultiplier, superBoost.boostDuration);
                        }
                        break;

                    case PowerUpType.SuperWeapon:
                        if (superWeapon)
                        {
                            var swHandler = netObj.GetComponentInChildren<NetworkSuperWeaponHandler>();
                            if (swHandler)
                            {
                                AircraftCharacterManager.SuperWeaponBonuses bonuses = new AircraftCharacterManager.SuperWeaponBonuses
                                {
                                    projectileDamage = superWeapon.projectileDamageMultiplier,
                                    projectileRange = superWeapon.projectileRangeMultiplier,
                                    projectileSpeed = superWeapon.projectileSpeedMultiplier,
                                    projectileFireRate = superWeapon.projectileFireRateMultiplier,
                                    projectileReload = superWeapon.projectileReloadMultiplier,
                                    missileDamage = superWeapon.missileDamageMultiplier,
                                    missileRange = superWeapon.missileRangeMultiplier,
                                    missileSpeed = superWeapon.missileSpeedMultiplier,
                                    missileFireRate = superWeapon.missileFireRateMultiplier,
                                    missileReload = superWeapon.missileReloadMultiplier
                                };
                                swHandler.ActivateSuperWeapon(superWeapon.duration, bonuses);
                            }
                        }
                        break;
                }
            }
            else
            {
                Debug.LogWarning("[RandomPowerSphere] Target has no NetworkObject! Falling back to local Apply.");
                switch (type)
                {
                    case PowerUpType.Shield: if (shield) shield.Apply(target); break;
                }
            }
        }

        // =====================================================================
        // NETWORK RPC — notifies all machines about a power collection.
        // Only the machine with InputAuthority on the player processes it.
        // =====================================================================

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_NotifyPowerCollected(PowerUpType type, NetworkId playerNetId)
        {
            if (Runner.TryFindObject(playerNetId, out NetworkObject playerObj))
            {
                // Only the machine that "owns" this player (InputAuthority) should
                // update its local PowerSphereMasterController inventory & UI.
                if (playerObj.HasInputAuthority)
                {
                    if (PowerSphereMasterController.Instance != null)
                    {
                        TeleportPowerUp teleportRef = (type == PowerUpType.Teleport) ? teleport : null;
                        PowerSphereMasterController.Instance.CollectPower(type, playerObj.gameObject, teleportRef);
                        Debug.Log($"[RandomPowerSphere] RPC: Local player collected {type}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[RandomPowerSphere] RPC_NotifyPowerCollected: Could not find NetworkObject with ID {playerNetId}");
            }
        }

        private IEnumerator CooldownRoutine()
        {
            yield return new WaitForSeconds(cooldownAfterPickup);
            IsActive = true;
            Debug.Log("[RandomPowerSphere] Sphere reactivated after cooldown — ready for collection again.");
        }

        private void SetVisuals(bool active)
        {
            if (m_Collider) m_Collider.enabled = active;
            foreach (var r in m_Renderers) if (r) r.enabled = active;
            
            // Pickup Effect Logic
            // If active becomes false, spawn effect
            // Note: This function is called in Render() and Spawned().
            // We need to track previous state to detect transition if we want to spawn effect here.
            // Or use ChangeDetector in Render loop explicitly.
            
            // For now, let's keep it simple.
        }
    }
}
