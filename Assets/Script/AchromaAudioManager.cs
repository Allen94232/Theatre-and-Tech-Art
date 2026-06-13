using UnityEngine;
using OscJack;

[AddComponentMenu("TD/Achroma/Achroma Audio Manager")]
public class AchromaAudioManager : MonoBehaviour
{
    public enum AudioMode { UnityNative, QLab }

    // ── Mode ──────────────────────────────────────────────────────────────────

    [Header("Mode")]
    [Tooltip("UnityNative = local AudioClips for testing. QLab = OSC to QLab for theatre.")]
    public AudioMode mode = AudioMode.UnityNative;
    [SerializeField] private bool _debugLog = true;

    // ── QLab Network ──────────────────────────────────────────────────────────

    [Header("QLab Network")]
    [SerializeField] private string _qlabIP   = "127.0.0.1";
    [SerializeField] private int    _qlabPort = 53000;

    // Cue IDs match the cue numbers in your QLab workspace.
    // Leave a field empty to silently skip that trigger.

    [Header("QLab – Game 1 Cues")]
    [SerializeField] private string _q1Bgm      = "G1_BGM";
    [SerializeField] private string _q1Collect  = "G1_Collect";
    [SerializeField] private string _q1Complete = "G1_Complete";

    [Header("QLab – Game 2 Cues")]
    [SerializeField] private string _q2Bgm      = "G2_BGM";
    [SerializeField] private string _q2Level    = "G2_LevelDone";
    [SerializeField] private string _q2Complete = "G2_Complete";

    [Header("QLab – Game 3 Cues")]
    [Tooltip("Boss entrance sting — one-shot at game start")]
    [SerializeField] private string _q3Enter      = "G3_BossEnter";
    [Tooltip("Looping BGM for the city-repair phase")]
    [SerializeField] private string _q3RepairBgm  = "G3_RepairBGM";
    [Tooltip("SFX when a crack appears on the floor")]
    [SerializeField] private string _q3Hole       = "G3_HoleSpawn";
    [Tooltip("SFX when a player repairs a hole")]
    [SerializeField] private string _q3Repair     = "G3_HoleRepair";
    [Tooltip("Warning sound during boss attack shadow phase")]
    [SerializeField] private string _q3WarnSfx    = "G3_AttackWarn";
    [Tooltip("Impact SFX when the boss attack fires")]
    [SerializeField] private string _q3Attack     = "G3_AttackFire";
    [Tooltip("SFX when a player is caught in a shadow")]
    [SerializeField] private string _q3PlayerHit  = "G3_PlayerHit";
    [Tooltip("Looping BGM for the counter-attack phase")]
    [SerializeField] private string _q3CounterBgm = "G3_CounterBGM";
    [Tooltip("Charge-up SFX when all players stand on their zones")]
    [SerializeField] private string _q3Charge     = "G3_BeamCharge";
    [Tooltip("Impact SFX when the boss is hit by the beam")]
    [SerializeField] private string _q3BossHit    = "G3_BossHit";
    [Tooltip("Victory fanfare when the boss is defeated")]
    [SerializeField] private string _q3Defeat     = "G3_BossDefeated";
    [Tooltip("SFX when the counter-attack timer runs out")]
    [SerializeField] private string _q3CtFail     = "G3_CounterFail";

    // ── Native Audio Sources ──────────────────────────────────────────────────

    [Header("Native – Sources")]
    [Tooltip("Looping BGM source. Auto-created if left empty.")]
    [SerializeField] private AudioSource _bgmSource;
    [Tooltip("One-shot SFX source. Auto-created if left empty.")]
    [SerializeField] private AudioSource _sfxSource;

    [Header("Native – Game 1 Clips")]
    [SerializeField] private AudioClip _n1Bgm;
    [SerializeField] private AudioClip _n1Collect;
    [SerializeField] private AudioClip _n1Complete;

    [Header("Native – Game 2 Clips")]
    [SerializeField] private AudioClip _n2Bgm;
    [SerializeField] private AudioClip _n2Level;
    [SerializeField] private AudioClip _n2Complete;

