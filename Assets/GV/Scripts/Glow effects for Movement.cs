using UnityEngine;
using System.Collections.Generic;
using VSX.Engines3D;
using VSX.ResourceSystem;
using Fusion;

public class BoostGlow : MonoBehaviour
{
    [Header("Boost Settings (Tab)")]
    public float boostIntensity = 6.0f;
    public Color boostColor = Color.magenta;
    public KeyCode boostKey = KeyCode.W; // Updated default to W to match new controls

    [Header("Forward Settings (Auto / W)")]
    public bool autoForwardGlow = true; // New: Default to ON
    public float forwardIntensity = 2.0f;
    public Color forwardColor = Color.cyan;
    public KeyCode forwardKey = KeyCode.W; // Kept for reference, though W is now boost
    public KeyCode brakeKey = KeyCode.S;   // New: Brake key to turn off glow

    [Header("Side Settings (Q & E)")]
    public float sideIntensity = 3.0f;     // Intensity for Q and E
    public Color leftColor = Color.yellow; // Color for Q
    public KeyCode leftKey = KeyCode.Q;
    public Color rightColor = Color.green; // Color for E
    public KeyCode rightKey = KeyCode.E;

    [Header("General")]
    public float transitionSpeed = 5.0f;

    [Header("References")]
    public GameObject[] targetModelParents;
    public VehicleEngines3D vehicleEngines; // Made public to verify assignment

    private List<Renderer> _allRenderers = new List<Renderer>();
    private float _currentIntensity;
    private Color _currentColor;

    // Network awareness — only read keyboard for local player's ship
    private NetworkObject _networkObject;
    private bool _isLocalPlayer;

    public Color CurrentBaseColor => _currentColor;
    public float CurrentIntensity => _currentIntensity;

    void Start()
    {
        // Set default starting color
        _currentColor = forwardColor;

        if (vehicleEngines == null)
            vehicleEngines = GetComponent<VehicleEngines3D>();

        if (vehicleEngines == null)
            vehicleEngines = GetComponentInParent<VehicleEngines3D>();

        // Find NetworkObject to determine local vs remote ship
        _networkObject = transform.root.GetComponent<NetworkObject>();
        if (_networkObject == null)
            _networkObject = GetComponentInParent<NetworkObject>();

        // Find all renderers in all assigned parents
        foreach (GameObject parentObj in targetModelParents)
        {
            if (parentObj != null)
            {
                Renderer[] foundRenderers = parentObj.GetComponentsInChildren<Renderer>();
                _allRenderers.AddRange(foundRenderers);
            }
        }

        // Enable emission for everything
        foreach (Renderer r in _allRenderers)
        {
            r.material.EnableKeyword("_EMISSION");
        }
    }

    void Update()
    {
        // Determine if this is the local player's ship
        // If networked and not our ship, use engine state instead of keyboard input
        _isLocalPlayer = (_networkObject == null) || _networkObject.HasInputAuthority;

        float targetIntensity = 0f;
        Color targetBaseColor = _currentColor;

        if (_isLocalPlayer)
        {
            // LOCAL PLAYER: read keyboard input (responsive, no lag)

            bool canBoost = true;
            if (vehicleEngines != null)
            {
                foreach (var handler in vehicleEngines.BoostResourceHandlers)
                {
                    if (!handler.Ready())
                    {
                        canBoost = false;
                        break;
                    }
                }
            }

            // 1. Boost (Highest Priority) - NOW W KEY
            if (Input.GetKey(boostKey) && canBoost)
            {
                targetIntensity = boostIntensity;
                targetBaseColor = boostColor;
            }
            // 2. Brake (S Key) - Turn OFF glow
            else if (Input.GetKey(brakeKey))
            {
                targetIntensity = 0f;
            }
            // 3. Auto-Forward OR Manual Forward
            else if (autoForwardGlow || Input.GetKey(forwardKey))
            {
                targetIntensity = forwardIntensity;
                targetBaseColor = forwardColor;
            }
            // 4. Left (Q)
            else if (Input.GetKey(leftKey))
            {
                targetIntensity = sideIntensity;
                targetBaseColor = leftColor;
            }
            // 5. Right (E)
            else if (Input.GetKey(rightKey))
            {
                targetIntensity = sideIntensity;
                targetBaseColor = rightColor;
            }
            // 6. Idle (Only if auto-forward is OFF)
            else
            {
                targetIntensity = 0f;
            }
        }
        else
        {
            // REMOTE PLAYER: derive glow from engine state (network-synced)
            if (vehicleEngines != null)
            {
                Vector3 boostState = vehicleEngines.BoostInputs;
                Vector3 moveState = vehicleEngines.MovementInputs;

                // 1. Boost active
                if (boostState.magnitude > 0.1f)
                {
                    targetIntensity = boostIntensity;
                    targetBaseColor = boostColor;
                }
                // 2. Reverse / brake (negative Z movement)
                else if (moveState.z < -0.1f)
                {
                    targetIntensity = 0f;
                }
                // 3. Forward movement
                else if (moveState.z > 0.1f || autoForwardGlow)
                {
                    targetIntensity = forwardIntensity;
                    targetBaseColor = forwardColor;
                }
                // 4. Left strafe
                else if (moveState.x < -0.1f)
                {
                    targetIntensity = sideIntensity;
                    targetBaseColor = leftColor;
                }
                // 5. Right strafe
                else if (moveState.x > 0.1f)
                {
                    targetIntensity = sideIntensity;
                    targetBaseColor = rightColor;
                }
                // 6. Idle
                else
                {
                    targetIntensity = 0f;
                }
            }
        }

        // SMOOTHING
        _currentIntensity = Mathf.Lerp(_currentIntensity, targetIntensity, Time.deltaTime * transitionSpeed);
        _currentColor = Color.Lerp(_currentColor, targetBaseColor, Time.deltaTime * transitionSpeed);

        // APPLY
        Color finalColor = _currentColor * _currentIntensity;

        foreach (Renderer r in _allRenderers)
        {
            if (r != null)
            {
                r.material.SetColor("_EmissionColor", finalColor);
            }
        }
    }
}