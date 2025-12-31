using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LaserReactiveGlow : MonoBehaviour
{
    [Header("Settings")]
    public float laserBrightness = 5.0f; // How bright the laser glows

    private LineRenderer _lineRenderer;
    private BoostGlow _playerShipGlow;

    void Start()
    {
        _lineRenderer = GetComponent<LineRenderer>();
        
        // Ensure the material can glow
        _lineRenderer.material.EnableKeyword("_EMISSION");

        // Automatically find the player's BoostGlow script in the scene
        _playerShipGlow = FindObjectOfType<BoostGlow>();
    }

    void Update()
    {
        if (_playerShipGlow == null) return; // Safety check

        // 1. Determine which color the ship is currently using
        // We read the public variables directly from your OTHER script!
        Color targetColor = _playerShipGlow.forwardColor; // Default

        if (Input.GetKey(_playerShipGlow.boostKey))
        {
            targetColor = _playerShipGlow.boostColor;
        }
        else if (Input.GetKey(_playerShipGlow.leftKey))
        {
            targetColor = _playerShipGlow.leftColor;
        }
        else if (Input.GetKey(_playerShipGlow.rightKey))
        {
            targetColor = _playerShipGlow.rightColor;
        }

        // 2. Apply the Glow Intensity (Make it bright!)
        // We use our own brightness so the laser stays visible even if the ship engine is off
        Color finalGlow = targetColor * laserBrightness;

        // 3. Apply to the Line Renderer Material
        _lineRenderer.material.SetColor("_EmissionColor", finalGlow);
        
        // 4. Also tint the line itself for better visibility
        _lineRenderer.startColor = finalGlow;
        _lineRenderer.endColor = finalGlow;
    }
}