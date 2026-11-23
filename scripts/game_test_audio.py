"""Keep audio disabled in isolated game tests unless the test needs sound."""

import json


def audio_arguments(enabled=False):
    return [] if enabled else ['--audio-driver', 'Dummy']


def mute_game_settings(settings):
    # The game's FMOD output also reads its own master volume on startup.
    settings['volume_master'] = 0.0


def configure_reusable_profile(settings_file, enabled=False):
    """Preserve the test profile's master volume for an explicit audio run."""
    saved_volume = settings_file.with_name('test-audio-volume.json')
    if settings_file.is_symlink() or saved_volume.is_symlink():
        raise ValueError('Test audio settings require regular files')
    if enabled and not saved_volume.exists():
        return
    settings_file.parent.mkdir(parents=True, exist_ok=True)
    settings = (json.loads(settings_file.read_text(encoding='utf-8-sig'))
                if settings_file.exists() else {'schema_version': 8})
    if enabled:
        volume = json.loads(saved_volume.read_text(encoding='utf-8'))['volume_master']
        if volume is None:
            settings.pop('volume_master', None)
        else:
            settings['volume_master'] = volume
    else:
        volume = settings.get('volume_master')
        if volume != 0 or not saved_volume.exists():
            saved_volume.write_text(json.dumps({'volume_master': volume}), encoding='utf-8')
        mute_game_settings(settings)
    settings_file.write_text(json.dumps(settings, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    if enabled:
        saved_volume.unlink()
