using System.Collections;
using UnityEngine;

public class GazeTracker : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────
    [Header("Gaze Settings")]
    public float sampleIntervalSeconds = 0.1f;
    // ↑ sample gaze every 100ms = 10 times per second
    // increase frequency if headset supports it

    public LayerMask gazeLayerMask;
    // ↑ set this in Inspector to include NPC and environment layers

    // ── Private fields ────────────────────────────────
    VRSessionManager sessionManager;
    Camera vrCamera;

    string lastGazeTarget = "";
    float fixationStartTime = 0f;
    string currentFixationTarget = "";
    bool isJointAttention = false;

    // ─────────────────────────────────────────────────
    void Start()
    {
        sessionManager = FindAnyObjectByType<VRSessionManager>();
        vrCamera = Camera.main;

        StartCoroutine(SampleGaze());
    }

    // ── Sample gaze on interval ───────────────────────
    IEnumerator SampleGaze()
    {
        while (true)
        {
            yield return new WaitForSeconds(sampleIntervalSeconds);
            DetectGaze();
        }
    }

    // ── Raycast from camera to find gaze target ───────
    void DetectGaze()
    {
        // In real OpenXR eye tracking use:
        // UnityEngine.XR.InputDevice.TryGetFeatureValue
        // For now raycast from camera center as fallback

        Ray ray = new Ray(vrCamera.transform.position,
                                  vrCamera.transform.forward);
        RaycastHit hit;

        string gazeTarget = "Environment";
        Vector2 gazePoint = new Vector2(0.5f, 0.5f);
        // ↑ normalised screen centre as default

        if (Physics.Raycast(ray, out hit, 100f, gazeLayerMask))
        {
            gazeTarget = hit.collider.gameObject.name;

            // Convert hit point to normalised screen coords
            Vector3 screenPoint = vrCamera.WorldToViewportPoint(hit.point);
            gazePoint = new Vector2(screenPoint.x, screenPoint.y);
        }

        // ── Track fixation duration ───────────────────
        int fixationMs = 0;

        if (gazeTarget == currentFixationTarget)
        {
            fixationMs = (int)((Time.time - fixationStartTime) * 1000);
        }
        else
        {
            currentFixationTarget = gazeTarget;
            fixationStartTime = Time.time;
        }

        // ── Check joint attention ─────────────────────
        // Joint attention = child and NPC both looking at same object
        // NPCController sets this flag when NPC looks at an object
        isJointAttention = NPCController.CurrentNPCGazeTarget == gazeTarget
                           && gazeTarget != "Environment";

        // ── Send to Django ────────────────────────────
        sessionManager.SendPacket(new
        {
            type = "gaze_data",
            timestamp_ms = sessionManager.GetSessionMs(),
            payload = new
            {
                x = gazePoint.x,
                y = gazePoint.y,
                target = gazeTarget,
                fixation_ms = fixationMs,
                joint_attention = isJointAttention,
            }
        });
    }
}