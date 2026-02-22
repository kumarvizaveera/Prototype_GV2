using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh-only swap with optional VFX + SFX + flash + fade.
/// Fixes:
/// 1) Swap sound will NOT play on Play (forces AudioSource playOnAwake=false and stops it).
/// 2) Swap sound plays for BOTH swaps (uses a dedicated AudioSource on the ALWAYS-ACTIVE aircraft root,
///    so it won't get disabled when you turn mesh roots on/off).
/// </summary>
[DisallowMultipleComponent]
public class AircraftMeshSwapWithFX : MonoBehaviour
{
    [Header("Visual Roots (meshes/VFX only)")]
    public GameObject meshRootA;
    public GameObject meshRootB;

    [Header("Start State")]
    public bool startWithA = true;

    [Header("Direct Select Keys")]
    public bool useAlpha1Alpha2 = true;
    public KeyCode keyForA = KeyCode.Alpha1;
    public KeyCode keyForB = KeyCode.Alpha2;

    [Header("Optional Toggle Key")]
    public bool allowToggleKey = false;
    public KeyCode toggleKey = KeyCode.Tab;

    [Header("Swap Timing")]
    [Min(0f)] public float swapDelay = 0.05f;
    public bool lockInputWhileSwapping = true;

    [Header("Swap VFX (Optional)")]
    public GameObject swapVfxPrefab;
    public Transform vfxSpawnPoint;
    public Vector3 vfxLocalOffset = Vector3.zero;
    public bool parentVfxToAircraft = true;
    [Min(0f)] public float vfxAutoDestroySeconds = 3f;

    [Header("Swap SFX (Optional)")]
    [Tooltip("Leave empty to auto-create/use an AudioSource on THIS aircraft root (recommended).")]
    public AudioSource audioSource;

    public AudioClip swapClip;
    [Range(0f, 1f)] public float swapVolume = 1f;

    [Tooltip("If true, disables Play On Awake and stops ALL AudioSources under meshRootA/B on start (prevents auto-play).")]
    public bool silenceMeshRootAudioOnStart = true;

    [Header("Flash (Optional)")]
    public Light flashLight;
    [Min(0f)] public float flashPeakIntensity = 8f;
    [Min(0f)] public float flashDuration = 0.08f;

    [Header("Fade Animation (Optional)")]
    public bool enableFade = false;
    [Min(0.01f)] public float fadeOutTime = 0.12f;
    [Min(0.01f)] public float fadeInTime = 0.12f;

    [Tooltip("Common color property names: URP Lit = _BaseColor, Standard = _Color")]
    public string[] colorPropertyCandidates = new string[] { "_BaseColor", "_Color" };

    public bool fadeEmissionToo = false;
    public string emissionColorProperty = "_EmissionColor";

    [Header("Visual Safety")]
    public bool forceDisableCollidersOnVisualRoots = true;

    [Header("Network Ownership")]
    [Tooltip("Set to false on remote (non-local) ships so only the owning player can trigger swaps.")]
    public bool isLocalPlayer = true;

    [SerializeField] private bool isA;
    private bool isSwapping;

    private Renderer[] aRenderers;
    private Renderer[] bRenderers;

    private struct RendererColorCache
    {
        public Renderer r;
        public string colorProp;
        public Color baseColor;
        public bool hasEmission;
        public Color emissionColor;
    }

    private List<RendererColorCache> aCache = new List<RendererColorCache>(64);
    private List<RendererColorCache> bCache = new List<RendererColorCache>(64);
    private MaterialPropertyBlock mpb;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();

        if (meshRootA == null || meshRootB == null)
        {
            Debug.LogError($"{nameof(AircraftMeshSwapWithFX)} on '{name}': Assign meshRootA and meshRootB.");
            enabled = false;
            return;
        }

        // Prevent any audio on mesh roots from auto-playing on start (common cause of "sound plays immediately")
        if (silenceMeshRootAudioOnStart)
        {
            SilenceAudioSourcesUnder(meshRootA);
            SilenceAudioSourcesUnder(meshRootB);
        }

