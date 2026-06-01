using UnityEngine;
using OscJack; // Requires OSCJack to be installed in the project

[AddComponentMenu("TD/TD Game Audio Manager (QLab)")]
public class TDGameAudioManagerQlab : MonoBehaviour
{
    public enum MusicState
    {
        None,
        PreStart,
        Countdown,
        Gameplay
    }

    [Header("QLab Network")]
    [SerializeField] string _qlabIP = "127.0.0.1";
    [SerializeField] int _port = 53000;

    [Header("Music Cues (String/Number)")]
    [SerializeField] string _preStartBgmCue = "PreStart";
    [SerializeField] string _gameplayBgmCue = "Gameplay";
    [SerializeField] string _countdownLoopCue = "Countdown";

    [Header("Event Cues (String/Number)")]
    [SerializeField] string _playerBecameActiveCue = "PlayerActive";
    [SerializeField] string _growPickupCue = "Grow";
    [SerializeField] string _paintBurstPickupCue = "PaintBurst";
    [SerializeField] string _cleanseBurstPickupCue = "CleanseBurst";

    MusicState _state = MusicState.None;
    OscClient _client;

    void Awake()
    {
        // Initialize the OSC Client instead of AudioSources
        _client = new OscClient(_qlabIP, _port);
    }

    void OnDestroy()
    {
        // Vital: Dispose of the client to free up the UDP port when destroyed
        _client?.Dispose();
    }

    public void EnterPreStart()
    {
        if (_state == MusicState.PreStart)
            return;

        _state = MusicState.PreStart;
        StopCue(_countdownLoopCue);
        StopCue(_gameplayBgmCue); // Safety stop
        StartCue(_preStartBgmCue);
    }

    public void EnterCountdown()
    {
        if (_state == MusicState.Countdown)
            return;

        _state = MusicState.Countdown;
        StopCue(_preStartBgmCue);
        StopCue(_gameplayBgmCue); // Safety stop
        StartCue(_countdownLoopCue);
    }

    public void EnterGameplay()
    {
        if (_state == MusicState.Gameplay)
            return;

        _state = MusicState.Gameplay;
        StopCue(_countdownLoopCue);
        StopCue(_preStartBgmCue); // Safety stop
        StartCue(_gameplayBgmCue);
    }

    public void PlayPlayerBecameActive()
    {
        StartCue(_playerBecameActiveCue);
    }

    public void PlayPickupSfx(TDPowerupKind kind)
    {
        switch (kind)
        {
            case TDPowerupKind.Grow:
                StartCue(_growPickupCue);
                break;
            case TDPowerupKind.PaintBurst:
                StartCue(_paintBurstPickupCue);
                break;
            case TDPowerupKind.CleanseBurst:
                StartCue(_cleanseBurstPickupCue);
                break;
        }
    }

    // --- OSC Helper Methods ---

    void StartCue(string cueNumber)
    {
        if (string.IsNullOrEmpty(cueNumber) || _client == null)
            return;
            
        // Tell QLab to start the specific cue number
        _client.Send($"/cue/{cueNumber}/start");
    }

    void StopCue(string cueNumber)
    {
        if (string.IsNullOrEmpty(cueNumber) || _client == null)
            return;
            
        // Use panic instead of stop so QLab handles fades properly if configured
        _client.Send($"/cue/{cueNumber}/panic");
    }
}