using System.Collections;
using UnityEngine;

/// <summary>
/// Audio-only swap router for 2 aircraft audio rigs (A/B).
/// Put ALL audio GameObjects for aircraft A under audioRootA,
/// and ALL audio GameObjects for aircraft B under audioRootB.
///
/// Works best when attached to an ALWAYS-ACTIVE parent (same object as AircraftMeshSwapWithFX).
/// It will follow the mesh swap automatically and enable/disable (or mute) the correct audio rig.
/// </summary>
[DisallowMultipleComponent]
public class AircraftSwapAudioOnly : MonoBehaviour
{
    [Header("Swap Driver (optional but recommended)")]
    [Tooltip("Assign the AircraftMeshSwapWithFX that swaps visuals. If empty, the script will auto-find it on this GameObject.")]
    public AircraftMeshSwapWithFX swapDriver;

    [Tooltip("If true, detects which aircraft is active by checking meshRootA/meshRootB activeInHierarchy (best sync).")]
    public bool syncFromMeshRootActiveState = true;

    [Tooltip("Optional delay before switching audio rigs (seconds). Usually keep 0.")]
    [Min(0f)] public float audioSwitchDelay = 0f;

    [Header("Audio Roots (create these)")]
    [Tooltip("Parent GameObject that contains ONLY Aircraft A audio objects (AudioSources, audio scripts, etc).")]
    public GameObject audioRootA;

    [Tooltip("Parent GameObject that contains ONLY Aircraft B audio objects (AudioSources, audio scripts, etc).")]
    public GameObject audioRootB;

    [Header("Start State (used if driver not found)")]
    public bool startWithA = true;

    [Header("How to deactivate the non-active rig")]
    [Tooltip("If true, SetActive(false) the non-active audio root. If false, keep both active and mute the inactive root instead.")]
    public bool disableInactiveRootGameObject = true;

    [Tooltip("If true (and disableInactiveRootGameObject is false), mutes all AudioSources under the inactive root.")]
    public bool muteInactiveRoot = true;

    [Header("Safety / Cleanup")]
    [Tooltip("Stops all AudioSources under the rig that is being turned OFF (prevents lingering one-shots).")]
    public bool stopLeavingRigOnSwap = true;

    [Tooltip("If true, forces playOnAwake=false for ALL AudioSources under BOTH rigs (prevents unwanted auto-play).")]
    public bool forcePlayOnAwakeOffForAllRigSources = false;

    [Header("Swap One-Shot SFX (optional)")]
    [Tooltip("Dedicated AudioSource that stays always active. If left empty, one will be auto-created on THIS GameObject.")]
    public AudioSource oneShotSource;

    [Tooltip("Sound to play when switching TO aircraft A (optional).")]
    public AudioClip swapToAClip;

    [Tooltip("Sound to play when switching TO aircraft B (optional).")]
    public AudioClip swapToBClip;

    [Range(0f, 1f)] public float swapVolume = 1f;

    private bool _isA;
    private bool _initialized;
    private Coroutine _pendingSwitch;

    void Awake()
    {
        if (swapDriver == null) swapDriver = GetComponent<AircraftMeshSwapWithFX>();

        if (audioRootA == null || audioRootB == null)
        {
            Debug.LogError($"{nameof(AircraftSwapAudioOnly)} on '{name}': Assign audioRootA and audioRootB.");
            enabled = false;
            return;
        }

        EnsureOneShotSource();

        if (forcePlayOnAwakeOffForAllRigSources)
        {
            ForcePlayOnAwakeOff(audioRootA);
            ForcePlayOnAwakeOff(audioRootB);
        }

        // Initial state
        _isA = GetDesiredAInitial();
        ApplyStateImmediate(_isA, playSwapSfx: false);
        _initialized = true;
    }

    void Update()
    {
        if (!_initialized) return;
        if (swapDriver == null) return;

        bool desiredA = GetDesiredAFromDriver();
        if (desiredA == _isA) return;

        // Debounce + optional delay
        if (_pendingSwitch != null) StopCoroutine(_pendingSwitch);
        _pendingSwitch = StartCoroutine(SwitchAfterDelay(desiredA));
    }

