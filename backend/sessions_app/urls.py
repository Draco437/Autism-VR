from django.urls import path
from .views import CreateSessionView, SessionAnalyticsView, ChildProgressView

urlpatterns = [
    path('sessions/create/',               CreateSessionView.as_view()),
    path('sessions/<int:session_id>/analytics/', SessionAnalyticsView.as_view()),
    path('children/<int:child_id>/progress/',    ChildProgressView.as_view()),
]