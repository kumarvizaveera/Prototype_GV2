using UnityEngine;
using Fusion;
using VSX.Engines3D;

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
        
        [Header("Input Scripts to Disable on Remote")]
        [SerializeField] private MonoBehaviour[] localInputScripts;
        
        // Local state for visual feedback (not networked for now)
        private bool _isBoosting;
        public bool IsBoosting => _isBoosting;
        
        public override void Spawned()
        {
            // Find engines if not assigned
            if (engines == null)
            {
                engines = GetComponentInChildren<VehicleEngines3D>();
            }
            
            // Check if this is the local player using multiple methods
            bool isLocalPlayer = Object.HasInputAuthority;
            
            // Alternative check: compare the input authority player to the local player
            if (Runner != null && Object.InputAuthority != PlayerRef.None)
            {
                isLocalPlayer = Object.InputAuthority == Runner.LocalPlayer;
            }
            
            Debug.Log($"[NetworkedSpaceshipBridge] Spawned - HasInputAuthority: {Object.HasInputAuthority}, " +
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
            // Only run input processing on state authority (host for spawned players)
            if (!Object.HasStateAuthority) return;
            if (engines == null) return;
            
            // Get input from Fusion
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
        }
        
        public override void Render()
        {
            // Visual feedback can be done here for all clients
            // e.g., boost effects, engine sounds based on IsBoosting
        }
    }
}

