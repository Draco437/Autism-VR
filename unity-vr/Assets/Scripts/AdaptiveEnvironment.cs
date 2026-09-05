using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class AdaptiveEnvironment : MonoBehaviour
{
    // ── Inspector references ──────────────────────────
    [Header("Environment References")]
    public AudioSource backgroundAudioSource;
    public GameObject visualPromptPrefab;
    public GameObject npcGameObject;
    public Light[] sceneLights;

    // ── Private fields ────────────────────────────────
    float originalBgVolume = 1.0f;
    float originalLightIntensity = 1.0f;

    // ─────────────────────────────────────────────────
    void OnEnable()
    {
        // Subscribe to signals from VRSessionManager
        VRSessionManager.OnAdaptiveSignal += HandleAdaptiveSignal;
        VRSessionManager.OnTherapistOverride += HandleTherapistOverride;
    }

    void OnDisable()
    {
        VRSessionManager.OnAdaptiveSignal -= HandleAdaptiveSignal;
        VRSessionManager.OnTherapistOverride -= HandleTherapistOverride;
    }

    void Start()
    {
        if (backgroundAudioSource != null)
            originalBgVolume = backgroundAudioSource.volume;

        if (sceneLights != null && sceneLights.Length > 0)
            originalLightIntensity = sceneLights[0].intensity;
    }

    // ── Handle RL adaptive signal from Django ─────────
    void HandleAdaptiveSignal(string jsonPayload)
    {
        try
        {
            JObject signal = JObject.Parse(jsonPayload);
            string action = signal["action"]?.ToString();

            Debug.Log($"🎮 Applying adaptation: {action}");

            switch (action)
            {
                case "reduce_complexity":
                    ReduceComplexity(signal);
                    break;

                case "increase_complexity":
                    IncreaseComplexity(signal);
                    break;

                case "add_attention_prompt":
                    AddAttentionPrompt(signal);
                    break;

                case "load_next_scenario":
                    LoadNextScenario(signal);
                    break;

                case "end_scenario":
                    EndScenario();
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Adaptive signal parse error: {e.Message}");
        }
    }

    // ── Handle therapist manual override ─────────────
    void HandleTherapistOverride(string jsonPayload)
    {
        try
        {
            JObject signal = JObject.Parse(jsonPayload);
            string action = signal["action"]?.ToString();

            Debug.Log($"👨‍⚕️ Therapist override: {action}");

            switch (action)
            {
                case "pause_scenario":
                    PauseScenario();
                    break;
                case "end_scenario":
                    EndScenario();
                    break;
                case "reduce_complexity":
                    ReduceComplexity(signal);
                    break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Override parse error: {e.Message}");
        }
    }

    // ── Adaptation actions ────────────────────────────
    void ReduceComplexity(JObject signal)
    {
        // Reduce background noise
        if (backgroundAudioSource != null)
        {
            bool reduceNoise = signal["reduce_noise"]?.Value<bool>() ?? false;
            if (reduceNoise)
            {
                backgroundAudioSource.volume =
                    Mathf.Max(0, originalBgVolume * 0.3f);
                Debug.Log("🔇 Background noise reduced");
            }
        }

        // Reduce lighting intensity (less visual overwhelm)
        if (sceneLights != null)
        {
            foreach (var light in sceneLights)
                light.intensity = originalLightIntensity * 0.7f;
        }

        // Tell NPC to simplify expressions
        bool simplifyNpc = signal["simplify_npc"]?.Value<bool>() ?? false;
        if (simplifyNpc && npcGameObject != null)
        {
            var npc = npcGameObject.GetComponent<NPCController>();
            npc?.SetExpressionMode("simplified");
            Debug.Log("😐 NPC expressions simplified");
        }

        // Slow NPC speech
        bool slowSpeech = signal["slow_npc_speech"]?.Value<bool>() ?? false;
        if (slowSpeech && npcGameObject != null)
        {
            var npc = npcGameObject.GetComponent<NPCController>();
            npc?.SetSpeechRate(0.7f);
            Debug.Log("🐢 NPC speech slowed");
        }

        // Add visual prompts to guide child
        bool addPrompts = signal["add_visual_prompts"]?.Value<bool>() ?? false;
        if (addPrompts && visualPromptPrefab != null)
        {
            Instantiate(visualPromptPrefab,
                        npcGameObject.transform.position + Vector3.up * 2f,
                        Quaternion.identity);
            Debug.Log("💡 Visual prompt added");
        }
    }

    void IncreaseComplexity(JObject signal)
    {
        // Restore original audio levels
        if (backgroundAudioSource != null)
            backgroundAudioSource.volume = originalBgVolume;

        // Restore lighting
        if (sceneLights != null)
        {
            foreach (var light in sceneLights)
                light.intensity = originalLightIntensity;
        }

        // Tell NPC to use full expressions
        if (npcGameObject != null)
        {
            var npc = npcGameObject.GetComponent<NPCController>();
            npc?.SetExpressionMode("full");
            npc?.SetSpeechRate(1.0f);
        }

        Debug.Log("⬆️ Complexity increased");
    }

    void AddAttentionPrompt(JObject signal)
    {
        bool highlightFace = signal["highlight_npc_face"]?.Value<bool>() ?? false;

        if (highlightFace && npcGameObject != null)
        {
            var npc = npcGameObject.GetComponent<NPCController>();
            npc?.HighlightFace(true);
        }

        bool playName = signal["play_name_audio"]?.Value<bool>() ?? false;
        if (playName && npcGameObject != null)
        {
            var npc = npcGameObject.GetComponent<NPCController>();
            npc?.PlayNameCallout();
        }
    }

    void LoadNextScenario(JObject signal)
    {
        bool celebrate = signal["celebrate"]?.Value<bool>() ?? false;
        if (celebrate)
        {
            // Play positive reinforcement audio/animation
            Debug.Log("🎉 Playing celebration feedback");
            // ParticleSystem, audio clip etc.
        }

        // Load next Unity scene
        float newDifficulty = signal["new_difficulty"]?.Value<float>() ?? 1.0f;
        Debug.Log($"➡️ Loading next scenario at difficulty {newDifficulty}");
        // UnityEngine.SceneManagement.SceneManager.LoadScene("NextScene");
    }

    void PauseScenario()
    {
        Time.timeScale = 0f;
        Debug.Log("⏸ Scenario paused by therapist");
    }

    void EndScenario()
    {
        Time.timeScale = 1f;
        Debug.Log("🔚 Scenario ended");
        // Return to main menu or notify session manager
        FindAnyObjectByType<VRSessionManager>()?.SendSessionEvent("scenario_ended");
    }
}