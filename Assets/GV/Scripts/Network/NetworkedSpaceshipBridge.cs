using UnityEngine;
using Fusion;
using VSX.Engines3D;
using VSX.CameraSystem;
using VSX.Vehicles;
using VSX.VehicleCombatKits;

namespace GV.Network
{
    /// <summary>
    /// Bridge between Fusion network input and SpaceCombatKit's VehicleEngines3D.
    /// On host/server: reads networked input and applies to engines.
    /// On clients: disables local input and just receives synced physics state.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkedSpaceshipBridge : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private VehicleEngines3D engines;
        [SerializeField] private VSX.Weapons.TriggerablesManager triggerablesManager;
        
        [Header("Input Scripts to Disable on Remote")]
        [SerializeField] private MonoBehaviour[] localInputScripts;
        
        // Local state for visual feedback (not networked for now)
        private bool _isBoosting;
        public bool IsBoosting => _isBoosting;
        
        // Flag to track if we've done the initial authority check
        private bool _hasCheckedAuthority = false;
        
        public override void Spawned()
        {
            // Find engines if not assigned
            if (engines == null)
            {
                engines = GetComponentInChildren<VehicleEngines3D>();
            }

            if (triggerablesManager == null)
            {
                triggerablesManager = GetComponentInChildren<VSX.Weapons.TriggerablesManager>();
            }
            
            Debug.Log($"[NetworkedSpaceshipBridge] Spawned - will check authority on first network tick");

            // --- ADDED: Camera Setup since we are on the client ---
            if (Object.HasInputAuthority)
            {
                Debug.Log($"[NetworkedSpaceshipBridge] We have InputAuthority! Setting up camera.");
                SetupCamera();
            }
        }

        private void SetupCamera()
        {
             // Find the Vehicle component on the spawned player
            var vehicle = GetComponentInChildren<Vehicle>(true);
            if (vehicle == null)
            {
                Debug.LogWarning("[NetworkedSpaceshipBridge] No Vehicle found on spawned player!");
                return;
            }
            
            // Find the VehicleCamera in the scene (SpaceCombatKit's camera system)
            var vehicleCamera = FindFirstObjectByType<VehicleCamera>();
            if (vehicleCamera != null)
            {
                vehicleCamera.SetVehicle(vehicle);
                Debug.Log($"[NetworkedSpaceshipBridge] VehicleCamera now following: {vehicle.name}");
                return;
            }
             // Fallback: try generic CameraEntity
            var cameraEntity = FindFirstObjectByType<CameraEntity>();
            if (cameraEntity != null)
            {
                var cameraTarget = GetComponentInChildren<CameraTarget>(true);
                if (cameraTarget != null)
                {
                    cameraEntity.SetCameraTarget(cameraTarget);
                    Debug.Log($"[NetworkedSpaceshipBridge] CameraEntity now following: {name}");
                    return;
                }
            }

            Debug.LogWarning("[NetworkedSpaceshipBridge] No camera system found for local player!");
        }
        
        private void CheckAuthorityAndSetupInput()
        {
            if (_hasCheckedAuthority) return;
            _hasCheckedAuthority = true;
            
            // Check if this is the local player
            bool isLocalPlayer = Object.HasInputAuthority;
            
            // Alternative check: compare the input authority player to the local player
            if (Runner != null && Object.InputAuthority != PlayerRef.None)
            {
                isLocalPlayer = Object.InputAuthority == Runner.LocalPlayer;
            }
            
            Debug.Log($"[NetworkedSpaceshipBridge] Authority Check - HasInputAuthority: {Object.HasInputAuthority}, " +
                      $"InputAuthority: {Object.InputAuthority}, LocalPlayer: {Runner?.LocalPlayer}, " +
                      $"HasStateAuthority: {Object.HasStateAuthority}");
            
            // Disable local input scripts on remote players
            if (!isLocalPlayer)
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Remote player - disabling local input");
                DisableLocalInput();
            }
            else
            {
                Debug.Log($"[NetworkedSpaceshipBridge] Local player - keeping input enabled");
            }
        }
        
        private void DisableLocalInput()
        {
            // Disable specified input scripts
            if (localInputScripts != null)
            {
                foreach (var script in localInputScripts)
                {
                    if (script != null) script.enabled = false;
                }
            }
            
            // Also try to find common input script types and disable them via gameObject
            var spaceshipInput = GetComponentInChildren<VSX.SpaceCombatKit.PlayerInput_Base_SpaceshipControls>(true);
            if (spaceshipInput != null)
            {
                // Cast to Behaviour to access enabled property
                ((UnityEngine.Behaviour)spaceshipInput).enabled = false;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            // Check authority on first tick (after it's been assigned)
            CheckAuthorityAndSetupInput();
            
            // Only run input processing on state authority (host for spawned players)
            if (!Object.HasStateAuthority) return;
            if (engines == null) return;
            
            // For LOCAL player: let the original input scripts handle everything
            // This prevents overwriting what PlayerInput_InputSystem_SpaceshipControls sets
            if (Object.HasInputAuthority)
            {
                return; // Local player uses native input scripts
            }
            
            // For REMOTE players: apply networked input to engines
            var input = GetInput<PlayerInputData>();
            if (!input.HasValue) return;
            
            var data = input.Value;
            
            // Apply input to SpaceCombatKit engines
            // steering: x=pitch, y=yaw, z=roll (already in correct format from NetworkedPlayerInput)
            engines.SetSteeringInputs(data.steering);
            
            // movement: x=strafe X, y=strafe Y, z=throttle (already in correct format)
            engines.SetMovementInputs(data.movement);
            
            // boostInputs
            _isBoosting = data.boost;
            engines.SetBoostInputs(data.boost ? new Vector3(0f, 0f, 1f) : Vector3.zero);

            // Weapon Inputs
            if (triggerablesManager != null)
            {
                // Primary Fire (Button 0)
                if (data.buttons.IsSet(PlayerInputData.BUTTON_FIRE_PRIMARY))
                {
                    triggerablesManager.StartTriggeringAtIndex(0);
                }
                else
                {
                    triggerablesManager.StopTriggeringAtIndex(0);
                }

                // Secondary Fire (Button 1)
                if (data.buttons.IsSet(PlayerInputData.BUTTON_FIRE_SECONDARY))
                {
                    triggerablesManager.StartTriggeringAtIndex(1);
                }
                else
                {
                    triggerablesManager.StopTriggeringAtIndex(1);
                }

                // Missile Fire (Button 2)
                // Assuming missile is index 2 or handled separately - checking Button 2
                 if (data.buttons.IsSet(PlayerInputData.BUTTON_FIRE_MISSILE))
                {
                    // If missiles are a distinct trigger index, e.g. 2
                    triggerablesManager.StartTriggeringAtIndex(2);
                }
                else
                {
                    triggerablesManager.StopTriggeringAtIndex(2);
                }
            }
        }
        
        public override void Render()
        {
            // Visual feedback can be done here for all clients
            // e.g., boost effects, engine sounds based on IsBoosting
        }
    }
}
