from django.urls import re_path
from .consumers import VRSessionConsumer

websocket_urlpatterns = [
    re_path(r'ws/session/(?P<session_id>\d+)/$', VRSessionConsumer.as_asgi()),
]