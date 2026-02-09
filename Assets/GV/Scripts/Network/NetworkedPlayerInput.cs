using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace GV.Network
{
    /// <summary>
    /// Input data structure sent over the network.
    /// </summary>
    public struct PlayerInputData : INetworkInput
    {
        // Steering: x = pitch, y = yaw, z = roll (matches VehicleEngines3D steeringInputs)
        public Vector3 steering;
        
        // Movement: x = strafe horizontal, y = strafe vertical, z = throttle
        public Vector3 movement;
        
        // Boost
        public NetworkBool boost;
        
        // Weapons
        public NetworkButtons buttons;
        
        public const int BUTTON_FIRE_PRIMARY = 0;
        public const int BUTTON_FIRE_SECONDARY = 1;
        public const int BUTTON_FIRE_MISSILE = 2;
        public const int BUTTON_CYCLE_WEAPON = 3;
        public const int BUTTON_CYCLE_CHARACTER = 4;
        public const int BUTTON_SWAP_AIRCRAFT = 5;
    }
    
    /// <summary>
    /// Collects local player input and provides it to Fusion's input system.
    /// Mirrors the input collection from PlayerInput_InputSystem_SpaceshipControls.
    /// </summary>
    public class NetworkedPlayerInput : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Settings")]
        [SerializeField] private bool enableAutoForward = true;
        
        private PlayerInputData _inputData;
        
        private void Update()
        {
            var networkManager = NetworkManager.Instance;
            if (networkManager == null || !networkManager.IsConnected) return;
            CollectInput();
        }
        
        private void CollectInput()
        {
            // Get mouse/gamepad steering input
            Vector2 rawSteer = Vector2.zero;
            if (Mouse.current != null)
            {
                // For mouse steering, typically use screen position delta or direct values
                // For now, use keyboard/gamepad as primary
            }
            
            if (Keyboard.current != null)
            {
                // Arrow keys for steering
                float pitch = 0f;
                float yaw = 0f;
                float roll = 0f;
                
                if (Keyboard.current.upArrowKey.isPressed) pitch = 1f;
                else if (Keyboard.current.downArrowKey.isPressed) pitch = -1f;
                
                if (Keyboard.current.rightArrowKey.isPressed) yaw = 1f;
                else if (Keyboard.current.leftArrowKey.isPressed) yaw = -1f;
                
                if (Keyboard.current.qKey.isPressed) roll = 1f;
                else if (Keyboard.current.eKey.isPressed) roll = -1f;
                
                rawSteer = new Vector2(yaw, pitch);
                _inputData.steering = new Vector3(pitch, yaw, roll);
            }
            
            // Movement (throttle and strafe)
            float throttle = 0f;
            float strafeX = 0f;
            float strafeY = 0f;
            
            if (Keyboard.current != null)
            {
                // Auto-forward mode
                if (enableAutoForward)
                {
                    throttle = 1f; // Always forward
                    if (Keyboard.current.sKey.isPressed) throttle = -1f; // Brake/reverse
                }
                else
                {
                    if (Keyboard.current.wKey.isPressed) throttle = 1f;
                    else if (Keyboard.current.sKey.isPressed) throttle = -1f;
                }
                
                // Strafe
                if (Keyboard.current.aKey.isPressed) strafeX = -1f;
                else if (Keyboard.current.dKey.isPressed) strafeX = 1f;
            }
            
            _inputData.movement = new Vector3(strafeX, strafeY, throttle);
            
            // Boost (shift or W with auto-forward)
            if (Keyboard.current != null)
            {
                if (enableAutoForward)
                {
                    _inputData.boost = Keyboard.current.wKey.isPressed;
                }
                else
                {
                    _inputData.boost = Keyboard.current.leftShiftKey.isPressed;
                }
            }
            
            // Weapon buttons
            _inputData.buttons = default;
            if (Mouse.current != null)
            {
                _inputData.buttons.Set(PlayerInputData.BUTTON_FIRE_PRIMARY, Mouse.current.leftButton.isPressed);
                _inputData.buttons.Set(PlayerInputData.BUTTON_FIRE_SECONDARY, Mouse.current.rightButton.isPressed);
            }
            if (Keyboard.current != null)
            {
                _inputData.buttons.Set(PlayerInputData.BUTTON_FIRE_MISSILE, Keyboard.current.spaceKey.wasPressedThisFrame);
                _inputData.buttons.Set(PlayerInputData.BUTTON_CYCLE_WEAPON, Keyboard.current.tabKey.wasPressedThisFrame);
                _inputData.buttons.Set(PlayerInputData.BUTTON_CYCLE_CHARACTER, Keyboard.current.cKey.wasPressedThisFrame);
                _inputData.buttons.Set(PlayerInputData.BUTTON_SWAP_AIRCRAFT, Keyboard.current.vKey.wasPressedThisFrame);
            }
        }
        
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            input.Set(_inputData);
            _inputData.buttons = default; // Reset one-shot buttons
        }
        
        // Required INetworkRunnerCallbacks implementations (empty stubs)
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
