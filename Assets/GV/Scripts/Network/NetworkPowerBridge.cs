using UnityEngine;
using Fusion;
using GV.PowerUps;
using GV.Network;
using VSX.Engines3D;

namespace GV
{
    /// <summary>
    /// NetworkBehaviour placed on the player ship prefab.
    /// Bridges power activation requests from the local client (InputAuthority)
    /// to the host (StateAuthority), where the actual network handlers live.
    ///
    /// This is necessary because all power handlers (NetworkShieldHandler,
    /// InvisibilityHandler, AircraftSuperBoostHandler, NetworkSuperWeaponHandler)
    /// guard their activation with HasStateAuthority checks.
    /// </summary>
    public class NetworkPowerBridge : NetworkBehaviour
    {
        // =====================================================================
        // PUBLIC API — called by PowerSphereMasterController on the local machine
        // =====================================================================

        /// <summary>
        /// Routes a power activation to the host. If this machine IS the host
        /// (has StateAuthority), executes immediately. Otherwise sends an RPC.
        /// </summary>
        public void RequestActivatePower(PowerUpType type)
        {
            if (Object.HasStateAuthority)
            {
                // Host player: activate directly
                ExecutePowerActivation(type);
            }
            else
            {
                // Client player: ask the host to activate
                RPC_RequestActivatePower(type);
            }
        }

        // =====================================================================
        // RPC — Client (InputAuthority) → Host (StateAuthority)
        // =====================================================================

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestActivatePower(PowerUpType type)
        {
            // This runs on the host, which has StateAuthority on this player ship.
            Debug.Log($"[NetworkPowerBridge] Host received activation request for {type} from client on {gameObject.name}");
            ExecutePowerActivation(type);
        }

        // =====================================================================
        // EXECUTION — runs on the host (StateAuthority) to call the real handlers
        // =====================================================================

        private void ExecutePowerActivation(PowerUpType type)
        {
            var master = PowerSphereMasterController.Instance;
            if (master == null)
            {
                Debug.LogError("[NetworkPowerBridge] No PowerSphereMasterController.Instance — cannot activate power.");
                return;
            }

            switch (type)
            {
                case PowerUpType.Shield:
                    var shieldHandler = GetComponentInChildren<NetworkShieldHandler>();
                    if (shieldHandler != null)
                    {
                        shieldHandler.ActivateShield(master.shieldSettings.duration);
                        Debug.Log($"[NetworkPowerBridge] Activated Shield on {gameObject.name}");
                    }
                    else Debug.LogError($"[NetworkPowerBridge] NetworkShieldHandler missing on {gameObject.name}");
                    break;

                case PowerUpType.Invisibility:
                    var invHandler = GetComponentInChildren<InvisibilityHandler>();
                    if (invHandler != null)
                    {
                        invHandler.ActivateInvisibility(
                            master.invisibilitySettings.glassMaterial,
                            master.invisibilitySettings.duration,
                            master.invisibilitySettings.revertOnExit
                        );
                        Debug.Log($"[NetworkPowerBridge] Activated Invisibility on {gameObject.name}");
                    }
                    else Debug.LogError($"[NetworkPowerBridge] InvisibilityHandler missing on {gameObject.name}");
                    break;

                case PowerUpType.SuperBoost:
                    var boostHandler = GetComponentInChildren<AircraftSuperBoostHandler>();
                    if (boostHandler != null)
                    {
                        boostHandler.ActivateSuperBoost(
                            master.superBoostSettings.speedMultiplier,
                            master.superBoostSettings.steeringMultiplier,
                            master.superBoostSettings.boostMultiplier,
                            master.superBoostSettings.boostDuration
                        );
                        Debug.Log($"[NetworkPowerBridge] Activated SuperBoost on {gameObject.name}");
                    }
                    else Debug.LogError($"[NetworkPowerBridge] AircraftSuperBoostHandler missing on {gameObject.name}");
                    break;

                case PowerUpType.SuperWeapon:
                    var swHandler = GetComponentInChildren<NetworkSuperWeaponHandler>();
                    if (swHandler != null)
                    {
                        AircraftCharacterManager.SuperWeaponBonuses bonuses = new AircraftCharacterManager.SuperWeaponBonuses
                        {
                            projectileDamage = master.superWeaponSettings.projectileDamageMultiplier,
                            projectileRange = master.superWeaponSettings.projectileRangeMultiplier,
                            projectileSpeed = master.superWeaponSettings.projectileSpeedMultiplier,
                            projectileFireRate = master.superWeaponSettings.projectileFireRateMultiplier,
                            projectileReload = master.superWeaponSettings.projectileReloadMultiplier,
                            missileDamage = master.superWeaponSettings.missileDamageMultiplier,
                            missileRange = master.superWeaponSettings.missileRangeMultiplier,
                            missileSpeed = master.superWeaponSettings.missileSpeedMultiplier,
                            missileFireRate = master.superWeaponSettings.missileFireRateMultiplier,
                            missileReload = master.superWeaponSettings.missileReloadMultiplier
                        };
                        swHandler.ActivateSuperWeapon(master.superWeaponSettings.duration, bonuses);
                        Debug.Log($"[NetworkPowerBridge] Activated SuperWeapon on {gameObject.name}");
                    }
                    else Debug.LogError($"[NetworkPowerBridge] NetworkSuperWeaponHandler missing on {gameObject.name}");
                    break;

                case PowerUpType.Teleport:
                    // TeleportPowerUp lives on the sphere, not the player.
                    // Find any available instance in the scene — settings are pulled
                    // from PowerSphereMasterController anyway.
                    var tp = FindObjectOfType<TeleportPowerUp>();
                    if (tp != null)
                    {
                        tp.Apply(gameObject);
                        Debug.Log($"[NetworkPowerBridge] Activated Teleport on {gameObject.name}");
                    }
                    else Debug.LogError("[NetworkPowerBridge] No TeleportPowerUp found in scene");
                    break;
            }
        }
    }
}
