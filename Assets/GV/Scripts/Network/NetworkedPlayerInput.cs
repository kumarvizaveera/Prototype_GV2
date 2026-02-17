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

        // Virtual reticle position (viewport coords, 0.5 = center).
        // SCK uses mouse DELTA to move a virtual reticle around the screen.
        // The reticle's offset from center determines steer direction/amount.
        // This works with CursorLockMode.Locked (cursor hidden at screen center).
        private Vector2 _reticlePos = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Exposes the latest collected input data so NetworkManager.OnInput() can call
        /// input.Set() directly. This is necessary because NetworkInput is a STRUCT —
        /// delegating OnInput to another method passes a COPY, and Set() on the copy
        /// is invisible to Fusion. The registered callback must call Set() itself.
        /// </summary>
        public PlayerInputData CurrentInputData => _inputData;

        /// <summary>
        /// Called by NetworkManager.OnInput() after reading CurrentInputData and calling input.Set().
        /// Resets one-shot buttons and increments diagnostic counters.
        /// </summary>
        public void NotifyInputConsumed()
        {
            _onInputCalled = true;
            _onInputCallCount++;
            _inputData.buttons = default; // Reset one-shot buttons after they've been sent
        }

        // --- ON-SCREEN DIAGNOSTICS (visible in builds without dev console) ---
        private bool _onInputCalled = false;
        private int _onInputCallCount = 0;
        private bool _collectInputCalled = false;
        private string _debugStatus = "Waiting...";

        private void Update()
        {
            var networkManager = NetworkManager.Instance;
            if (networkManager == null)
            {
                _debugStatus = "NetworkManager.Instance is NULL";
                return;
            }
            if (!networkManager.IsConnected)
            {
                _debugStatus = "Not Connected";
                return;
            }
            _debugStatus = $"Connected. OnInput called: {_onInputCalled} ({_onInputCallCount}x)";
            _collectInputCalled = true;
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

            // Mouse steering using DELTA-BASED ACCUMULATED RETICLE.
            // SCK locks the cursor (CursorLockMode.Locked) so Mouse.current.position is
            // always screen center — reading absolute position gives zero steer forever.
            //
            // Instead, we match SCK's actual approach (PlayerInput_Base_SpaceshipControls):
            // 1. Read mouse DELTA (movement) each frame
            // 2. Accumulate into a virtual reticle position (viewport coords)
            // 3. Reticle offset from center (0.5, 0.5) determines steer direction/amount
            // 4. Reticle drifts back to center when not moving (auto-center)
            //
            // FOCUS GATE: Only read mouse delta when this window has focus.
            // On the same machine, both editor and build receive the same mouse delta.
            // On separate PCs this is irrelevant (each has its own mouse).
            if (Application.isFocused && Mouse.current != null)
            {
                // Read mouse delta (pixels moved this frame)
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();

                // Convert pixel delta to viewport-space delta
                // reticleMovementSpeed controls sensitivity (SCK default varies, 1.0 is a good start)
                float reticleSpeed = 1.0f;
                float deltaX = (mouseDelta.x / Screen.width) * reticleSpeed;
                float deltaY = (mouseDelta.y / Screen.height) * reticleSpeed;

                // Accumulate into virtual reticle position
                _reticlePos.x += deltaX;
                _reticlePos.y += deltaY;

                // Auto-center: drift reticle back to center when mouse isn't moving.
                // This prevents the reticle from getting stuck at an edge.
                float centerSpeed = 2.0f * Time.deltaTime;
                _reticlePos.x = Mathf.Lerp(_reticlePos.x, 0.5f, centerSpeed);
                _reticlePos.y = Mathf.Lerp(_reticlePos.y, 0.5f, centerSpeed);

                // Clamp reticle to viewport bounds
                float maxReticleDistance = 0.4f;
                Vector2 centered = _reticlePos - new Vector2(0.5f, 0.5f);
                // Correct for aspect ratio before clamping (matches SCK)
                float aspect = (float)Screen.width / Screen.height;
                centered.x *= aspect;
                centered = Vector2.ClampMagnitude(centered, maxReticleDistance);
                centered.x /= aspect;
                _reticlePos = centered + new Vector2(0.5f, 0.5f);

                // Compute steer from reticle offset
                Vector2 steerOffset = _reticlePos - new Vector2(0.5f, 0.5f);
                // Re-apply aspect correction for magnitude calculation
                Vector2 corrected = new Vector2(steerOffset.x * aspect, steerOffset.y);
                float magnitude = corrected.magnitude;

                float mouseDeadRadius = 0.02f;
                if (magnitude > mouseDeadRadius)
                {
                    float amount = Mathf.Clamp01((magnitude - mouseDeadRadius) / (maxReticleDistance - mouseDeadRadius));

                    // SCK convention: -Y = pitch (mouse up = pitch up), X = yaw
                    pitch = -steerOffset.y / Mathf.Max(magnitude, 0.001f) * amount;
                    yaw = steerOffset.x / Mathf.Max(magnitude, 0.001f) * amount;
                    pitch = Mathf.Clamp(pitch, -1f, 1f);
                    yaw = Mathf.Clamp(yaw, -1f, 1f);
                    mouseProvidedSteering = true;
                }

                // Debug every ~1 second
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[NetworkedPlayerInput] MouseDelta: delta=({mouseDelta.x:F1},{mouseDelta.y:F1}), " +
                              $"reticle=({_reticlePos.x:F3},{_reticlePos.y:F3}), mag={magnitude:F3}, " +
                              $"steer=({pitch:F2},{yaw:F2}), focused={Application.isFocused}");
                }
            }
            else
            {
                // Not focused: reset reticle to center (no steer)
                _reticlePos = new Vector2(0.5f, 0.5f);
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
            // NOTE: This is no longer called by Fusion directly (NetworkedPlayerInput is not
            // registered via AddCallbacks). It may still be called for diagnostic tracking.
            // The actual input.Set() happens in NetworkManager.OnInput() using CurrentInputData.
            _onInputCalled = true;
            _onInputCallCount++;
            _inputData.buttons = default; // Reset one-shot buttons after NetworkManager reads them
        }

        // On-screen debug display — visible in builds without dev console
        private void OnGUI()
        {
            // Only show on non-editor builds (editor has console)
            if (Application.isEditor) return;

            GUILayout.BeginArea(new Rect(10, 260, 600, 420));
            GUILayout.BeginVertical("box");
            GUILayout.Label("[NetworkedPlayerInput Debug]");
            GUILayout.Label($"Status: {_debugStatus}");
            // FOCUS DIAGNOSTIC: Shows whether Application.isFocused is true (must be true for mouse input)
            GUI.color = Application.isFocused ? Color.green : Color.red;
            GUILayout.Label($"FOCUSED: {Application.isFocused}");
            GUI.color = Color.white;
            // Show mouse delta and reticle position
            string mouseInfo = "N/A";
            if (Mouse.current != null)
            {
                var delta = Mouse.current.delta.ReadValue();
                Vector2 reticleOffset = _reticlePos - new Vector2(0.5f, 0.5f);
                mouseInfo = $"delta=({delta.x:F1},{delta.y:F1}) reticle=({_reticlePos.x:F3},{_reticlePos.y:F3}) offset={reticleOffset.magnitude:F3}";
            }
            GUILayout.Label($"Mouse: {mouseInfo}");
            // Steer values — these are what get sent via RPC
            GUI.color = (_inputData.steerPitch != 0 || _inputData.steerYaw != 0) ? Color.green : Color.yellow;
            GUILayout.Label($"Throttle: {_inputData.moveZ:F2}, Steer: ({_inputData.steerPitch:F2}, {_inputData.steerYaw:F2})");
            GUI.color = Color.white;
            GUILayout.Label($"Magic: {_inputData.magicNumber}");
            var nm = NetworkManager.Instance;
            if (nm != null && nm.Runner != null)
            {
                GUILayout.Label($"Runner.IsClient: {nm.Runner.IsClient}, IsServer: {nm.Runner.IsServer}");
                GUILayout.Label($"LocalPlayer: {nm.Runner.LocalPlayer}");
            }
            // --- RAW SEND DIAGNOSTICS ---
            if (nm != null)
            {
                GUI.color = nm.RawSendCount > 0 ? Color.green : Color.red;
                GUILayout.Label($"RAW SEND: ok={nm.RawSendCount}, errs={nm.RawSendErrorCount}");
                GUILayout.Label($"RAW BLOCK: {nm.RawSendBlockReason}");
                if (!string.IsNullOrEmpty(nm.RawSendError))
                {
                    GUI.color = Color.red;
                    GUILayout.Label($"RAW ERROR: {nm.RawSendError}");
                    // Show first line of stack trace to identify WHERE the null ref happens
                    if (!string.IsNullOrEmpty(nm.RawSendErrorStack))
                    {
                        string firstLine = nm.RawSendErrorStack.Split('\n')[0];
                        GUILayout.Label($"STACK: {firstLine}");
                    }
                }
                GUI.color = Color.white;
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
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