        // Ensure swap SFX uses an ALWAYS-ACTIVE AudioSource (this GameObject),
        // so swapping visuals won't cut the sound.
        EnsureDedicatedSwapAudioSource();

        aRenderers = meshRootA.GetComponentsInChildren<Renderer>(true);
        bRenderers = meshRootB.GetComponentsInChildren<Renderer>(true);

        CacheRendererColors(aRenderers, aCache);
        CacheRendererColors(bRenderers, bCache);

        if (forceDisableCollidersOnVisualRoots)
        {
            DisableAllColliders(meshRootA);
            DisableAllColliders(meshRootB);
        }

        isA = startWithA;

        meshRootA.SetActive(isA);
        meshRootB.SetActive(!isA);

        if (enableFade)
        {
            SetRootAlpha(aCache, isA ? 1f : 0f);
            SetRootAlpha(bCache, !isA ? 1f : 0f);
        }

        if (flashLight != null) flashLight.intensity = 0f;
    }

    void Update()
    {
        // Only the local player's ship should respond to keyboard input
        if (!isLocalPlayer) return;

        if (lockInputWhileSwapping && isSwapping) return;

        // Checkpoint Restriction
        if (CheckpointNetwork.Instance != null && !CheckpointNetwork.Instance.CanSwapAircraft) return;

        if (useAlpha1Alpha2)
        {
            if (Input.GetKeyDown(keyForA)) RequestSetA(true);
            if (Input.GetKeyDown(keyForB)) RequestSetA(false);
        }

        if (allowToggleKey && Input.GetKeyDown(toggleKey))
        {
            RequestSetA(!isA);
        }
    }

    public void RequestSetA(bool wantA)
    {
        if (wantA == isA) return;
        if (lockInputWhileSwapping && isSwapping) return;

        StopAllCoroutines();
        StartCoroutine(SwapRoutine(wantA));
    }

    private IEnumerator SwapRoutine(bool toA)
    {
        isSwapping = true;

        // Play effects ONLY on swap input (never on Play)
        SpawnVFX();
        PlaySwapSFX();
        if (flashLight != null) StartCoroutine(FlashRoutine());

        GameObject fromRoot = isA ? meshRootA : meshRootB;
        GameObject toRoot = toA ? meshRootA : meshRootB;
        List<RendererColorCache> fromCache = isA ? aCache : bCache;
        List<RendererColorCache> toCache = toA ? aCache : bCache;

        if (enableFade)
            yield return FadeAlpha(fromCache, 1f, 0f, fadeOutTime);

        fromRoot.SetActive(false);

        if (swapDelay > 0f) yield return new WaitForSeconds(swapDelay);

        toRoot.SetActive(true);

        if (enableFade)
        {
            SetRootAlpha(toCache, 0f);
            yield return FadeAlpha(toCache, 0f, 1f, fadeInTime);
        }

        isA = toA;
        isSwapping = false;
    }

    private void EnsureDedicatedSwapAudioSource()
    {
        // If user assigned an AudioSource that lives under a mesh root, it will be disabled during swap.
        // In that case, ignore it and create/use one on THIS root.
        if (audioSource != null)
        {
            bool underA = audioSource.transform.IsChildOf(meshRootA.transform);
            bool underB = audioSource.transform.IsChildOf(meshRootB.transform);
            if (underA || underB)
            {
                Debug.LogWarning($"{nameof(AircraftMeshSwapWithFX)}: Assigned AudioSource is under a mesh root and may get disabled. " +
                                 "Using a dedicated AudioSource on the aircraft root instead.");
                audioSource = null;
            }
        }

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Critical: stop auto-play at runtime even if Inspector had Play On Awake enabled.
        audioSource.playOnAwake = false;
        audioSource.loop = false;

        // Also prevent "clip auto plays" setups. We only use PlayOneShot.
        audioSource.clip = null;
        audioSource.Stop();
    }

    private void SilenceAudioSourcesUnder(GameObject root)
    {
        if (root == null) return;
        var sources = root.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            sources[i].playOnAwake = false;
            sources[i].loop = false;
            sources[i].Stop();
        }
    }

    private void PlaySwapSFX()
    {
        if (swapClip == null) return;
        if (audioSource == null) return;

        // Use PlayOneShot so it doesn't depend on AudioSource.clip or state
        audioSource.PlayOneShot(swapClip, swapVolume);
    }

    private void SpawnVFX()
    {
        if (swapVfxPrefab == null) return;

        Transform t = (vfxSpawnPoint != null) ? vfxSpawnPoint : transform;
        Vector3 pos = t.TransformPoint(vfxLocalOffset);
        Quaternion rot = t.rotation;

        GameObject vfx = Instantiate(swapVfxPrefab, pos, rot);

        if (parentVfxToAircraft)
            vfx.transform.SetParent(t, worldPositionStays: true);

        if (vfxAutoDestroySeconds > 0f)
            Destroy(vfx, vfxAutoDestroySeconds);
    }

    private IEnumerator FlashRoutine()
    {
        float original = flashLight.intensity;
        flashLight.intensity = 0f;

        float t = 0f;
        while (t < flashDuration)
        {
            t += Time.deltaTime;
            float k = (flashDuration <= 0f) ? 1f : (t / flashDuration);
            float tri = (k <= 0.5f) ? (k * 2f) : (2f - k * 2f);
            flashLight.intensity = Mathf.Lerp(0f, flashPeakIntensity, tri);
            yield return null;
        }

        flashLight.intensity = original;
    }

    private void CacheRendererColors(Renderer[] renderers, List<RendererColorCache> outCache)
    {
        outCache.Clear();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) continue;

            string colorProp = null;
            Color baseCol = Color.white;

            // Find a valid color property on any material
            for (int m = 0; m < mats.Length && colorProp == null; m++)
            {
                Material mat = mats[m];
                if (mat == null) continue;

                for (int p = 0; p < colorPropertyCandidates.Length; p++)
                {
                    string cand = colorPropertyCandidates[p];
                    if (mat.HasProperty(cand))
                    {
                        colorProp = cand;
                        baseCol = mat.GetColor(cand);
                        break;
                    }
                }
            }

            if (colorProp == null) continue;

            bool hasEm = false;
            Color emCol = Color.black;

            if (fadeEmissionToo)
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null) continue;
                    if (mat.HasProperty(emissionColorProperty))
                    {
                        hasEm = true;
                        emCol = mat.GetColor(emissionColorProperty);
                        break;
                    }
                }
            }

            outCache.Add(new RendererColorCache
            {
                r = r,
                colorProp = colorProp,
                baseColor = baseCol,
                hasEmission = hasEm,
                emissionColor = emCol
            });
        }
    }

    private IEnumerator FadeAlpha(List<RendererColorCache> cache, float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetRootAlpha(cache, to);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float a = Mathf.Lerp(from, to, k);
            SetRootAlpha(cache, a);
            yield return null;
        }

        SetRootAlpha(cache, to);
    }

    private void SetRootAlpha(List<RendererColorCache> cache, float alpha01)
    {
        alpha01 = Mathf.Clamp01(alpha01);

        for (int i = 0; i < cache.Count; i++)
        {
            var c = cache[i];
            if (c.r == null) continue;

            c.r.GetPropertyBlock(mpb);

            Color col = c.baseColor;
            col.a = alpha01;
            mpb.SetColor(c.colorProp, col);

            if (fadeEmissionToo && c.hasEmission)
            {
                Color em = c.emissionColor * alpha01;
                mpb.SetColor(emissionColorProperty, em);
            }

            c.r.SetPropertyBlock(mpb);
        }
    }

    private void DisableAllColliders(GameObject root)
    {
        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;
    }

    public bool IsAActive => isA;
}
