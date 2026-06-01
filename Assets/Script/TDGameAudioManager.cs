using UnityEngine;

[AddComponentMenu("TD/TD Game Audio Manager")]
public class TDGameAudioManager : MonoBehaviour
{
    enum MusicState
    {
        None,
        PreStart,
        Countdown,
        Gameplay
    }

    [Header("Sources")]
    [SerializeField] AudioSource _bgmSource;
    [SerializeField] AudioSource _countdownSource;
    [SerializeField] AudioSource _sfxSource;

    [Header("Source Volume")]
    [SerializeField, Range(0f, 1f)] float _bgmVolume = 1f;
    [SerializeField, Range(0f, 1f)] float _countdownVolume = 1f;
    [SerializeField, Range(0f, 1f)] float _sfxVolume = 1f;

    [Header("Music")]
    [SerializeField] AudioClip _preStartBgm;
    [SerializeField] AudioClip _gameplayBgm;
    [SerializeField] AudioClip _countdownLoopSfx;

    [Header("Events")]
    [SerializeField] AudioClip _playerBecameActiveSfx;
    [SerializeField] AudioClip _roundEndedSfx;
    [SerializeField] AudioClip _growPickupSfx;
    [SerializeField] AudioClip _paintBurstPickupSfx;
    [SerializeField] AudioClip _cleanseBurstPickupSfx;

    MusicState _state = MusicState.None;

    void Awake()
    {
        if (_bgmSource == null)
            _bgmSource = gameObject.AddComponent<AudioSource>();
        if (_countdownSource == null)
            _countdownSource = gameObject.AddComponent<AudioSource>();
        if (_sfxSource == null)
            _sfxSource = gameObject.AddComponent<AudioSource>();

        _bgmSource.loop = true;
        _bgmSource.playOnAwake = false;

        _countdownSource.loop = true;
        _countdownSource.playOnAwake = false;

        _sfxSource.loop = false;
        _sfxSource.playOnAwake = false;

        ApplySourceVolumes();
    }

    void OnValidate()
    {
        _bgmVolume = Mathf.Clamp01(_bgmVolume);
        _countdownVolume = Mathf.Clamp01(_countdownVolume);
        _sfxVolume = Mathf.Clamp01(_sfxVolume);
        ApplySourceVolumes();
    }

    public void EnterPreStart()
    {
        if (_state == MusicState.PreStart)
            return;

        _state = MusicState.PreStart;
        StopCountdown();
        PlayBgm(_preStartBgm);
    }

    public void EnterCountdown()
    {
        if (_state == MusicState.Countdown)
            return;

        _state = MusicState.Countdown;
        StopBgm();
        PlayCountdown();
    }

    public void EnterGameplay()
    {
        if (_state == MusicState.Gameplay)
            return;

        _state = MusicState.Gameplay;
        StopCountdown();
        PlayBgm(_gameplayBgm);
    }

    public void PlayPlayerBecameActive()
    {
        PlaySfx(_playerBecameActiveSfx);
    }

    public void PlayRoundEnded()
    {
        PlaySfx(_roundEndedSfx);
    }

    public void PlayPickupSfx(TDPowerupKind kind)
    {
        switch (kind)
        {
            case TDPowerupKind.Grow:
                PlaySfx(_growPickupSfx);
                break;
            case TDPowerupKind.PaintBurst:
                PlaySfx(_paintBurstPickupSfx);
                break;
            case TDPowerupKind.CleanseBurst:
                PlaySfx(_cleanseBurstPickupSfx);
                break;
        }
    }

    void PlayBgm(AudioClip clip)
    {
        if (_bgmSource == null)
            return;

        if (clip == null)
        {
            _bgmSource.Stop();
            _bgmSource.clip = null;
            return;
        }

        if (_bgmSource.clip != clip)
            _bgmSource.clip = clip;

        if (!_bgmSource.isPlaying)
            _bgmSource.Play();
    }

    void StopBgm()
    {
        if (_bgmSource != null)
            _bgmSource.Stop();
    }

    void PlayCountdown()
    {
        if (_countdownSource == null)
            return;

        if (_countdownLoopSfx == null)
        {
            _countdownSource.Stop();
            _countdownSource.clip = null;
            return;
        }

        if (_countdownSource.clip != _countdownLoopSfx)
            _countdownSource.clip = _countdownLoopSfx;

        if (!_countdownSource.isPlaying)
            _countdownSource.Play();
    }

    void StopCountdown()
    {
        if (_countdownSource != null)
            _countdownSource.Stop();
    }

    void PlaySfx(AudioClip clip)
    {
        if (clip == null || _sfxSource == null)
            return;

        _sfxSource.PlayOneShot(clip);
    }

    void ApplySourceVolumes()
    {
        if (_bgmSource != null)
            _bgmSource.volume = _bgmVolume;

        if (_countdownSource != null)
            _countdownSource.volume = _countdownVolume;

        if (_sfxSource != null)
            _sfxSource.volume = _sfxVolume;
    }
}
