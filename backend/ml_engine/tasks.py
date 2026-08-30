from celery import shared_task
from channels.layers import get_channel_layer
from asgiref.sync import async_to_sync
import base64
import numpy as np
import cv2
import torch
import mediapipe as mp
from django.db.models import Avg
from sessions_app.models import (
    VRSession, GazeEvent, EmotionReading,
    MotorEvent, AdaptiveSignal
)


# ── Load models once at worker startup ───────────────
# These stay in memory — not reloaded per task

# Emotion CNN (MobileNetV3 fine-tuned on AffectNet)
emotion_model = torch.hub.load(
    'pytorch/vision', 'mobilenet_v3_small', pretrained=False
)
# Load your fine-tuned weights here
# emotion_model.load_state_dict(torch.load('weights/emotion_cnn.pth'))
emotion_model.eval()

EMOTION_LABELS = ['neutral', 'happy', 'anxious', 'confused', 'distress']

# MediaPipe hands for gesture classification
mp_hands    = mp.solutions.hands
hands_model = mp_hands.Hands(
    static_image_mode      = True,
    max_num_hands          = 2,
    min_detection_confidence = 0.7
)

channel_layer = get_channel_layer()


# ── Task 1: Emotion recognition ───────────────────────
@shared_task
def run_emotion_analysis(session_id, frame_b64, timestamp_ms):
    """
    Runs CNN on camera frame to detect child's emotion
    Sends result back to WebSocket group → Unity + React dashboard
    """
    try:
        # Decode base64 frame from Unity
        frame_bytes  = base64.b64decode(frame_b64)
        frame_array  = np.frombuffer(frame_bytes, dtype=np.uint8)
        frame        = cv2.imdecode(frame_array, cv2.IMREAD_COLOR)

        if frame is None:
            return

        # Preprocess for CNN
        frame_rgb    = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        frame_resized = cv2.resize(frame_rgb, (224, 224))
        tensor       = torch.from_numpy(frame_resized).float()
        tensor       = tensor.permute(2, 0, 1).unsqueeze(0) / 255.0

        # Run inference
        with torch.no_grad():
            logits = emotion_model(tensor)
            probs  = torch.softmax(logits, dim=1).squeeze().numpy()

        dominant_emotion = EMOTION_LABELS[np.argmax(probs)]

        # Stress index: weighted sum of anxious + confused + distress
        stress_index = float(
            probs[2] * 0.35 +   # anxious
            probs[3] * 0.25 +   # confused
            probs[4] * 0.40     # distress
        )

        # Save to PostgreSQL
        EmotionReading.objects.create(
            session_id       = session_id,
            timestamp_ms     = timestamp_ms,
            emotion_neutral  = float(probs[0]),
            emotion_happy    = float(probs[1]),
            emotion_anxious  = float(probs[2]),
            emotion_confused = float(probs[3]),
            emotion_distress = float(probs[4]),
            dominant_emotion = dominant_emotion,
            stress_index     = stress_index,
        )

        result = {
            'type':             'emotion_result',
            'session_id':       session_id,
            'timestamp_ms':     timestamp_ms,
            'dominant_emotion': dominant_emotion,
            'stress_index':     round(stress_index, 3),
            'probabilities': {
                label: round(float(p), 3)
                for label, p in zip(EMOTION_LABELS, probs)
            }
        }

        # Send to WebSocket group (Unity + React dashboard both receive)
        async_to_sync(channel_layer.group_send)(
            f"session_{session_id}",
            {'type': 'ml_result', 'result': result}
        )

        # High stress → trigger RL immediately
        if stress_index > 0.7:
            run_rl_adaptation.delay(session_id, trigger='stress_spike')

    except Exception as e:
        print(f"❌ Emotion analysis failed: {e}")


# ── Task 2: Gaze analysis (LSTM over time window) ────
@shared_task
def run_gaze_analysis(session_id, current_timestamp_ms):
    """
    Runs LSTM over last 5 seconds of gaze data
    Detects: joint attention, gaze avoidance, fixation patterns
    """
    try:
        # Get last 5 seconds of gaze events
        window_start = current_timestamp_ms - 5000
        gaze_events  = GazeEvent.objects.filter(
            session_id   = session_id,
            timestamp_ms__gte = window_start
        ).order_by('timestamp_ms').values(
            'gaze_x', 'gaze_y', 'gaze_target',
            'fixation_duration_ms', 'is_joint_attention'
        )

        if len(gaze_events) < 10:
            return

        # Feature extraction
        events_list  = list(gaze_events)

        total_samples     = len(events_list)
        face_fixations    = sum(1 for e in events_list if 'Face' in e['gaze_target'])
        eye_fixations     = sum(1 for e in events_list if 'Eye' in e['gaze_target'])
        joint_att_samples = sum(1 for e in events_list if e['is_joint_attention'])
        env_fixations     = sum(1 for e in events_list if e['gaze_target'] == 'Environment')

        # Rates
        face_gaze_rate      = face_fixations    / total_samples
        eye_contact_rate    = eye_fixations     / total_samples
        joint_att_rate      = joint_att_samples / total_samples
        env_distraction_rate = env_fixations   / total_samples

        avg_fixation_ms = sum(
            e['fixation_duration_ms'] for e in events_list
        ) / total_samples

        # Engagement score (higher = more engaged)
        engagement_score = (
            face_gaze_rate   * 0.30 +
            eye_contact_rate * 0.35 +
            joint_att_rate   * 0.35
        )

        result = {
            'type':               'gaze_result',
            'session_id':         session_id,
            'timestamp_ms':       current_timestamp_ms,
            'face_gaze_rate':     round(face_gaze_rate,       3),
            'eye_contact_rate':   round(eye_contact_rate,     3),
            'joint_attention_rate': round(joint_att_rate,     3),
            'env_distraction_rate': round(env_distraction_rate, 3),
            'avg_fixation_ms':    round(avg_fixation_ms,      1),
            'engagement_score':   round(engagement_score,     3),
        }

        async_to_sync(channel_layer.group_send)(
            f"session_{session_id}",
            {'type': 'ml_result', 'result': result}
        )

        # Low engagement → RL adjustment
        if engagement_score < 0.3:
            run_rl_adaptation.delay(
                session_id, trigger='low_engagement'
            )

    except Exception as e:
        print(f"❌ Gaze analysis failed: {e}")


