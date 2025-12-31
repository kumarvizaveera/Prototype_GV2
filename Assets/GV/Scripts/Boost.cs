using UnityEngine;
using System.Collections.Generic;

public class BoostGlow : MonoBehaviour
{
    [Header("Boost Settings (Tab)")]
    public float boostIntensity = 6.0f;
    public Color boostColor = Color.magenta;
    public KeyCode boostKey = KeyCode.Tab;

    [Header("Forward Settings (W)")]
    public float forwardIntensity = 2.0f;
    public Color forwardColor = Color.cyan;
    public KeyCode forwardKey = KeyCode.W;

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

    private List<Renderer> _allRenderers = new List<Renderer>();
    private float _currentIntensity;
    private Color _currentColor;

    void Start()
    {
        // Set default starting color
        _currentColor = forwardColor;

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
        // 1. Boost (Highest Priority)
        if (Input.GetKey(boostKey))
        {
            targetIntensity = boostIntensity;
            targetBaseColor = boostColor;
        }
        // 2. Forward
        else if (Input.GetKey(forwardKey))
        {
            targetIntensity = forwardIntensity;
            targetBaseColor = forwardColor;
        }
        // 3. Left (Q)
        else if (Input.GetKey(leftKey))
        {
            targetIntensity = sideIntensity;
            targetBaseColor = leftColor;
        }
        // 4. Right (E)
        else if (Input.GetKey(rightKey))
        {
            targetIntensity = sideIntensity;
            targetBaseColor = rightColor;
        }
        // 5. Idle (No keys pressed)
        else
        {
            targetIntensity = 0f;
            // Keep the last known color so it fades out naturally
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