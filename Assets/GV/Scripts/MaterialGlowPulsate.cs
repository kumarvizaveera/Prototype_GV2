using UnityEngine;

public class MaterialGlowPulsate : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The color of the glow. If null/white, uses the material's current emission color.")]
    public Color glowColor = Color.cyan;
    
    [Tooltip("Minimum intensity of the glow.")]
    public float minIntensity = 0.5f;

    [Tooltip("Maximum intensity of the glow.")]
    public float maxIntensity = 2.0f;

    [Tooltip("How fast the glow pulsates.")]
    public float pulseSpeed = 1.0f;

    [Header("Target")]
    [Tooltip("The renderer to apply the glow to. If empty, uses GetComponent<Renderer>().")]
    public Renderer targetRenderer;

    [Tooltip("The index of the material to target (usually 0).")]
    public int materialIndex = 0;

    [Tooltip("The name of the emission color property in the shader (e.g. _EmissionColor or _EmissiveColor).")]
    public string emissionPropertyName = "_EmissionColor";

    private MaterialPropertyBlock _propBlock;
    private int _emissionColorId;

    private void Awake()
    {
        Initialize();
    }

    private void OnEnable()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (targetRenderer != null)
        {
            // Ensure the material has Emission enabled
            // We use sharedMaterial to avoid leaking materials in Editor
            Material mat = Application.isPlaying ? targetRenderer.material : targetRenderer.sharedMaterial;
            if (mat != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        if (_propBlock == null)
        {
            _propBlock = new MaterialPropertyBlock();
        }
        
        _emissionColorId = Shader.PropertyToID(emissionPropertyName);
    }

    private void Update()
    {
        if (targetRenderer == null) return;

        // Calculate current intensity using a sine wave
        // (Sin goes from -1 to 1, map to 0 to 1)
        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
        float currentIntensity = Mathf.Lerp(minIntensity, maxIntensity, t);

        // Calculate the final color
        // Using the base color * intensity
        // Note: For HDR/bloom, values > 1 work well.
        Color finalColor = glowColor * currentIntensity;

        // Apply to the property block
        targetRenderer.GetPropertyBlock(_propBlock, materialIndex);
        _propBlock.SetColor(_emissionColorId, finalColor);
        targetRenderer.SetPropertyBlock(_propBlock, materialIndex);
    }
}