    [Header("Native – Game 3 Clips")]
    [SerializeField] private AudioClip _n3EnterSting;
    [SerializeField] private AudioClip _n3RepairBgm;
    [SerializeField] private AudioClip _n3HoleSpawn;
    [SerializeField] private AudioClip _n3HoleRepair;
    [SerializeField] private AudioClip _n3AttackWarn;
    [SerializeField] private AudioClip _n3AttackFire;
    [SerializeField] private AudioClip _n3PlayerHit;
    [SerializeField] private AudioClip _n3CounterBgm;
    [SerializeField] private AudioClip _n3BeamCharge;
    [SerializeField] private AudioClip _n3BossHit;
    [SerializeField] private AudioClip _n3BossDefeated;
    [SerializeField] private AudioClip _n3CounterFail;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private OscClient _client;

    // Tracks the active BGM state to suppress redundant transitions in both modes.
    private enum BgmState { None, Game1, Game2, Game3Repair, Game3Counter }
    private BgmState _bgmState = BgmState.None;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        EnsureSources();
        if (mode == AudioMode.QLab) InitQlab();
    }

    private void OnDestroy()
    {
        _client?.Dispose();
    }

    // Called by TDAchromaFlowManager before the first game starts.
    // useQLab=true selects QLab; false selects UnityNative.
    public void SetMode(bool useQLab)
    {
        mode = useQLab ? AudioMode.QLab : AudioMode.UnityNative;
        if (mode == AudioMode.QLab && _client == null) InitQlab();
        Log($"Mode → {mode}");
    }

    // ── Game 1 ────────────────────────────────────────────────────────────────

    public void Game1_OnGameStart()
    {
        if (_bgmState == BgmState.Game1) return;
        _bgmState = BgmState.Game1;
        NativeSwitchBgm(_n1Bgm);
        CueTrigger(_q1Bgm);
    }

    public void Game1_OnBottleCollected()
    {
        NativePlaySfx(_n1Collect);
        CueTrigger(_q1Collect);
    }

    public void Game1_OnGameComplete()
    {
        _bgmState = BgmState.None;
        NativeStopBgm();
        CueStop(_q1Bgm);
        NativePlaySfx(_n1Complete);
        CueTrigger(_q1Complete);
    }

    // ── Game 2 ────────────────────────────────────────────────────────────────

    public void Game2_OnGameStart()
    {
        if (_bgmState == BgmState.Game2) return;
        _bgmState = BgmState.Game2;
        NativeSwitchBgm(_n2Bgm);
        CueTrigger(_q2Bgm);
    }

    public void Game2_OnLevelComplete()
    {
        NativePlaySfx(_n2Level);
        CueTrigger(_q2Level);
    }

    public void Game2_OnAllComplete()
    {
        _bgmState = BgmState.None;
        NativeStopBgm();
        CueStop(_q2Bgm);
        NativePlaySfx(_n2Complete);
        CueTrigger(_q2Complete);
    }

    // ── Game 3 ────────────────────────────────────────────────────────────────

    public void Game3_OnBossEnter()
    {
        _bgmState = BgmState.Game3Repair;
        NativeSwitchBgm(_n3RepairBgm);
        CueTrigger(_q3Enter);
        NativePlaySfx(_n3EnterSting);  // entrance sting plays over the repair BGM
    }

    // Ensures repair-phase BGM is running. Safe to call multiple times.
    public void Game3_OnRepairPhase()
    {
        if (_bgmState == BgmState.Game3Repair) return;
        _bgmState = BgmState.Game3Repair;
        NativeSwitchBgm(_n3RepairBgm);
        CueStop(_q3CounterBgm);
        CueTrigger(_q3RepairBgm);
    }

    public void Game3_OnHoleSpawned()
    {
        NativePlaySfx(_n3HoleSpawn);
        CueTrigger(_q3Hole);
    }

    public void Game3_OnHoleRepaired()
    {
        NativePlaySfx(_n3HoleRepair);
        CueTrigger(_q3Repair);
    }

    public void Game3_OnAttackWarning()
    {
        NativePlaySfx(_n3AttackWarn);
        CueTrigger(_q3WarnSfx);
    }

    public void Game3_OnAttackFires()
    {
        NativePlaySfx(_n3AttackFire);
        CueTrigger(_q3Attack);
    }

    public void Game3_OnPlayerHit()
    {
        NativePlaySfx(_n3PlayerHit);
        CueTrigger(_q3PlayerHit);
    }

    public void Game3_OnCounterAttack()
    {
        if (_bgmState == BgmState.Game3Counter) return;
        _bgmState = BgmState.Game3Counter;
        NativeSwitchBgm(_n3CounterBgm);
        CueStop(_q3RepairBgm);
        CueTrigger(_q3CounterBgm);
    }

    public void Game3_OnBeamCharging()
    {
        NativePlaySfx(_n3BeamCharge);
        CueTrigger(_q3Charge);
    }

    public void Game3_OnBossHit()
    {
        NativePlaySfx(_n3BossHit);
        CueTrigger(_q3BossHit);
    }

    public void Game3_OnBossDefeated()
    {
        _bgmState = BgmState.None;
        NativeStopBgm();
        CueStop(_q3RepairBgm);
        CueStop(_q3CounterBgm);
        NativePlaySfx(_n3BossDefeated);
        CueTrigger(_q3Defeat);
    }

    public void Game3_OnCounterFailed()
    {
        NativePlaySfx(_n3CounterFail);
        CueTrigger(_q3CtFail);
        // BGM transition back to repair is handled by the subsequent EnterRepairPhase call.
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    // Stop all audio. Called by TDAchromaFlowManager when entering story states.
    public void StopAll()
    {
        _bgmState = BgmState.None;
        NativeStopBgm();
        if (_sfxSource != null) _sfxSource.Stop();
        QLabPanicAll();
    }

    // ── Native Helpers ────────────────────────────────────────────────────────

    private void EnsureSources()
    {
        if (_bgmSource == null)
        {
            _bgmSource            = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop       = true;
            _bgmSource.playOnAwake = false;
        }
        if (_sfxSource == null)
        {
            _sfxSource            = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop       = false;
            _sfxSource.playOnAwake = false;
        }
    }

    private void NativeSwitchBgm(AudioClip clip)
    {
        if (mode != AudioMode.UnityNative || _bgmSource == null) return;
        if (clip == null) { _bgmSource.Stop(); _bgmSource.clip = null; return; }
        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;
        _bgmSource.clip = clip;
        _bgmSource.Play();
    }

    private void NativeStopBgm()
    {
        if (mode != AudioMode.UnityNative || _bgmSource == null) return;
        _bgmSource.Stop();
    }

    private void NativePlaySfx(AudioClip clip)
    {
        if (mode != AudioMode.UnityNative || _sfxSource == null || clip == null) return;
        _sfxSource.PlayOneShot(clip);
    }

    // ── QLab Helpers ──────────────────────────────────────────────────────────

    private void InitQlab()
    {
        _client?.Dispose();
        _client = new OscClient(_qlabIP, _qlabPort);
        Log($"QLab client → {_qlabIP}:{_qlabPort}");
    }

    // Start a cue. QLab's internal setup handles looping, routing, and fades.
    private void CueTrigger(string cueId)
    {
        if (mode != AudioMode.QLab || string.IsNullOrEmpty(cueId) || _client == null) return;
        _client.Send($"/cue/{cueId}/start");
        Log($"QLab ▶ {cueId}");
    }

    // Fade-stop a cue via /panic. Use for looping BGM cues.
    private void CueStop(string cueId)
    {
        if (mode != AudioMode.QLab || string.IsNullOrEmpty(cueId) || _client == null) return;
        _client.Send($"/cue/{cueId}/panic");
        Log($"QLab ■ {cueId}");
    }

    // Panic (fade + stop) all running cues in the QLab workspace.
    private void QLabPanicAll()
    {
        if (mode != AudioMode.QLab || _client == null) return;
        _client.Send("/panicAll");
        Log("QLab ■ panicAll");
    }

    private void Log(string msg)
    {
        if (_debugLog) Debug.Log($"[AchromaAudio] {msg}");
    }
}
