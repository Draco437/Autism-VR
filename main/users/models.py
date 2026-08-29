from django.contrib.auth.models import AbstractUser
from django.db import models

class User(AbstractUser):
    ROLE_CHOICES = [
        ('child',     'Child'),
        ('therapist', 'Therapist'),
        ('parent',    'Parent'),
    ]
    role            = models.CharField(max_length=20, choices=ROLE_CHOICES)
    age             = models.IntegerField(null=True, blank=True)
    asd_profile     = models.JSONField(default=dict)
    # ↑ stores: sensory_sensitivity, communication_level,
    #   preferred_scenarios, trigger_stimuli etc.
    created_at      = models.DateTimeField(auto_now_add=True)
