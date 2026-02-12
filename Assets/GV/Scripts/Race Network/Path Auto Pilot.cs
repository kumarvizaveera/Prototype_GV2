using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using Fusion;

public class PathAutoPilot : NetworkBehaviour
{
    [Header("Refs")]
    public Rigidbody rb;
    public CheckpointNetwork network;

    [Header("Disable player controls while fully active")]
    public MonoBehaviour[] disableWhileActive;

    [Header("Path Following")]
    public float arriveRadius = 3f;
    public bool alignRotationToPath = true;
    public float rotationLerp = 10f;

    [Header("Speed / Steering")]
    [Tooltip("How quickly velocity is blended toward desired velocity (higher = snappier).")]
    public float velocityLerp = 8f;

    [Header("Cancel on player input")]
    public bool cancelOnAnyInput = true;
    public float axisThreshold = 0.15f;
    public string[] legacyAxesToCheck = new string[] { "Horizontal", "Vertical", "Mouse X", "Mouse Y" };
    
    [Tooltip("Time in seconds to ignore input after activation.")]
    public float inputDelay = 1.0f;

    [Header("Easing (smooth handoff)")]
    [Tooltip("Seconds to fade autopilot influence from 1 -> 0 while player regains control.")]
    public float easeOutSeconds = 0.75f;

    [Tooltip("Curve where X=0..1 (time), Y=1..0 (autopilot weight).")]
    public AnimationCurve easeOutCurve = null;

    [Tooltip("If true, keep a small guidance while easing; if false, autopilot stops immediately and only delays control enable.")]
    public bool guideDuringEaseOut = true;

    [Header("UI")]
    public TMPro.TMP_Text statusText;
    [Tooltip("Format for the timer text. Use {0} for the time remaining.")]
    public string uiFormat = "Auto Pilot ending in ({0:0.0}) seconds";

    // runtime
    bool _active;
    bool _easing;
    float _endTime;
    float _easeStartTime;
    float _inputUnlockTime;

    float _speed;
    int _goalIndex;

