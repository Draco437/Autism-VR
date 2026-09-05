using System;
using System.Collections;
using System.Text;
using UnityEngine;
using NativeWebSocket;
using Newtonsoft.Json;

public class VRSessionManager : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────
    [Header("Session Config")]
    public string backendUrl = "ws://localhost:8000/ws/session/";
    public int sessionId = 1;
    public string authToken = "";
    // ↑ paste JWT token here for testing
    // in production read from PlayerPrefs after login

    // ── Private fields ────────────────────────────────
    WebSocket websocket;
    long sessionStartMs;
    bool isConnected = false;

    // ── References to other scripts ───────────────────
    GazeTracker gazeTracker;
    CameraCapture cameraCapture;
    HandTracker handTracker;
    AdaptiveEnvironment adaptiveEnv;

    // ── Events other scripts can listen to ────────────
    public static event Action<string> OnAdaptiveSignal;
    public static event Action<string> OnTherapistOverride;

    // ─────────────────────────────────────────────────
    async void Start()
    {
        // Get references (Updated for Unity 6)
        gazeTracker = FindAnyObjectByType<GazeTracker>();
        cameraCapture = FindAnyObjectByType<CameraCapture>();
        handTracker = FindAnyObjectByType<HandTracker>();
        adaptiveEnv = FindAnyObjectByType<AdaptiveEnvironment>();

        sessionStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Build WebSocket URL
        string url = $"{backendUrl}{sessionId}/";
        websocket = new WebSocket(url);

        // ── WebSocket event handlers ──────────────────
        websocket.OnOpen += () =>
        {
            isConnected = true;
            Debug.Log($"✅ Connected to Django session {sessionId}");
        };

        websocket.OnError += (error) =>
        {
            Debug.LogError($"❌ WebSocket error: {error}");
        };

        websocket.OnClose += (code) =>
        {
            isConnected = false;
            Debug.Log($"🔌 Disconnected: {code}");
        };

        websocket.OnMessage += (bytes) =>
        {
            string json = Encoding.UTF8.GetString(bytes);
            HandleServerMessage(json);
        };

        await websocket.Connect();
    }

    // ─────────────────────────────────────────────────
    void Update()
    {
        // Required by NativeWebSocket to dispatch messages
        // on the main Unity thread
#if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
#endif
    }

    // ── Handle messages from Django ───────────────────
    void HandleServerMessage(string json)
    {
        try
        {
            var msg = JsonConvert.DeserializeObject<ServerMessage>(json);

            if (msg.type == "adaptive_signal")
            {
                Debug.Log($"🎮 Adaptive signal: {msg.payload}");
                OnAdaptiveSignal?.Invoke(msg.payload?.ToString());
                // AdaptiveEnvironment.cs listens to this event
            }
            else if (msg.type == "therapist_override")
            {
                Debug.Log($"👨‍⚕️ Therapist override: {msg.payload}");
                OnTherapistOverride?.Invoke(msg.payload?.ToString());
            }
            else if (msg.type == "ml_result")
            {
                Debug.Log($"🤖 ML result received: {msg.type}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Parse error: {e.Message}");
        }
    }

    // ── Send any packet to Django ─────────────────────
    public async void SendPacket(object packet)
    {
        if (!isConnected) return;

        if (websocket.State == WebSocketState.Open)
        {
            string json = JsonConvert.SerializeObject(packet);
            await websocket.SendText(json);
        }
    }

    // ── Helper: ms since session started ─────────────
    public long GetSessionMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sessionStartMs;
    }

    // ── Send session event ────────────────────────────
    public void SendSessionEvent(string eventName)
    {
        SendPacket(new
        {
            type = "session_event",
            timestamp_ms = GetSessionMs(),
            payload = new { @event = eventName }
        });
    }

    // ── Cleanup on exit ───────────────────────────────
    async void OnApplicationQuit()
    {
        if (websocket != null)
            await websocket.Close();
    }
}

// ── Message classes for JSON deserialization ──────────
[Serializable]
public class ServerMessage
{
    public string type;

    [System.NonSerialized]
    public object payload;
}