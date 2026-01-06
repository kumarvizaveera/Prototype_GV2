using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh-only aircraft swap with optional swap VFX + SFX + flash + fade animation.
/// Does NOT touch movement/physics scripts (keep those on the aircraft root).
///
/// Setup:
/// - Put this on the same GameObject that moves (your aircraft root with Rigidbody + movement scripts).
/// - Create two children that contain ONLY visuals:
///     MeshRootA (vimana mesh, vfx, etc)
///     MeshRootB (spaceship mesh, vfx, etc)
/// - Assign them to meshRootA / meshRootB.
///
/// Notes about fading:
/// - Fading requires your materials to support transparency (URP Lit: Surface Type = Transparent).
/// - If your materials are opaque, keep enableFade = false and use VFX/flash instead.
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
    [Tooltip("Delay before enabling the new mesh (lets VFX hit).")]
    [Min(0f)] public float swapDelay = 0.05f;

    [Tooltip("Ignore new input while swapping.")]
    public bool lockInputWhileSwapping = true;

    [Header("Swap VFX (Optional)")]
    [Tooltip("Particle prefab to spawn at swap time (ParticleSystem or any prefab).")]
    public GameObject swapVfxPrefab;

    [Tooltip("Where to spawn the VFX. If null, uses this transform.")]
    public Transform vfxSpawnPoint;

    public Vector3 vfxLocalOffset = Vector3.zero;

    [Tooltip("If true, VFX will follow the aircraft (parented).")]
    public bool parentVfxToAircraft = true;

    [Min(0f)] public float vfxAutoDestroySeconds = 3f;

    [Header("Swap SFX (Optional)")]
    public AudioSource audioSource;
    public AudioClip swapClip;
    [Range(0f, 1f)] public float swapVolume = 1f;

    [Header("Flash (Optional)")]
    [Tooltip("Assign a Light to do a quick flash on swap (optional).")]
    public Light flashLight;
    [Min(0f)] public float flashPeakIntensity = 8f;
    [Min(0f)] public float flashDuration = 0.08f;

    [Header("Fade Animation (Optional)")]
    public bool enableFade = false;

    [Min(0.01f)] public float fadeOutTime = 0.12f;
    [Min(0.01f)] public float fadeInTime = 0.12f;

    [Tooltip("Common color property names: URP Lit = _BaseColor, Standard = _Color")]
    public string[] colorPropertyCandidates = new string[] { "_BaseColor", "_Color" };

    [Tooltip("If you also want emission to fade with alpha (only if shader uses _EmissionColor).")]
    public bool fadeEmissionToo = false;
    public string emissionColorProperty = "_EmissionColor";

    [Header("Visual Safety")]
    [Tooltip("Recommended: keep colliders on visual roots disabled so visuals don't affect physics.")]
    public bool forceDisableCollidersOnVisualRoots = true;

    [SerializeField] private bool isA;
    private bool isSwapping;

    // Cached renderers per root for fading
    private Renderer[] aRenderers;
    private Renderer[] bRenderers;

    // Cache original colors per renderer (per property name)
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

        aRenderers = meshRootA.GetComponentsInChildren<Renderer>(true);
        bRenderers = meshRootB.GetComponentsInChildren<Renderer>(true);

        CacheRendererColors(meshRootA, aRenderers, aCache);
        CacheRendererColors(meshRootB, bRenderers, bCache);

        if (forceDisableCollidersOnVisualRoots)
        {
            DisableAllColliders(meshRootA);
            DisableAllColliders(meshRootB);
        }

        isA = startWithA;

        // Ensure correct initial active state
        meshRootA.SetActive(isA);
        meshRootB.SetActive(!isA);

        // Ensure initial alpha = 1 for active, 0 for inactive (if fade enabled)
        if (enableFade)
        {
            SetRootAlpha(meshRootA, aCache, isA ? 1f : 0f);
            SetRootAlpha(meshRootB, bCache, !isA ? 1f : 0f);
        }

        if (flashLight != null) flashLight.intensity = 0f;
    }

    void Update()
    {
        if (lockInputWhileSwapping && isSwapping) return;

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

        // Spawn effects immediately
        SpawnVFX();
        PlaySFX();
        if (flashLight != null) StartCoroutine(FlashRoutine());

        GameObject fromRoot = isA ? meshRootA : meshRootB;
        GameObject toRoot = toA ? meshRootA : meshRootB;
        List<RendererColorCache> fromCache = isA ? aCache : bCache;
        List<RendererColorCache> toCache = toA ? aCache : bCache;

        if (enableFade)
        {
            // Fade out current
            yield return FadeAlpha(fromRoot, fromCache, 1f, 0f, fadeOutTime);
        }

        // Hide current mesh root
        fromRoot.SetActive(false);

        // Small delay so VFX reads well
        if (swapDelay > 0f) yield return new WaitForSeconds(swapDelay);

        // Show new mesh root
        toRoot.SetActive(true);

        if (enableFade)
        {
            // Ensure starts hidden then fade in
            SetRootAlpha(toRoot, toCache, 0f);
            yield return FadeAlpha(toRoot, toCache, 0f, 1f, fadeInTime);
        }

        isA = toA;
        isSwapping = false;
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

    private void PlaySFX()
    {
        if (audioSource == null || swapClip == null) return;
        audioSource.PlayOneShot(swapClip, swapVolume);
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
            // quick up then down triangle
            float tri = (k <= 0.5f) ? (k * 2f) : (2f - k * 2f);
            flashLight.intensity = Mathf.Lerp(0f, flashPeakIntensity, tri);
            yield return null;
        }

        flashLight.intensity = original;
    }

    private IEnumerator FadeAlpha(GameObject root, List<RendererColorCache> cache, float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetRootAlpha(root, cache, to);
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            float a = Mathf.Lerp(from, to, k);
            SetRootAlpha(root, cache, a);
            yield return null;
        }

        SetRootAlpha(root, cache, to);
    }

    private void CacheRendererColors(GameObject root, Renderer[] renderers, List<RendererColorCache> outCache)
    {
        outCache.Clear();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;

            // Pick a usable color property from the first material that has it
            string colorProp = null;
            Color baseCol = Color.white;

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) continue;

            for (int m = 0; m < mats.Length; m++)
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
                if (colorProp != null) break;
            }

            if (colorProp == null)
                continue; // can't fade this renderer (shader doesn't expose a known color prop)

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

    private void SetRootAlpha(GameObject root, List<RendererColorCache> cache, float alpha01)
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
                Color em = c.emissionColor;
                em *= alpha01;
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
