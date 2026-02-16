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
        // Steering (broken down to floats to fix serialization issue)
        public float steerPitch;
        public float steerYaw;
        public float steerRoll;
        
        // Movement (broken down to floats)
        public float moveX;
        public float moveY;
        public float moveZ; // Throttle
        
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
        
        // Diagnostic
        public int magicNumber;
    }
    
    /// <summary>
    /// Collects local player input and provides it to Fusion's input system.
    /// Mirrors the input collection from PlayerInput_InputSystem_SpaceshipControls.
    /// </summary>
    public class NetworkedPlayerInput : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Settings")]
        [SerializeField] private bool enableAutoForward = false;
        
        private PlayerInputData _inputData;
        
        private void Update()
        {
            var networkManager = NetworkManager.Instance;
            if (networkManager == null || !networkManager.IsConnected) return;
            CollectInput();
        }
        
        private void CollectInput()
        {
            // --- STEERING ---
            // Mouse provides pitch (vertical) and yaw (horizontal) via screen-position mode,
            // matching SCK's PlayerInput_Base_SpaceshipControls ScreenPosition behavior.
            // Keyboard Q/E provides roll. Arrow keys as fallback for pitch/yaw if no mouse.
            //
            // IMPORTANT: Mouse position is ALWAYS somewhere on screen, even when the user
            // isn't actively steering with it. We must only use mouse steering when the mouse
            // is inside the game window AND the window has focus. Otherwise keyboard steering
            // would be overridden by stale mouse position (causing random spinning).

            float pitch = 0f;
            float yaw = 0f;
            float roll = 0f;
            bool mouseProvidedSteering = false;

            // Mouse screen-position steering (SCK ScreenPosition mode)
            // Only use mouse when: window is focused, screen size is valid, mouse is inside game window
            if (Mouse.current != null && Application.isFocused
                && Screen.width > 100 && Screen.height > 100)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();

                // CRITICAL: Only process mouse if it's actually inside the game window.
                // In the Unity Editor, Mouse.current.position can return coordinates outside
                // the Game View (e.g., hovering over Console or Inspector), which would produce
                // wild steering values and cause the ship to spin randomly.
                bool mouseInsideWindow = mousePos.x >= 0 && mousePos.x <= Screen.width
                                      && mousePos.y >= 0 && mousePos.y <= Screen.height;

                // Debug mouse diagnostics
                if (Time.frameCount % 60 == 0) // Log once per second
                {
                     Debug.Log($"[NetworkedPlayerInput] MousePos: {mousePos}, Screen: {Screen.width}x{Screen.height}, Inside: {mouseInsideWindow}, Focused: {Application.isFocused}");
                }

                if (mouseInsideWindow)
                {
                    // Convert to viewport-centered coords: -0.5 to +0.5
                    float viewportX = (mousePos.x / Screen.width) - 0.5f;
                    float viewportY = (mousePos.y / Screen.height) - 0.5f;

                    // Dead zone and max distance (matches SCK defaults)
                    float mouseDeadRadius = 0.1f;
                    float maxDistance = 0.475f;

                    float magnitude = new Vector2(viewportX, viewportY).magnitude;

                    if (magnitude > mouseDeadRadius)
                    {
                        // Normalize the input from dead zone edge to max distance
                        float amount = Mathf.Clamp01((magnitude - mouseDeadRadius) / (maxDistance - mouseDeadRadius));
                        Vector2 direction = new Vector2(viewportX, viewportY).normalized;

                        // SCK convention: -viewportY = pitch (mouse up = pitch up), viewportX = yaw
                        pitch = -direction.y * amount;
                        yaw = direction.x * amount;

                        pitch = Mathf.Clamp(pitch, -1f, 1f);
                        yaw = Mathf.Clamp(yaw, -1f, 1f);
                        mouseProvidedSteering = true;
                        
                        // Debug active steering
                        if (amount > 0) Debug.Log($"[NetworkedPlayerInput] Mouse Steering: Pitch={pitch:F2}, Yaw={yaw:F2}");
                    }
                }
            }

            // Keyboard roll (Q/E) — always active
            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) roll = 1f;
                else if (Keyboard.current.eKey.isPressed) roll = -1f;

                // Arrow keys as fallback only if mouse didn't provide steering
                if (!mouseProvidedSteering)
                {
                    if (Keyboard.current.upArrowKey.isPressed) pitch = 1f;
                    else if (Keyboard.current.downArrowKey.isPressed) pitch = -1f;

                    if (Keyboard.current.rightArrowKey.isPressed) yaw = 1f;
                    else if (Keyboard.current.leftArrowKey.isPressed) yaw = -1f;
                }
            }

            _inputData.steerPitch = pitch;
            _inputData.steerYaw = yaw;
            _inputData.steerRoll = roll;
            
            // Movement (throttle and strafe)
            float throttle = 0f;
            float strafeX = 0f;
            float strafeY = 0f;
            
            if (Keyboard.current != null)
            {
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
                
                // Debug raw collection
                if (throttle != 0) Debug.Log($"[NetworkedPlayerInput] CollectInput (Instance {this.GetInstanceID()}): Throttle {throttle}");

                
                // Strafe
                if (Keyboard.current.aKey.isPressed) strafeX = -1f;
                else if (Keyboard.current.dKey.isPressed) strafeX = 1f;
            }
            
            _inputData.moveX = strafeX;
            _inputData.moveY = strafeY;
            _inputData.moveZ = throttle;
            
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
            
            // Diagnostic: Set valid flag
            _inputData.magicNumber = 42;
        }
        
        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            // Debug frequency limiter for OnInput
            if (Time.frameCount % 60 == 0 && Mathf.Abs(_inputData.moveZ) > 0)
            {
                Debug.Log($"[NetworkedPlayerInput] OnInput (Instance {this.GetInstanceID()}): Client {runner.LocalPlayer} Sending Throttle {_inputData.moveZ}");
            }

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
