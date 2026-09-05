using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class HandTracker : MonoBehaviour
{
    // ── Inspector fields ──────────────────────────────
    [Header("Hand Tracking Settings")]
    public float sendIntervalSeconds = 0.2f;
    // ↑ send hand data 5 times per second

    // ── Private fields ────────────────────────────────
    VRSessionManager sessionManager;

    InputDevice leftHand;
    InputDevice rightHand;

    // ─────────────────────────────────────────────────
    void Start()
    {
        sessionManager = FindAnyObjectByType<VRSessionManager>();

        // Get XR hand devices
        var leftHandDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.HandTracking,
            leftHandDevices
        );
        if (leftHandDevices.Count > 0)
            leftHand = leftHandDevices[0];

        var rightHandDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.HandTracking,
            rightHandDevices
        );
        if (rightHandDevices.Count > 0)
            rightHand = rightHandDevices[0];

        StartCoroutine(SendHandData());
    }

    // ── Send hand tracking data to Django ─────────────
    IEnumerator SendHandData()
    {
        while (true)
        {
            yield return new WaitForSeconds(sendIntervalSeconds);

            Hand leftHandData, rightHandData;
            List<object> landmarks = new List<object>();

            // ── Left hand landmarks ───────────────────
            if (leftHand.TryGetFeatureValue(
                CommonUsages.handData, out leftHandData))
            {
                landmarks.AddRange(ExtractLandmarks(leftHandData, "left"));
            }

            // ── Right hand landmarks ──────────────────
            if (rightHand.TryGetFeatureValue(
                CommonUsages.handData, out rightHandData))
            {
                landmarks.AddRange(ExtractLandmarks(rightHandData, "right"));
            }

            if (landmarks.Count == 0) continue;

            // ── Body position (head transform) ────────
            Vector3 headPos = Camera.main.transform.position;

            sessionManager.SendPacket(new
            {
                type = "motor_data",
                timestamp_ms = sessionManager.GetSessionMs(),
                payload = new
                {
                    event_type = "hand_tracking",
                    position = new
                    {
                        x = headPos.x,
                        y = headPos.y,
                        z = headPos.z
                    },
                    hand_landmarks = landmarks,
                    gesture = ""
                    // ↑ gesture classified by Django ML worker
                }
            });
        }
    }

    // ── Extract 21 landmarks from XR Hand ────────────
    List<object> ExtractLandmarks(Hand hand, string side)
    {
        var landmarks = new List<object>();
        var bones = new List<Bone>();

        if (hand.TryGetFingerBones(HandFinger.Index, bones) ||
            hand.TryGetFingerBones(HandFinger.Middle, bones) ||
            hand.TryGetFingerBones(HandFinger.Ring, bones) ||
            hand.TryGetFingerBones(HandFinger.Pinky, bones) ||
            hand.TryGetFingerBones(HandFinger.Thumb, bones))
        {
            foreach (var bone in bones)
            {
                Vector3 pos;
                if (bone.TryGetPosition(out pos))
                {
                    landmarks.Add(new
                    {
                        x = pos.x,
                        y = pos.y,
                        z = pos.z,
                        side = side
                    });
                }
            }
        }

        return landmarks;
    }

    // ── Send body locomotion event ────────────────────
    public void SendLocomotionEvent(string eventType, Vector3 position)
    {
        sessionManager.SendPacket(new
        {
            type = "motor_data",
            timestamp_ms = sessionManager.GetSessionMs(),
            payload = new
            {
                event_type = eventType,
                position = new
                {
                    x = position.x,
                    y = position.y,
                    z = position.z
                },
                hand_landmarks = new object[] { },
                gesture = ""
            }
        });
    }
}