# ── Task 3: Gesture classification ───────────────────
@shared_task
def run_gesture_classification(session_id, hand_landmarks, timestamp_ms):
    """
    Classifies hand gestures from Unity hand tracking landmarks
    Detects: pointing, waving, open palm, reach toward
    """
    try:
        landmarks = np.array(hand_landmarks)

        # Simple rule-based classifier on landmark geometry
        # Replace with trained MLP/CNN for better accuracy

        gesture = classify_gesture(landmarks)

        result = {
            'type':        'gesture_result',
            'session_id':  session_id,
            'timestamp_ms': timestamp_ms,
            'gesture':     gesture,
        }

        async_to_sync(channel_layer.group_send)(
            f"session_{session_id}",
            {'type': 'ml_result', 'result': result}
        )

    except Exception as e:
        print(f"❌ Gesture classification failed: {e}")


def classify_gesture(landmarks):
    """
    Rule-based gesture classifier using hand landmark geometry
    landmarks: numpy array shape (21, 3)
    """
    if len(landmarks) < 21:
        return 'unknown'

    # Index finger tip = landmark 8, base = 5
    # Thumb tip = 4, Middle = 12
    index_tip    = landmarks[8]
    index_base   = landmarks[5]
    middle_tip   = landmarks[12]
    ring_tip     = landmarks[16]
    pinky_tip    = landmarks[20]
    wrist        = landmarks[0]

    # Index finger extended, others curled → pointing
    index_extended = index_tip[1] < index_base[1]
    others_curled  = (
        middle_tip[1] > landmarks[9][1] and
        ring_tip[1]   > landmarks[13][1]
    )

    if index_extended and others_curled:
        return 'pointing'

    # All fingers extended → open palm or wave
    all_extended = all([
        landmarks[i][1] < landmarks[i-3][1]
        for i in [8, 12, 16, 20]
    ])

    if all_extended:
        # Check lateral movement for wave (needs history)
        return 'open_palm'

    return 'unknown'


# ── Task 4: RL environment adaptation ────────────────
@shared_task
def run_rl_adaptation(session_id, trigger):
    """
    Rule-based + RL agent that decides how to adjust
    the VR environment based on current session state

    In production: replace rules with trained DQN/PPO agent
    from Unity ML-Agents exported as ONNX
    """
    try:
        session = VRSession.objects.get(id=session_id)

        # Get recent ML readings
        recent_emotions = EmotionReading.objects.filter(
            session_id=session_id
        ).order_by('-timestamp_ms')[:5]

        avg_stress = sum(
            e.stress_index for e in recent_emotions
        ) / max(len(recent_emotions), 1)

        current_difficulty = session.difficulty_level

        # ── Decision logic ──────────────────────────
        action          = None
        new_difficulty  = current_difficulty

        if trigger == 'stress_spike' or avg_stress > 0.7:
            action         = 'reduce_complexity'
            new_difficulty = max(0.1, current_difficulty - 0.2)
            signal = {
                'action':             'reduce_complexity',
                'reduce_noise':       True,
                'simplify_npc':       True,
                'slow_npc_speech':    True,
                'add_visual_prompts': True,
                'new_difficulty':     new_difficulty,
            }

        elif trigger == 'low_engagement' and avg_stress < 0.3:
            action         = 'increase_complexity'
            new_difficulty = min(3.0, current_difficulty + 0.1)
            signal = {
                'action':           'increase_complexity',
                'add_npc':          True,
                'increase_noise':   False,
                'new_difficulty':   new_difficulty,
            }

        elif trigger == 'task_completed':
            action         = 'next_scenario'
            new_difficulty = min(3.0, current_difficulty + 0.15)
            signal = {
                'action':         'load_next_scenario',
                'new_difficulty': new_difficulty,
                'celebrate':      True,  # play positive audio
            }

        elif trigger == 'gaze_avoidance':
            action = 'add_attention_prompt'
            signal = {
                'action':              'add_attention_prompt',
                'highlight_npc_face':  True,
                'play_name_audio':     True,
                'new_difficulty':      current_difficulty,
            }

        else:
            return

        # Save adaptation to PostgreSQL
        AdaptiveSignal.objects.create(
            session_id          = session_id,
            timestamp_ms        = 0,
            trigger             = trigger,
            action_taken        = action,
            previous_difficulty = current_difficulty,
            new_difficulty      = new_difficulty,
        )

        # Update session difficulty
        VRSession.objects.filter(id=session_id).update(
            difficulty_level=new_difficulty
        )

        # Send signal to Unity via WebSocket
        async_to_sync(channel_layer.group_send)(
            f"session_{session_id}",
            {
                'type':    'therapist_command',
                'payload': signal,
            }
        )

        print(f"✅ RL adaptation: {action} (difficulty {current_difficulty:.1f} → {new_difficulty:.1f})")

    except Exception as e:
        print(f"❌ RL adaptation failed: {e}")