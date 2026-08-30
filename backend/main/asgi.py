"""
ASGI config for main project.

It exposes the ASGI callable as a module-level variable named ``application``.

For more information on this file, see
https://docs.djangoproject.com/en/6.1/howto/deployment/asgi/
"""

# core/asgi.py

import os
import django
from channels.routing import ProtocolTypeRouter, URLRouter
from channels.auth import AuthMiddlewareStack
from django.core.asgi import get_asgi_application
import sessions_app

os.environ.setdefault('DJANGO_SETTINGS_MODULE', 'main.settings')
django.setup()

application = ProtocolTypeRouter({
    # Regular HTTP → standard Django views
    'http': get_asgi_application(),

    # WebSocket → Unity VR client connects here
    'websocket': AuthMiddlewareStack(
        URLRouter(sessions_app.routing.websocket_urlpatterns)
    ),
})
