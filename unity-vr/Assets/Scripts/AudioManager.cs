using UnityEngine;

public class AudioManager : MonoBehaviour
{
    // ── Inspector references ──────────────────────────
    [Header("Audio Sources")]
    public AudioSource backgroundAmbience;
    public AudioSource npcVoice;
    public AudioSource uiSounds;
    public AudioSource rewardSounds;

    [Header("Audio Clips")]
    public AudioClip celebrationClip;
    public AudioClip correctResponseClip;
    public AudioClip encouragementClip;

    // ── Volume presets ────────────────────────────────
    [Header("Volume Presets")]
    [Range(0, 1)] public float normalAmbienceVolume = 0.4f;
    [Range(0, 1)] public float reducedAmbienceVolume = 0.1f;
    [Range(0, 1)] public float mutedAmbienceVolume = 0.0f;

    // ── Singleton ─────────────────────────────────────
    public static AudioManager Instance;

    // ─────────────────────────────────────────────────
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (backgroundAmbience != null)
        {
            backgroundAmbience.loop = true;
            backgroundAmbience.volume = normalAmbienceVolume;
            backgroundAmbience.Play();
        }
    }

    // ── Volume controls called by AdaptiveEnvironment ─
    public static void ReduceBackgroundNoise()
    {
        if (Instance?.backgroundAmbience != null)
        {
            Instance.backgroundAmbience.volume =
                Instance.reducedAmbienceVolume;
            Debug.Log("🔇 Background noise reduced");
        }
    }

    public static void MuteBackgroundNoise()
    {
        if (Instance?.backgroundAmbience != null)
        {
            Instance.backgroundAmbience.volume =
                Instance.mutedAmbienceVolume;
            Debug.Log("🔕 Background noise muted");
        }
    }

    public static void RestoreBackgroundNoise()
    {
        if (Instance?.backgroundAmbience != null)
        {
            Instance.backgroundAmbience.volume =
                Instance.normalAmbienceVolume;
            Debug.Log("🔊 Background noise restored");
        }
    }

    // ── Positive reinforcement sounds ─────────────────
    public static void PlayCelebration()
    {
        if (Instance?.rewardSounds != null &&
            Instance?.celebrationClip != null)
        {
            Instance.rewardSounds.PlayOneShot(Instance.celebrationClip);
        }
    }

    public static void PlayCorrectResponse()
    {
        if (Instance?.rewardSounds != null &&
            Instance?.correctResponseClip != null)
        {
            Instance.rewardSounds.PlayOneShot(Instance.correctResponseClip);
        }
    }

    public static void PlayEncouragement()
    {
        if (Instance?.rewardSounds != null &&
            Instance?.encouragementClip != null)
        {
            Instance.rewardSounds.PlayOneShot(Instance.encouragementClip);
        }
    }
}