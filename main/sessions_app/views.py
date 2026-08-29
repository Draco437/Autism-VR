from django.db import models
from rest_framework.views import APIView
from rest_framework.response import Response
from rest_framework.permissions import IsAuthenticated
from django.db.models import Avg, Count
from sessions_app.models import VRSession, GazeEvent, EmotionReading, AdaptiveSignal
from users.models import User


class CreateSessionView(APIView):
    """
    React dashboard calls this to start a new VR session
    Returns session_id → Unity uses it to connect via WebSocket
    ws://backend/ws/session/<session_id>/
    """
    permission_classes = [IsAuthenticated]

    def post(self, request):
        child_id      = request.data.get('child_id')
        scenario_type = request.data.get('scenario_type', 'emotion_recognition')

        try:
            child = User.objects.get(id=child_id, role='child')
        except User.DoesNotExist:
            return Response({'error': 'Child not found'}, status=404)

        session = VRSession.objects.create(
            child         = child,
            therapist     = request.user,
            scenario_type = scenario_type,
            status        = 'pending',
        )

        return Response({
            'session_id':    session.id,
            'ws_url':        f"ws://localhost:8000/ws/session/{session.id}/",
            'scenario_type': scenario_type,
        }, status=201)


class SessionAnalyticsView(APIView):
    """
    Returns aggregated analytics for a session
    React dashboard shows this as live charts during session
    Also used for post-session report
    """
    permission_classes = [IsAuthenticated]

    def get(self, request, session_id):
        session = VRSession.objects.get(id=session_id)

        # Emotion summary
        emotion_avg = EmotionReading.objects.filter(
            session_id=session_id
        ).aggregate(
            avg_stress   = Avg('stress_index'),
            avg_happy    = Avg('emotion_happy'),
            avg_anxious  = Avg('emotion_anxious'),
            avg_neutral  = Avg('emotion_neutral'),
        )

        # Gaze summary
        gaze_summary = GazeEvent.objects.filter(
            session_id=session_id
        ).aggregate(
            total_samples    = Count('id'),
            joint_att_count  = Count('id', filter=models.Q(is_joint_attention=True)),
            face_gaze_count  = Count('id', filter=models.Q(gaze_target__contains='Face')),
            eye_gaze_count   = Count('id', filter=models.Q(gaze_target__contains='Eye')),
        )

        total = gaze_summary['total_samples'] or 1

        # Adaptation history
        adaptations = list(AdaptiveSignal.objects.filter(
            session_id=session_id
        ).values('trigger', 'action_taken', 'timestamp_ms',
                  'previous_difficulty', 'new_difficulty'))

        return Response({
            'session': {
                'id':              session.id,
                'status':          session.status,
                'scenario_type':   session.scenario_type,
                'duration_seconds': session.duration_seconds,
                'difficulty_level': session.difficulty_level,
            },
            'emotion': {
                'avg_stress':   round(emotion_avg['avg_stress']  or 0, 3),
                'avg_happy':    round(emotion_avg['avg_happy']   or 0, 3),
                'avg_anxious':  round(emotion_avg['avg_anxious'] or 0, 3),
                'avg_neutral':  round(emotion_avg['avg_neutral'] or 0, 3),
            },
            'gaze': {
                'total_samples':       total,
                'joint_attention_rate': round(gaze_summary['joint_att_count'] / total, 3),
                'face_gaze_rate':      round(gaze_summary['face_gaze_count']  / total, 3),
                'eye_contact_rate':    round(gaze_summary['eye_gaze_count']   / total, 3),
            },
            'adaptations': adaptations,
        })


class ChildProgressView(APIView):
    """
    Returns progress across all sessions for one child
    Used in React dashboard for longitudinal tracking
    """
    permission_classes = [IsAuthenticated]

    def get(self, request, child_id):
        sessions = VRSession.objects.filter(
            child_id = child_id,
            status   = 'completed'
        ).order_by('created_at')

        progress = []
        for session in sessions:
            emotion_avg = EmotionReading.objects.filter(
                session_id=session.id
            ).aggregate(avg_stress=Avg('stress_index'))

            gaze_data = GazeEvent.objects.filter(session_id=session.id)
            total     = gaze_data.count() or 1
            joint_att = gaze_data.filter(is_joint_attention=True).count()

            progress.append({
                'session_id':       session.id,
                'date':             session.created_at.date().isoformat(),
                'scenario_type':    session.scenario_type,
                'duration_seconds': session.duration_seconds,
                'avg_stress':       round(emotion_avg['avg_stress'] or 0, 3),
                'joint_att_rate':   round(joint_att / total, 3),
                'difficulty_reached': session.difficulty_level,
            })

        return Response({'child_id': child_id, 'progress': progress})