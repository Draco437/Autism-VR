using UnityEngine;

public class NPCController : MonoBehaviour
{
    // ── Inspector references ──────────────────────────
    [Header("NPC Components")]
    public Animator npcAnimator;
    public AudioSource npcAudioSource;
    public AudioClip nameCalloutClip;
    public Renderer faceRenderer;
    // ↑ renderer on the NPC's face mesh for highlighting

    // ── Static: read by GazeTracker for joint attention ──
    public static string CurrentNPCGazeTarget = "";

    // ── Private fields ────────────────────────────────
    string expressionMode = "full";
    float speechRate = 1.0f;
    bool faceHighlighted = false;

    Material originalFaceMaterial;
    public Material highlightMaterial;

    // ─────────────────────────────────────────────────
    void Start()
    {
        if (faceRenderer != null)
            originalFaceMaterial = faceRenderer.material;
    }

    // ── Expression control ────────────────────────────
    public void SetExpressionMode(string mode)
    {
        expressionMode = mode;

        if (npcAnimator != null)
        {
            // "simplified" = neutral face, minimal movement
            // "full"       = normal expressive animations
            npcAnimator.SetBool("SimplifiedMode", mode == "simplified");
        }

        Debug.Log($"😐 NPC expression mode: {mode}");
    }

    // ── Speech rate control ───────────────────────────
    public void SetSpeechRate(float rate)
    {
        speechRate = rate;

        if (npcAudioSource != null)
            npcAudioSource.pitch = rate;
        // ↑ lower pitch = slower speech perception

        Debug.Log($"🗣️ Speech rate: {rate}");
    }

    // ── Face highlight ────────────────────────────────
    public void HighlightFace(bool highlight)
    {
        if (faceRenderer == null) return;

        faceHighlighted = highlight;

        if (highlight && highlightMaterial != null)
            faceRenderer.material = highlightMaterial;
        else
            faceRenderer.material = originalFaceMaterial;

        // Auto remove highlight after 3 seconds
        if (highlight)
            Invoke(nameof(RemoveHighlight), 3f);
    }

    void RemoveHighlight() => HighlightFace(false);

    // ── Name callout ──────────────────────────────────
    public void PlayNameCallout()
    {
        if (npcAudioSource != null && nameCalloutClip != null)
        {
            npcAudioSource.PlayOneShot(nameCalloutClip);
            Debug.Log("📢 NPC calling child's name");
        }
    }

    // ── NPC gaze (for joint attention detection) ──────
    public void SetNPCGazeTarget(string targetObjectName)
    {
        CurrentNPCGazeTarget = targetObjectName;
    }
}