    bool _savedWasKinematic;
    Vector3 _lastDir = Vector3.forward;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!network) network = CheckpointNetwork.Instance;
        if (!network) network = FindObjectOfType<CheckpointNetwork>(true);

        if (easeOutCurve == null || easeOutCurve.length == 0)
            easeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
    }

    /// <summary>Start autopilot toward goalIndex for durationSeconds, with target speed.</summary>
    public void Begin(int startGoalIndex, float durationSeconds, float speedMetersPerSec)
    {
        if (!rb) return;

        if (!network)
        {
            network = CheckpointNetwork.Instance;
            if (!network) network = FindObjectOfType<CheckpointNetwork>(true);
        }
        if (!network || network.Count == 0) return;

        _goalIndex = network.ClampOrWrapIndex(startGoalIndex);
        _speed = Mathf.Max(0.1f, speedMetersPerSec);
        _endTime = Time.time + Mathf.Max(0.05f, durationSeconds);
        
        _active = true;
        _easing = false;
        _inputUnlockTime = Time.time + inputDelay;
        
        // UI
        if (statusText) statusText.gameObject.SetActive(true);

        // Disable controls during full autopilot
        SetControlsEnabled(false);
        
        // Save kinematic state (keep whatever you use normally)
        _savedWasKinematic = rb.isKinematic;
        // Autopilot steering works best with dynamic RB:
        rb.isKinematic = false;
        rb.WakeUp();

        // SNAP VELOCITY (Fix for "Random" teleport issue)
        // If we are starting from a teleport, our old velocity vector is likely wrong.
        // We calculate the direction to the target and SNAP our velocity to it immediately.
        Transform firstGoal = network.GetCheckpoint(_goalIndex);
        if (firstGoal)
        {
            Vector3 dir = (firstGoal.position - rb.position).normalized;
            rb.linearVelocity = dir * _speed;
            rb.rotation = Quaternion.LookRotation(dir, Vector3.up);
            // Also reset angular velocity to prevent spinning
            rb.angularVelocity = Vector3.zero;
        }
    }

    void Update()
    {
        if (!_active) return;
        
        // Update UI
        if (statusText)
        {
            float remaining = Mathf.Max(0f, _endTime - Time.time);
            statusText.text = string.Format(uiFormat, remaining);
        }

        if (!_easing && cancelOnAnyInput && Time.time >= _inputUnlockTime)
        {
             if (DetectAnyInput())
             {
                 StartEaseOut();
                 return;
             }
        }

        if (!_easing && Time.time >= _endTime)
        {
            StartEaseOut();
            return;
        }

        if (_easing && easeOutSeconds <= 0f)
        {
            EndFinal();
            return;
        }

        if (_easing)
        {
            float t = (Time.time - _easeStartTime) / Mathf.Max(0.0001f, easeOutSeconds);
            if (t >= 1f) EndFinal();
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!_active) return;
        ApplyPhysicsOverride(Runner.DeltaTime);
    }

    void FixedUpdate()
    {
        // Fallback for when script is added dynamically (NetworkObject not initialized)
        if (!_active) return;
        if (Object == null)
        {
            ApplyPhysicsOverride(Time.fixedDeltaTime);
        }
    }

    void ApplyPhysicsOverride(float dt)
    {
        if (!rb || !network || network.Count == 0) return;

        // Easing logic
        float weight = 1f;
        if (_easing)
        {
            float t = (Time.time - _easeStartTime) / Mathf.Max(0.0001f, easeOutSeconds);
            t = Mathf.Clamp01(t);
            weight = easeOutCurve.Evaluate(t);
            if (!guideDuringEaseOut) weight = 0f;
        }

        if (weight <= 0.0001f) return;

        // Path logic
        Transform goal = network.GetCheckpoint(_goalIndex);
        if (!goal) 
        {
            // Self-healing: If current is null, try next
            int next = network.GetNextIndex(_goalIndex);
            if (next != _goalIndex)
            {
                _goalIndex = next;
            }
            return;
        }

        Vector3 toGoal = goal.position - rb.position;
        float dist = toGoal.magnitude;

        // PhysicsOverride logs removed

        if (dist <= arriveRadius)
        {
            _goalIndex = network.GetNextIndex(_goalIndex);
            goal = network.GetCheckpoint(_goalIndex);
            if (!goal) return;
            toGoal = goal.position - rb.position;
            dist = toGoal.magnitude;
        }

        if (dist < 0.0001f) return;

        Vector3 dir = toGoal / dist;
        _lastDir = dir;

        Vector3 desiredVel = dir * _speed;

        // Strong Override: Directly set velocity if far from target velocity, or blend fast
        float vLerp = 1f - Mathf.Exp(-velocityLerp * dt);
        // Force override other physics by setting velocity 'last'
        rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, desiredVel, vLerp * weight);

        if (alignRotationToPath)
        {
            // Kill external rotation forces (collisions, engine torque)
            rb.angularVelocity = Vector3.zero;

            Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
            // Slerp for smoothness, but apply DIRECTLY to rotation to bypass RB inertia
            float rLerp = 1f - Mathf.Exp(-rotationLerp * dt);
            rb.rotation = Quaternion.Slerp(rb.rotation, targetRot, rLerp * weight);
        }
    }


    void StartEaseOut()
    {
        if (_easing) return;

        _easing = true;
        _easeStartTime = Time.time;

        // Re-enable player controls immediately, but autopilot fades out smoothly.
        SetControlsEnabled(true);
    }

    void EndFinal()
    {
        _active = false;
        _easing = false;

        if (statusText) statusText.gameObject.SetActive(false);

        // Ensure controls are enabled
        SetControlsEnabled(true);

        // Restore kinematic mode as it was before autopilot
        rb.isKinematic = _savedWasKinematic;
    }

    void SetControlsEnabled(bool enabled)
    {
        if (disableWhileActive == null) return;
        for (int i = 0; i < disableWhileActive.Length; i++)
            if (disableWhileActive[i]) disableWhileActive[i].enabled = enabled;
    }

    bool DetectAnyInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;

        if (Mouse.current != null)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame ||
                Mouse.current.middleButton.wasPressedThisFrame)
                return true;

            if (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f) return true;
            if (Mouse.current.scroll.ReadValue().sqrMagnitude > 0.01f) return true;
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                Gamepad.current.buttonNorth.wasPressedThisFrame ||
                Gamepad.current.buttonWest.wasPressedThisFrame ||
                Gamepad.current.buttonEast.wasPressedThisFrame ||
                Gamepad.current.startButton.wasPressedThisFrame ||
                Gamepad.current.selectButton.wasPressedThisFrame)
                return true;

            if (Gamepad.current.leftStick.ReadValue().sqrMagnitude > axisThreshold * axisThreshold) return true;
            if (Gamepad.current.rightStick.ReadValue().sqrMagnitude > axisThreshold * axisThreshold) return true;
            if (Gamepad.current.leftTrigger.ReadValue() > axisThreshold) return true;
            if (Gamepad.current.rightTrigger.ReadValue() > axisThreshold) return true;
        }

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;

        return false;
#else
        if (Input.anyKeyDown) return true;

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            return true;

        if (legacyAxesToCheck != null)
        {
            for (int i = 0; i < legacyAxesToCheck.Length; i++)
            {
                string ax = legacyAxesToCheck[i];
                if (string.IsNullOrEmpty(ax)) continue;
                float v = Input.GetAxisRaw(ax);
                if (Mathf.Abs(v) > axisThreshold) return true;
            }
        }
        return false;
#endif
    }
}
