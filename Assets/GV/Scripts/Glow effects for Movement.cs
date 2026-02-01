using UnityEngine;
using System.Collections.Generic;
using VSX.Engines3D;
using VSX.ResourceSystem;

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
        float targetIntensity = 0f;
        Color targetBaseColor = _currentColor; 

        // INPUT LOGIC (Priority Order)
        
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
            // Fade out using last color
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