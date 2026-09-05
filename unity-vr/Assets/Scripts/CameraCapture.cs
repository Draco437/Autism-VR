using System;
using System.Collections;
using UnityEngine;

public class CameraCapture : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────
    [Header("Camera Settings")]
    public float captureIntervalSeconds = 2f;
    // ↑ send frame to Django every 2 seconds for emotion analysis
    // more frequent = more accurate but heavier network load

    public int captureWidth = 224;
    public int captureHeight = 224;
    // ↑ MobileNet input size — no point sending larger frames

    // ── Private fields ────────────────────────────────
    VRSessionManager sessionManager;
    Camera faceCamera;
    // ↑ assign a separate camera pointing at user's face
    // OR use passthrough camera on Meta Quest

    RenderTexture renderTexture;
    Texture2D captureTexture;

    // ─────────────────────────────────────────────────
    void Start()
    {
        sessionManager = FindAnyObjectByType<VRSessionManager>();

        // Create render texture for capturing
        renderTexture = new RenderTexture(captureWidth, captureHeight, 24);
        captureTexture = new Texture2D(captureWidth, captureHeight,
                                       TextureFormat.RGB24, false);

        // If no separate face camera, use main camera
        faceCamera = GetComponent<Camera>() ?? Camera.main;

        StartCoroutine(CaptureFrameLoop());
    }

    // ── Capture loop ──────────────────────────────────
    IEnumerator CaptureFrameLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(captureIntervalSeconds);
            yield return new WaitForEndOfFrame();
            // ↑ wait for end of frame ensures render is complete

            CaptureAndSend();
        }
    }

    // ── Capture frame and send to Django ─────────────
    void CaptureAndSend()
    {
        try
        {
            // Render camera to texture
            RenderTexture prev = faceCamera.targetTexture;
            faceCamera.targetTexture = renderTexture;
            faceCamera.Render();

            // Read pixels from render texture
            RenderTexture.active = renderTexture;
            captureTexture.ReadPixels(
                new Rect(0, 0, captureWidth, captureHeight), 0, 0
            );
            captureTexture.Apply();

            // Reset
            faceCamera.targetTexture = prev;
            RenderTexture.active = null;

            // Encode to JPEG and then base64
            byte[] jpegBytes = captureTexture.EncodeToJPG(quality: 75);
            string base64 = Convert.ToBase64String(jpegBytes);

            // Send to Django → Celery runs CNN emotion analysis
            sessionManager.SendPacket(new
            {
                type = "camera_frame",
                timestamp_ms = sessionManager.GetSessionMs(),
                payload = new { frame_b64 = base64 }
            });

            Debug.Log($"📸 Frame sent: {jpegBytes.Length / 1024}KB");
        }
        catch (Exception e)
        {
            Debug.LogError($"❌ Capture failed: {e.Message}");
        }
    }

    // ── Cleanup ───────────────────────────────────────
    void OnDestroy()
    {
        if (renderTexture != null) renderTexture.Release();
        if (captureTexture != null) Destroy(captureTexture);
    }
}