    /// <summary>
    /// If you prefer manual control (no driver), call this from your own swap logic.
    /// </summary>
    public void SetAActive(bool toA, bool playSwapSfx = true)
    {
        if (!_initialized)
        {
            _isA = toA;
            ApplyStateImmediate(_isA, playSwapSfx: false);
            _initialized = true;
            return;
        }

        if (toA == _isA) return;

        if (_pendingSwitch != null) StopCoroutine(_pendingSwitch);
        _pendingSwitch = StartCoroutine(SwitchAfterDelay(toA, playSwapSfx));
    }

    private IEnumerator SwitchAfterDelay(bool toA, bool playSwapSfx = true)
    {
        if (audioSwitchDelay > 0f)
            yield return new WaitForSeconds(audioSwitchDelay);

        ApplyStateImmediate(toA, playSwapSfx);
        _pendingSwitch = null;
    }

    private void ApplyStateImmediate(bool toA, bool playSwapSfx)
    {
        GameObject leaving = toA ? audioRootB : audioRootA;
        GameObject entering = toA ? audioRootA : audioRootB;

        if (stopLeavingRigOnSwap)
            StopAllAudioSources(leaving);

        if (disableInactiveRootGameObject)
        {
            if (leaving != null) leaving.SetActive(false);
            if (entering != null) entering.SetActive(true);
        }
        else
        {
            if (audioRootA != null) audioRootA.SetActive(true);
            if (audioRootB != null) audioRootB.SetActive(true);

            if (muteInactiveRoot)
            {
                SetMuteAll(audioRootA, mute: !toA);
                SetMuteAll(audioRootB, mute: toA);
            }
        }

        _isA = toA;

        if (playSwapSfx)
            PlaySwapOneShot(toA);
    }

    private bool GetDesiredAInitial()
    {
        // If driver exists, mirror its configured start state.
        if (swapDriver != null)
            return swapDriver.startWithA;

        return startWithA;
    }

    private bool GetDesiredAFromDriver()
    {
        if (swapDriver == null) return _isA;

        if (syncFromMeshRootActiveState && swapDriver.meshRootA != null && swapDriver.meshRootB != null)
        {
            // Best sync: follow which mesh root is currently active in hierarchy.
            bool aActive = swapDriver.meshRootA.activeInHierarchy;
            bool bActive = swapDriver.meshRootB.activeInHierarchy;

            if (aActive && !bActive) return true;
            if (!aActive && bActive) return false;

            // Fallback if both true/false for a moment
            return swapDriver.IsAActive;
        }

        return swapDriver.IsAActive;
    }

    private void EnsureOneShotSource()
    {
        // Ensure a dedicated, always-active AudioSource for swap one-shots.
        // Avoid using one inside audioRootA/B because those may be disabled/muted.
        if (oneShotSource != null)
        {
            if (oneShotSource.transform.IsChildOf(audioRootA.transform) || oneShotSource.transform.IsChildOf(audioRootB.transform))
                oneShotSource = null;
        }

        if (oneShotSource == null)
        {
            oneShotSource = GetComponent<AudioSource>();
            if (oneShotSource == null) oneShotSource = gameObject.AddComponent<AudioSource>();
        }

        oneShotSource.playOnAwake = false;
        oneShotSource.loop = false;
        oneShotSource.clip = null;
        oneShotSource.Stop();
    }

    private void PlaySwapOneShot(bool switchedToA)
    {
        if (oneShotSource == null) return;

        AudioClip clip = switchedToA ? swapToAClip : swapToBClip;
        if (clip == null) return;

        oneShotSource.PlayOneShot(clip, swapVolume);
    }

    private void StopAllAudioSources(GameObject root)
    {
        if (root == null) return;
        var sources = root.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            sources[i].Stop();
        }
    }

    private void SetMuteAll(GameObject root, bool mute)
    {
        if (root == null) return;
        var sources = root.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            sources[i].mute = mute;
        }
    }

    private void ForcePlayOnAwakeOff(GameObject root)
    {
        if (root == null) return;
        var sources = root.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            sources[i].playOnAwake = false;
        }
    }

    public bool IsAActive => _isA;
}
