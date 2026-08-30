from django.db import models
from users.models import User

class VRSession(models.Model):
    STATUS = [
        ('pending',    'Pending'),
        ('active',     'Active'),
        ('completed',  'Completed'),
        ('terminated', 'Terminated'),  # ended early due to distress
    ]

    child           = models.ForeignKey(User, on_delete=models.CASCADE,
                                        related_name='sessions')
    therapist       = models.ForeignKey(User, on_delete=models.SET_NULL,
                                        null=True, related_name='supervised')
    scenario_type   = models.CharField(max_length=50)
    # ↑ 'emotion_recognition' | 'social_roleplay' |
    #   'sensory_calibration' | 'joint_attention'

    status          = models.CharField(max_length=20, choices=STATUS,
                                        default='pending')
    difficulty_level = models.FloatField(default=1.0)
    # ↑ RL agent updates this continuously during session

    started_at      = models.DateTimeField(null=True)
    ended_at        = models.DateTimeField(null=True)
    duration_seconds = models.IntegerField(default=0)
    created_at      = models.DateTimeField(auto_now_add=True)


class GazeEvent(models.Model):
    """
    Stores raw gaze data streamed from Unity eye tracker
    One row per gaze sample (captured at ~90Hz, sampled down)
    """
    session         = models.ForeignKey(VRSession, on_delete=models.CASCADE,
                                        related_name='gaze_events')
    timestamp_ms    = models.BigIntegerField()
    # ↑ milliseconds since session start

    gaze_x          = models.FloatField()   # normalised 0-1
    gaze_y          = models.FloatField()
    gaze_target     = models.CharField(max_length=100)
    # ↑ Unity object name: 'NPC_Face', 'NPC_Eyes',
    #   'Environment', 'Hands', 'UI_Element'

    fixation_duration_ms = models.IntegerField(default=0)
    is_joint_attention   = models.BooleanField(default=False)
    # ↑ True when child and NPC both attend to same object


class EmotionReading(models.Model):
    """
    Output of CNN emotion classifier run on camera frame
    Captured every 2 seconds during active session
    """
    session         = models.ForeignKey(VRSession, on_delete=models.CASCADE,
                                        related_name='emotion_readings')
    timestamp_ms    = models.BigIntegerField()

    # Probabilities from CNN (sum to 1.0)
    emotion_neutral  = models.FloatField(default=0)
    emotion_happy    = models.FloatField(default=0)
    emotion_anxious  = models.FloatField(default=0)
    emotion_confused = models.FloatField(default=0)
    emotion_distress = models.FloatField(default=0)

    dominant_emotion = models.CharField(max_length=20)
    stress_index     = models.FloatField(default=0)
    # ↑ composite score 0-1, triggers RL env adjustment


class MotorEvent(models.Model):
    """
    Full-body and hand tracking data from Unity
    """
    session         = models.ForeignKey(VRSession, on_delete=models.CASCADE,
                                        related_name='motor_events')
    timestamp_ms    = models.BigIntegerField()
    event_type      = models.CharField(max_length=50)
    # ↑ 'reach', 'point', 'wave', 'step_forward',
    #   'step_back', 'head_turn', 'freeze'

    body_position   = models.JSONField()
    # ↑ { x, y, z } world coordinates

    hand_landmarks  = models.JSONField(null=True)
    # ↑ 21 landmarks per hand from MediaPipe

    gesture_label   = models.CharField(max_length=50, blank=True)
    # ↑ classified gesture: 'pointing', 'waving', 'open_palm'


class AdaptiveSignal(models.Model):
    """
    Records every time the RL agent changes the environment
    Useful for analysing what triggered environment adjustments
    """
    session         = models.ForeignKey(VRSession, on_delete=models.CASCADE,
                                        related_name='adaptive_signals')
    timestamp_ms    = models.BigIntegerField()

    trigger         = models.CharField(max_length=50)
    # ↑ 'stress_spike' | 'low_engagement' | 'task_completed'
    #   | 'gaze_avoidance' | 'therapist_override'

    action_taken    = models.CharField(max_length=100)
    # ↑ 'reduce_background_noise' | 'simplify_npc_expressions'
    #   | 'add_visual_prompt' | 'increase_npc_patience'
    #   | 'end_scenario'

    previous_difficulty = models.FloatField()
    new_difficulty      = models.FloatField()