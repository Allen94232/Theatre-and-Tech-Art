using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("TD/Achroma/Game 3 Controller")]
public class AchromaGame3Controller : MonoBehaviour
{
    // ── References ───────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private TDTableReceiverBase  _receiver;
    [SerializeField] private TDAchromaFlowManager _flowManager;
    [SerializeField] private AchromaAudioManager  _audioManager;
    [SerializeField] private Game3FloorRenderer   _floorRenderer;
    [Tooltip("The colored city image (same as Game 2's wall reference). Displayed on the floor at game start.")]
    [SerializeField] private Texture2D            _coloredCityImage;
    [Tooltip("Optional Animator on the wall boss visual. Receives triggers: BossEnter, BossAttack, BossHit, BossDefeat.")]
    [SerializeField] private Animator             _bossAnimator;

    // ── Holes ────────────────────────────────────────────────────────────────
    [Header("Holes")]
    [SerializeField] private int   _maxHoles          = 5;
    [SerializeField] private float _holeSpawnInterval = 3f;
    [Tooltip("World-unit radius of each hole")]
    [SerializeField] private float _holeRadius        = 0.6f;
    [Tooltip("World-unit distance a player must stand from a hole center to trigger repair")]
    [SerializeField] private float _repairRadius      = 0.5f;
    [SerializeField] private float _energyPerRepair   = 20f;

    // ── Boss Attacks ─────────────────────────────────────────────────────────
    [Header("Boss Attacks")]
    [SerializeField] private float _attackInterval        = 8f;
    [SerializeField] private float _shadowWarningDuration = 2.5f;
    [SerializeField] private float _shadowRadius          = 1.2f;
    [SerializeField] private int   _shadowsPerAttack      = 2;
    [SerializeField] private float _energyDamagePerHit    = 15f;

    // ── Difficulty Scaling ────────────────────────────────────────────────────
    [Header("Difficulty Scaling (scales as boss loses HP)")]
    [Tooltip("Hole spawn interval (s) when boss is at 1 HP. Base value is _holeSpawnInterval above.")]
    [SerializeField] private float _holeSpawnIntervalMin  = 1.5f;
    [Tooltip("Max simultaneous holes when boss is at 1 HP.")]
    [SerializeField] private int   _maxHolesMax           = 8;
    [Tooltip("Attack interval (s) when boss is at 1 HP.")]
    [SerializeField] private float _attackIntervalMin     = 4f;
    [Tooltip("Number of shadows per attack when boss is at 1 HP.")]
    [SerializeField] private int   _shadowsPerAttackMax   = 4;
    [Tooltip("Shadow warning duration (s) when boss is at 1 HP.")]
    [SerializeField] private float _shadowWarningMin      = 1.2f;

    // ── Energy & Boss HP ─────────────────────────────────────────────────────
    [Header("Energy & Boss HP")]
    [SerializeField] private float _maxEnergy = 100f;
    [SerializeField] private int   _bossMaxHP = 3;

    // ── Counter-Attack ────────────────────────────────────────────────────────
    [Header("Counter-Attack")]
    [SerializeField] private float _counterAttackDuration = 10f;
    [SerializeField] private float _targetZoneRadius      = 0.8f;
    [SerializeField] private float _beamHoldDuration      = 2f;

    // ── UI ────────────────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private Slider   _energyBar;
    [SerializeField] private Slider   _bossHPBar;
    [SerializeField] private TMP_Text _phaseText;
    [SerializeField] private TMP_Text _counterTimerText;

    // ── Layer ─────────────────────────────────────────────────────────────────
    [Header("Layer")]
    [Tooltip("Layer for floor-only visuals (shadows, zones, rings). Must be in the floor camera's culling mask but NOT the wall camera's. Add a 'Floor' layer in Edit > Project Settings > Tags and Layers.")]
    [SerializeField] private string _floorLayerName = "Floor";

    // ── Debug ─────────────────────────────────────────────────────────────────
    [Header("Debug")]
    [Tooltip("Fill city energy to max instantly (enters counter-attack phase next frame).")]
    [SerializeField] private KeyCode _debugFillEnergyKey = KeyCode.F3;
    [Tooltip("Fire beam immediately while in counter-attack phase (simulates all players standing).")]
    [SerializeField] private KeyCode _debugFireBeamKey   = KeyCode.F4;

    // ── Wall Effects ──────────────────────────────────────────────────────────
    [Header("Wall Effects")]
    [Tooltip("Full-screen Image on the Wall Canvas used for color flashes and vignette overlays. Set its Color Alpha to 0 in the editor.")]
    [SerializeField] private Image  _wallFlashOverlay;
    [Tooltip("Wall camera for shake effects on boss hit and defeat. If null, shake is skipped.")]
    [SerializeField] private Camera _wallCamera;
    [SerializeField] [Range(0f, 0.3f)]  private float _shakeAmplitude = 0.06f;
    [SerializeField] [Range(0.5f, 5f)]  private float _vignetteSpeed  = 2.2f;
    [Tooltip("Alpha of the dark overlay that appears during the counter-attack phase to build tension.")]
    [SerializeField] [Range(0f, 0.8f)]  private float _counterDarkenAlpha = 0.45f;

    // ── Floor Beam Effects ────────────────────────────────────────────────────
    [Header("Floor Beam Effects")]
    [Tooltip("Floor camera for shake on beam impact. If null, shake is skipped.")]
    [SerializeField] private Camera _floorCamera;
    [SerializeField] [Range(0f, 0.3f)] private float _floorShakeAmplitude = 0.05f;
    [Tooltip("Full-screen Image on Floor Canvas for the energy charge sweep. " +
             "Image Type = Filled, Fill Method = Vertical, Fill Origin = Bottom. " +
             "Set Color to your desired energy color and Alpha to 0 in the editor.")]
    [SerializeField] private Image _floorEnergyOverlay;

    // ── Effect Timings ────────────────────────────────────────────────────────
    [Header("Effect Timings — Wall Flash")]
    [SerializeField] private float _enterFlashHold  = 0.15f;
    [SerializeField] private float _enterFlashFade  = 3.0f;
    [SerializeField] private float _attackFlashHold = 0.24f;
    [SerializeField] private float _attackFlashFade = 1.65f;
    [SerializeField] private float _hitFlashHold    = 0.18f;
    [SerializeField] private float _hitFlashFade    = 2.1f;
    [SerializeField] private float _defeatFlashHold = 0.75f;
    [SerializeField] private float _defeatFlashFade = 5.4f;

    [Header("Effect Timings — Wall Camera")]
    [SerializeField] private float _hitShakeDuration    = 1.65f;
    [SerializeField] private float _defeatShakeDuration = 3.0f;
    [SerializeField] private float _darkenRepairTime    = 2.4f;
    [SerializeField] private float _darkenCounterTime   = 3.0f;

    [Header("Effect Timings — Attack Shadow")]
    [SerializeField] private float _shadowHitHold  = 0.75f;
    [SerializeField] private float _shadowFadeTime = 1.2f;

    [Header("Effect Timings — Beam Sequence")]
    [SerializeField] private float _zoneFlashHold  = 0.45f;
    [SerializeField] private float _zoneExpandTime = 1.5f;
    [SerializeField] private float _defeatWaitTime = 4.5f;

    [Header("Effect Timings — Floor Beam")]
    [SerializeField] private float _orbConvergeTime = 1.2f;
    [SerializeField] private float _burstRingTime   = 1.5f;
    [SerializeField] private float _floorShakeTime  = 1.65f;
    [SerializeField] private float _energySweepTime = 2.1f;
    [SerializeField] private float _energyFlashTime = 0.45f;
    [SerializeField] private float _energyFadeTime  = 1.05f;

    // ── Runtime State ─────────────────────────────────────────────────────────
    public bool IsRoundRunning => _gameRunning;

    private bool  _gameRunning = false;
    private float _cityEnergy  = 0f;
    private int   _bossHP      = 0;

    private enum Phase { Repair, CounterAttack, BeamFiring }
    private Phase _phase;

    private float     _holeSpawnTimer        = 0f;
    private float     _attackTimer           = 0f;
    private float     _counterTimer          = 0f;
    private Coroutine _attackCoroutine       = null;
    private Coroutine _wallVignetteCoroutine = null;
    private Coroutine _wallDarkenCoroutine   = null;
    private Coroutine _wallShakeCoroutine    = null;
    private Coroutine _floorShakeCoroutine   = null;

    private Vector3 _floorCameraOrigin;
    private Vector3 _wallCameraOrigin;

    // Base difficulty values captured from inspector at game start
    private float _holeSpawnIntervalBase;
    private int   _maxHolesBase;
    private float _attackIntervalBase;
    private int   _shadowsPerAttackBase;
    private float _shadowWarningBase;

    // ── Data Classes ──────────────────────────────────────────────────────────

    private sealed class HoleData
    {
        public Vector2 arenaPos;
        public int     floorHoleId;
        public bool    repaired;
    }

    private sealed class ShadowData
    {
        public Vector2        arenaPos;
        public GameObject     fillGo;
        public SpriteRenderer fillSr;
        public GameObject     ringGo;
        public SpriteRenderer ringSr;
    }

    private sealed class ZoneData
    {
        public int            playerSlot;
        public Vector2        arenaPos;
        public GameObject     fillGo;
        public SpriteRenderer fillSr;
        public GameObject     ringGo;
        public SpriteRenderer ringSr;
        public bool           occupied;
    }

    private readonly List<HoleData>   _holes   = new List<HoleData>();
    private readonly List<ShadowData> _shadows = new List<ShadowData>();
    private readonly List<ZoneData>   _zones   = new List<ZoneData>();

    private static readonly Color[] PlayerColors =
    {
        new Color(236 / 255f,  91 / 255f,  91 / 255f),
        new Color( 81 / 255f, 172 / 255f, 255 / 255f),
        new Color( 92 / 255f, 220 / 255f, 137 / 255f),
        new Color(246 / 255f, 196 / 255f,  83 / 255f),
    };

    // Cached sprites shared across all instances this frame
    private static Sprite _circleSprite;
    private static Sprite _ringSprite;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_receiver      == null) _receiver      = FindFirstObjectByType<TDTableReceiverBase>();
        if (_flowManager   == null) _flowManager   = FindFirstObjectByType<TDAchromaFlowManager>();
        if (_audioManager  == null) _audioManager  = FindFirstObjectByType<AchromaAudioManager>();
        if (_floorRenderer == null) _floorRenderer = FindFirstObjectByType<Game3FloorRenderer>();

        if (_floorCamera != null) _floorCameraOrigin = _floorCamera.transform.localPosition;
        if (_wallCamera  != null) _wallCameraOrigin  = _wallCamera.transform.localPosition;
    }

    private void Update()
    {
        if (!_gameRunning) return;

        HandleDebugInputs();

        switch (_phase)
        {
            case Phase.Repair:        UpdateRepair();        break;
            case Phase.CounterAttack: UpdateCounterAttack(); break;
        }

        AnimateShadowRings();
        AnimateZoneRings();
        UpdateUI();
    }

    private void HandleDebugInputs()
    {
        if (Input.GetKeyDown(_debugFillEnergyKey) && _phase == Phase.Repair)
        {
            _cityEnergy = _maxEnergy;
            Debug.Log("[Game3 Debug] Energy filled — counter-attack phase begins next frame.");
        }

        if (Input.GetKeyDown(_debugFireBeamKey) && _phase == Phase.CounterAttack)
        {
            Debug.Log("[Game3 Debug] Force firing beam sequence.");
            StartCoroutine(BeamSequenceCo());
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void StartGame(bool keepPlayers = true)
    {
        if (_gameRunning) return;
        _gameRunning    = true;
        _cityEnergy     = 0f;
        _bossHP         = _bossMaxHP;
        _holeSpawnTimer = 0f;
        _attackTimer    = _attackInterval * 0.5f;

        // Capture base difficulty values so scaling can lerp from them
        _holeSpawnIntervalBase = _holeSpawnInterval;
        _maxHolesBase          = _maxHoles;
        _attackIntervalBase    = _attackInterval;
        _shadowsPerAttackBase  = _shadowsPerAttack;
        _shadowWarningBase     = _shadowWarningDuration;

        if (_floorRenderer != null)
            _floorRenderer.Initialize(_coloredCityImage);

        ClearAll();
        ClearWallEffects();
        EnterRepairPhase();

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined += HandlePlayerJoined;
            _receiver.OnPlayerLeft   += HandlePlayerLeft;
        }

        if (_bossAnimator != null) _bossAnimator.SetTrigger("BossEnter");
        _audioManager?.Game3_OnBossEnter();

        // Boss entrance: brief dramatic white flash as boss appears
        StartCoroutine(WallFlashCo(new Color(1f, 1f, 1f, 0.6f), _enterFlashHold, _enterFlashFade));

        UpdateUI();
        Debug.Log("[Game3] StartGame");
    }

    public void EndGame()
    {
        if (!_gameRunning) return;
        _gameRunning     = false;
        _attackCoroutine = null;
        StopAllCoroutines();
        ClearAll();
        ClearWallEffects();

        if (_floorRenderer != null) _floorRenderer.ClearAllHoles();

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined -= HandlePlayerJoined;
            _receiver.OnPlayerLeft   -= HandlePlayerLeft;
        }

        if (_phaseText        != null) _phaseText.text        = string.Empty;
        if (_counterTimerText != null) _counterTimerText.text = string.Empty;

        Debug.Log("[Game3] EndGame");
    }

    // ── Repair Phase ──────────────────────────────────────────────────────────

    private void EnterRepairPhase()
    {
        _phase       = Phase.Repair;
        _attackTimer = _attackInterval;

        if (_attackCoroutine != null) { StopCoroutine(_attackCoroutine); _attackCoroutine = null; }

        ClearZones();
        ClearShadows();

        // Fade dark overlay back to transparent when returning to repair
        if (_wallDarkenCoroutine != null) { StopCoroutine(_wallDarkenCoroutine); _wallDarkenCoroutine = null; }
        _wallDarkenCoroutine = StartCoroutine(WallDarkenCo(0f, _darkenRepairTime));

        _audioManager?.Game3_OnRepairPhase();
        if (_phaseText        != null) _phaseText.text        = "修補城市！";
        if (_counterTimerText != null) _counterTimerText.text = string.Empty;
    }

    private void UpdateRepair()
    {
        _holeSpawnTimer -= Time.deltaTime;
        if (_holeSpawnTimer <= 0f && _holes.Count < _maxHoles)
        {
            _holeSpawnTimer = _holeSpawnInterval;
            SpawnHole();
        }

        _attackTimer -= Time.deltaTime;
        if (_attackTimer <= 0f && _attackCoroutine == null)
        {
            _attackTimer     = _attackInterval;
            _attackCoroutine = StartCoroutine(BossAttackCo());
        }

        CheckRepairs();

        if (_cityEnergy >= _maxEnergy)
            EnterCounterAttackPhase();
    }

    private void SpawnHole()
    {
        if (_receiver == null || _floorRenderer == null) return;

        // Prefer floor renderer canvas bounds for spawn area
        Vector2 bMin, bMax;
        bool hasBounds = _floorRenderer.TryGetCanvasBounds(out bMin, out bMax)
                      || _receiver.TryGetArenaBounds(out bMin, out bMax);
        if (!hasBounds) return;

        bMin += new Vector2(_holeRadius, _holeRadius);
        bMax -= new Vector2(_holeRadius, _holeRadius);
        if (bMin.x >= bMax.x || bMin.y >= bMax.y) return;

        Vector2 arenaPos = new Vector2(
            Random.Range(bMin.x, bMax.x),
            Random.Range(bMin.y, bMax.y));

        float uvRadius = _floorRenderer.WorldRadiusToUV(_holeRadius);
        Vector2 uv     = _floorRenderer.WorldToUV(arenaPos);
        int   holeId   = _floorRenderer.SpawnHole(uv, uvRadius);
        _audioManager?.Game3_OnHoleSpawned();

        _holes.Add(new HoleData { arenaPos = arenaPos, floorHoleId = holeId });
    }

    private void CheckRepairs()
    {
        if (_receiver == null) return;

        for (int slot = 0; slot < 4; slot++)
        {
            if (!_receiver.IsPlayerActive(slot)) continue;
            if (!_receiver.TryGetPlayerInfo(slot, out Vector2 pos, out float _)) continue;

            for (int i = _holes.Count - 1; i >= 0; i--)
            {
                var h = _holes[i];
                if (h.repaired) continue;
                if (Vector2.Distance(pos, h.arenaPos) > _repairRadius) continue;

                h.repaired = true;
                if (_floorRenderer != null) _floorRenderer.RepairHole(h.floorHoleId);
                _cityEnergy = Mathf.Min(_cityEnergy + _energyPerRepair, _maxEnergy);
                _audioManager?.Game3_OnHoleRepaired();
                Debug.Log($"[Game3] Slot {slot} repaired hole. Energy: {_cityEnergy}/{_maxEnergy}");
            }
        }

        for (int i = _holes.Count - 1; i >= 0; i--)
            if (_holes[i].repaired) _holes.RemoveAt(i);
    }

    // ── Boss Attack ───────────────────────────────────────────────────────────

    private IEnumerator BossAttackCo()
    {
        if (_receiver == null || !_receiver.TryGetArenaBounds(out Vector2 bMin, out Vector2 bMax))
        {
            _attackCoroutine = null;
            yield break;
        }

        if (_bossAnimator != null) _bossAnimator.SetTrigger("BossAttack");

        // Start pulsing red vignette as warning
        if (_wallVignetteCoroutine != null) StopCoroutine(_wallVignetteCoroutine);
        _wallVignetteCoroutine = StartCoroutine(WallVignettePulseCo());
        _audioManager?.Game3_OnAttackWarning();

        Vector2 sMin = bMin + new Vector2(_shadowRadius, _shadowRadius);
        Vector2 sMax = bMax - new Vector2(_shadowRadius, _shadowRadius);

        for (int i = 0; i < _shadowsPerAttack; i++)
        {
            Vector2 pos = new Vector2(
                Random.Range(sMin.x, sMax.x),
                Random.Range(sMin.y, sMax.y));
            SpawnShadow(pos);
        }

        if (_phaseText != null) _phaseText.text = "危險！躲開陰影！";

        // Warning period: shadow rings pulse and vignette pulses on wall
        yield return new WaitForSeconds(_shadowWarningDuration);

        // Attack fires: stop vignette → burst red flash → check player positions
        StopWallVignette();
        StartCoroutine(WallFlashCo(new Color(1f, 0.05f, 0.05f, 0.75f), _attackFlashHold, _attackFlashFade));
        _audioManager?.Game3_OnAttackFires();

        foreach (var s in _shadows)
        {
            if (s.fillSr != null) s.fillSr.color = new Color(1f, 0.05f, 0f, 0.75f);
            if (s.ringSr != null) s.ringSr.color = new Color(1f, 0.3f, 0f, 1.0f);
        }

        if (_receiver != null)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                if (!_receiver.IsPlayerActive(slot)) continue;
                if (!_receiver.TryGetPlayerInfo(slot, out Vector2 pos, out float _)) continue;
                foreach (var s in _shadows)
                {
                    if (Vector2.Distance(pos, s.arenaPos) > _shadowRadius) continue;
                    _cityEnergy = Mathf.Max(0f, _cityEnergy - _energyDamagePerHit);
                    _audioManager?.Game3_OnPlayerHit();
                    Debug.Log($"[Game3] Player {slot} hit! Energy: {_cityEnergy}/{_maxEnergy}");
                    break;
                }
            }
        }

        // Brief red hold, then fade out
        yield return new WaitForSeconds(_shadowHitHold);
        float fadeTime = _shadowFadeTime;
        float elapsed  = 0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
            foreach (var s in _shadows)
            {
                if (s.fillSr != null) { Color c = s.fillSr.color; c.a = a * 0.75f; s.fillSr.color = c; }
                if (s.ringSr != null) { Color c = s.ringSr.color; c.a = a;          s.ringSr.color = c; }
            }
            yield return null;
        }

        ClearShadows();

        if (_phase == Phase.Repair && _phaseText != null)
            _phaseText.text = "修補城市！";

        _attackCoroutine = null;
    }

    // ── Counter-Attack Phase ──────────────────────────────────────────────────

    private void EnterCounterAttackPhase()
    {
        _phase        = Phase.CounterAttack;
        _counterTimer = _counterAttackDuration;

        if (_attackCoroutine != null) { StopCoroutine(_attackCoroutine); _attackCoroutine = null; }

        ClearShadows();
        ClearHoles();
        if (_floorRenderer != null) _floorRenderer.ClearAllHoles();
        SpawnTargetZones();

        // Darken the wall background to build tension during counter-attack
        if (_wallVignetteCoroutine != null) { StopCoroutine(_wallVignetteCoroutine); _wallVignetteCoroutine = null; }
        if (_wallDarkenCoroutine   != null) { StopCoroutine(_wallDarkenCoroutine);   _wallDarkenCoroutine   = null; }
        _wallDarkenCoroutine = StartCoroutine(WallDarkenCo(_counterDarkenAlpha, _darkenCounterTime));

        _audioManager?.Game3_OnCounterAttack();
        if (_phaseText        != null) _phaseText.text        = "站到你的顏色上！";
        if (_counterTimerText != null) _counterTimerText.text = string.Empty;

        Debug.Log("[Game3] Counter-attack phase");
    }

    private void SpawnTargetZones()
    {
        ClearZones();
        if (_receiver == null || !_receiver.TryGetArenaBounds(out Vector2 bMin, out Vector2 bMax)) return;

        Vector2 center = (bMin + bMax) * 0.5f;
        float   mx     = (bMax.x - bMin.x) * 0.25f;
        float   my     = (bMax.y - bMin.y) * 0.25f;

        Vector2[] positions =
        {
            center + new Vector2(-mx, -my),
            center + new Vector2( mx, -my),
            center + new Vector2(-mx,  my),
            center + new Vector2( mx,  my),
        };

        for (int slot = 0; slot < 4; slot++)
        {
            Color fillColor = PlayerColors[slot]; fillColor.a = 0.30f;
            Color ringColor = PlayerColors[slot]; ringColor.a = 0.85f;

            float d = _targetZoneRadius * 2f;
            var (fillGo, fillSr) = SpawnCircle($"Zone_Fill_{slot}", positions[slot], d,           fillColor, 2);
            var (ringGo, ringSr) = SpawnCircle($"Zone_Ring_{slot}", positions[slot], d * 1.25f,   ringColor, 3,
                useRingSprite: true);

            _zones.Add(new ZoneData
            {
                playerSlot = slot,
                arenaPos   = positions[slot],
                fillGo     = fillGo, fillSr = fillSr,
                ringGo     = ringGo, ringSr = ringSr,
            });
        }
    }

    private void UpdateCounterAttack()
    {
        _counterTimer -= Time.deltaTime;
        if (_counterTimerText != null)
            _counterTimerText.text = Mathf.CeilToInt(Mathf.Max(0f, _counterTimer)).ToString();

        bool allStanding = true;
        int  activeCount = 0;

        foreach (var z in _zones)
        {
            bool standing = false;
            if (_receiver != null && _receiver.IsPlayerActive(z.playerSlot) &&
                _receiver.TryGetPlayerInfo(z.playerSlot, out Vector2 pos, out float _))
            {
                standing = Vector2.Distance(pos, z.arenaPos) <= _targetZoneRadius;
                activeCount++;
            }
            z.occupied = standing;

            // Fill brightens when occupied
            if (z.fillSr != null)
            {
                Color c = PlayerColors[z.playerSlot];
                c.a = standing ? 0.60f : 0.25f;
                z.fillSr.color = c;
            }

            if (_receiver != null && _receiver.IsPlayerActive(z.playerSlot) && !standing)
                allStanding = false;
        }

        if (activeCount > 0 && allStanding)
        {
            StartCoroutine(BeamSequenceCo());
            return;
        }

        if (_counterTimer <= 0f)
        {
            Debug.Log("[Game3] Counter-attack timed out.");
            _audioManager?.Game3_OnCounterFailed();
            _cityEnergy = 0f;
            EnterRepairPhase();
        }
    }

    private IEnumerator BeamSequenceCo()
    {
        _phase = Phase.BeamFiring;
        if (_counterTimerText != null) _counterTimerText.text = string.Empty;

        // Capture zone data before zones are cleared, for floor beam effect
        var beamPositions = new Vector2[_zones.Count];
        var beamColors    = new Color[_zones.Count];
        for (int i = 0; i < _zones.Count; i++)
        {
            beamPositions[i] = _zones[i].arenaPos;
            beamColors[i]    = PlayerColors[_zones[i].playerSlot];
        }
        StartCoroutine(FloorBeamEffectsCo(beamPositions, beamColors));

        // All zone rings flash white → all-ready signal
        _audioManager?.Game3_OnBeamCharging();
        foreach (var z in _zones)
        {
            if (z.fillSr != null) z.fillSr.color = Color.white;
            if (z.ringSr != null) z.ringSr.color = Color.white;
        }
        yield return new WaitForSeconds(_zoneFlashHold);

        // Expand and fade zones outward
        float expandTime = _zoneExpandTime;
        float elapsed    = 0f;
        while (elapsed < expandTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / expandTime;
            float scale = Mathf.Lerp(1f, 2.5f, t);
            float alpha = Mathf.Lerp(1f, 0f, t);
            foreach (var z in _zones)
            {
                if (z.fillGo != null) z.fillGo.transform.localScale = Vector3.one * scale * _targetZoneRadius * 2f;
                if (z.ringGo != null) z.ringGo.transform.localScale = Vector3.one * scale * _targetZoneRadius * 2.5f;
                if (z.fillSr != null) { Color c = z.fillSr.color; c.a = alpha * 0.6f; z.fillSr.color = c; }
                if (z.ringSr != null) { Color c = z.ringSr.color; c.a = alpha;          z.ringSr.color = c; }
            }
            yield return null;
        }

        ClearZones();

        _bossHP--;
        ApplyDifficultyScaling();
        Debug.Log($"[Game3] Boss hit! HP: {_bossHP}/{_bossMaxHP}");

        if (_bossAnimator != null) _bossAnimator.SetTrigger("BossHit");
        if (_phaseText    != null) _phaseText.text = "能量衝擊！";

        _audioManager?.Game3_OnBossHit();

        // Boss hit: white flash + camera shake
        StartCoroutine(WallFlashCo(new Color(1f, 1f, 1f, 0.9f), _hitFlashHold, _hitFlashFade));
        if (_wallShakeCoroutine != null) StopCoroutine(_wallShakeCoroutine);
        _wallShakeCoroutine = StartCoroutine(ShakeWallCameraCo(_hitShakeDuration, _shakeAmplitude));

        // Clear the dark overlay during the impact moment
        if (_wallDarkenCoroutine != null) { StopCoroutine(_wallDarkenCoroutine); _wallDarkenCoroutine = null; }

        UpdateUI();

        yield return new WaitForSeconds(_beamHoldDuration);

        if (!_gameRunning) yield break;

        if (_bossHP <= 0)
        {
            if (_bossAnimator != null) _bossAnimator.SetTrigger("BossDefeat");
            if (_phaseText    != null) _phaseText.text = "魔王擊敗！";

            _audioManager?.Game3_OnBossDefeated();

            // Defeat: extended white flash + heavy shake
            StartCoroutine(WallFlashCo(new Color(1f, 1f, 1f, 1f), _defeatFlashHold, _defeatFlashFade));
            if (_wallShakeCoroutine != null) StopCoroutine(_wallShakeCoroutine);
            _wallShakeCoroutine = StartCoroutine(ShakeWallCameraCo(_defeatShakeDuration, _shakeAmplitude * 2.2f));

            yield return new WaitForSeconds(_defeatWaitTime);
            EndGame();
        }
        else
        {
            _cityEnergy = 0f;
            EnterRepairPhase();
        }
    }

    // ── Animated Visual Updates ────────────────────────────────────────────────

    private void AnimateShadowRings()
    {
        if (_shadows.Count == 0) return;
        float t = Time.time;
        foreach (var s in _shadows)
        {
            if (s.ringSr == null) continue;
            // Ring pulses in size and shifts between orange and deep red
            float pulse = 1f + 0.18f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2.8f));
            s.ringGo.transform.localScale = Vector3.one * _shadowRadius * 2f * pulse;

            float hue   = Mathf.Lerp(0f, 0.08f, (Mathf.Sin(t * 5f) + 1f) * 0.5f);
            float alpha = 0.6f + 0.35f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f));
            s.ringSr.color = Color.HSVToRGB(hue, 1f, 1f);
            Color c = s.ringSr.color; c.a = alpha; s.ringSr.color = c;
        }
    }

    private void AnimateZoneRings()
    {
        if (_zones.Count == 0 || _phase != Phase.CounterAttack) return;
        float t = Time.time;
        foreach (var z in _zones)
        {
            if (z.ringSr == null) continue;
            // Ring breathes slowly; contracts tighter when player is standing on it
            float breathe = z.occupied
                ? 1f + 0.05f * Mathf.Sin(t * Mathf.PI * 4f)   // tight rapid pulse when occupied
                : 1f + 0.10f * Mathf.Sin(t * Mathf.PI * 1.2f); // slow breathing when waiting
            z.ringGo.transform.localScale = Vector3.one * _targetZoneRadius * 2.5f * breathe;

            Color c = PlayerColors[z.playerSlot];
            c.a = z.occupied
                ? 0.95f + 0.05f * Mathf.Sin(t * Mathf.PI * 8f)  // bright, fast flash
                : 0.50f + 0.35f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 1.2f));
            z.ringSr.color = c;
        }
    }

    // ── UI ─────────────────────────────────────────────────────────────────────

    private void UpdateUI()
    {
        if (_energyBar != null)
        {
            _energyBar.minValue = 0f;
            _energyBar.maxValue = _maxEnergy;
            _energyBar.value    = _cityEnergy;
        }
        if (_bossHPBar != null)
        {
            _bossHPBar.minValue = 0;
            _bossHPBar.maxValue = _bossMaxHP;
            _bossHPBar.value    = _bossHP;
        }
    }

    // ── Wall Effects ──────────────────────────────────────────────────────────

    // Pulsing red overlay during boss attack warning.
    private IEnumerator WallVignettePulseCo()
    {
        if (_wallFlashOverlay == null) yield break;
        while (true)
        {
            float a = 0.18f + 0.14f * Mathf.Abs(Mathf.Sin(Time.time * _vignetteSpeed * Mathf.PI));
            _wallFlashOverlay.color = new Color(0.9f, 0.04f, 0.04f, a);
            yield return null;
        }
    }

    // Single color flash that fades out.
    private IEnumerator WallFlashCo(Color flashColor, float holdTime, float fadeTime)
    {
        if (_wallFlashOverlay == null) yield break;
        _wallFlashOverlay.color = flashColor;
        if (holdTime > 0f) yield return new WaitForSeconds(holdTime);
        float elapsed = 0f;
        float startA  = flashColor.a;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            Color c = _wallFlashOverlay.color;
            c.a = Mathf.Lerp(startA, 0f, elapsed / fadeTime);
            _wallFlashOverlay.color = c;
            yield return null;
        }
        _wallFlashOverlay.color = new Color(0f, 0f, 0f, 0f);
    }

    // Gradually transitions the overlay to a black darkening at targetAlpha.
    private IEnumerator WallDarkenCo(float targetAlpha, float duration)
    {
        if (_wallFlashOverlay == null) yield break;
        float startA  = _wallFlashOverlay.color.a;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(startA, targetAlpha, elapsed / duration);
            _wallFlashOverlay.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }
        _wallFlashOverlay.color = new Color(0f, 0f, 0f, targetAlpha);
        _wallDarkenCoroutine = null;
    }

    // Positional shake on the wall camera.
    private IEnumerator ShakeWallCameraCo(float duration, float magnitude)
    {
        if (_wallCamera == null) yield break;
        Vector3 origin  = _wallCamera.transform.localPosition;
        float   elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float decay = 1f - elapsed / duration;
            _wallCamera.transform.localPosition = origin + (Vector3)(Random.insideUnitCircle * magnitude * decay);
            yield return null;
        }
        _wallCamera.transform.localPosition = origin;
        _wallShakeCoroutine = null;
    }

    // Stops the vignette coroutine and fades out whatever red is currently on the overlay.
    private void StopWallVignette()
    {
        if (_wallVignetteCoroutine != null) { StopCoroutine(_wallVignetteCoroutine); _wallVignetteCoroutine = null; }
    }

    // Kills all wall and floor effect coroutines and clears overlays instantly.
    private void ClearWallEffects()
    {
        if (_wallVignetteCoroutine != null) { StopCoroutine(_wallVignetteCoroutine); _wallVignetteCoroutine = null; }
        if (_wallDarkenCoroutine   != null) { StopCoroutine(_wallDarkenCoroutine);   _wallDarkenCoroutine   = null; }
        if (_wallShakeCoroutine    != null) { StopCoroutine(_wallShakeCoroutine);     _wallShakeCoroutine    = null; }
        if (_floorShakeCoroutine   != null) { StopCoroutine(_floorShakeCoroutine);   _floorShakeCoroutine   = null; }
        if (_wallFlashOverlay  != null) _wallFlashOverlay.color = new Color(0f, 0f, 0f, 0f);
        if (_floorEnergyOverlay != null)
        {
            _floorEnergyOverlay.color      = new Color(_floorEnergyOverlay.color.r,
                                                        _floorEnergyOverlay.color.g,
                                                        _floorEnergyOverlay.color.b, 0f);
            _floorEnergyOverlay.fillAmount = 0f;
        }
        if (_floorCamera != null) _floorCamera.transform.localPosition = _floorCameraOrigin;
        if (_wallCamera  != null) _wallCamera.transform.localPosition  = _wallCameraOrigin;
    }

    // ── Visual Spawning ────────────────────────────────────────────────────────

    private void SpawnShadow(Vector2 arenaPos)
    {
        float d = _shadowRadius * 2f;
        var (fillGo, fillSr) = SpawnCircle("Shadow_Fill", arenaPos, d,           new Color(0.05f, 0f, 0.1f, 0.60f), 2);
        var (ringGo, ringSr) = SpawnCircle("Shadow_Ring", arenaPos, d * 1.20f,   new Color(1f, 0.2f, 0f,    0.80f), 3,
            useRingSprite: true);
        _shadows.Add(new ShadowData
        {
            arenaPos = arenaPos,
            fillGo = fillGo, fillSr = fillSr,
            ringGo = ringGo, ringSr = ringSr,
        });
    }

    private (GameObject go, SpriteRenderer sr) SpawnCircle(
        string goName, Vector2 arenaPos, float worldDiameter,
        Color color, int sortOrder, bool useRingSprite = false)
    {
        var go = new GameObject(goName);
        int fl = LayerMask.NameToLayer(_floorLayerName);
        go.layer = fl >= 0 ? fl : gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.position = _receiver != null
            ? (Vector3)_receiver.ArenaToWorldPosition(arenaPos)
            : new Vector3(arenaPos.x, arenaPos.y, 0f);
        go.transform.localScale = new Vector3(worldDiameter, worldDiameter, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = useRingSprite ? GetRingSprite() : GetCircleSprite();
        sr.color        = color;
        sr.sortingOrder = sortOrder;
        return (go, sr);
    }

    private static Sprite GetCircleSprite()
    {
        if (_circleSprite != null) return _circleSprite;
        const int sz = 128;
        var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float ctr = (sz - 1) * 0.5f, r = ctr - 2f;
        var px = new Color32[sz * sz];
        for (int y = 0; y < sz; y++)
        for (int x = 0; x < sz; x++)
        {
            float d = Mathf.Sqrt((x - ctr) * (x - ctr) + (y - ctr) * (y - ctr));
            byte  a = d <= r - 1f ? (byte)255
                    : d <= r + 1f ? (byte)Mathf.RoundToInt(255f * Mathf.InverseLerp(r + 1f, r - 1f, d))
                    : (byte)0;
            px[y * sz + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _circleSprite = Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f), sz);
        return _circleSprite;
    }

    private static Sprite GetRingSprite()
    {
        if (_ringSprite != null) return _ringSprite;
        const int sz = 128;
        const float innerFraction = 0.68f;
        var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        float ctr    = (sz - 1) * 0.5f;
        float outerR = ctr - 2f;
        float innerR = outerR * innerFraction;
        float aaW    = 2.5f;
        var px = new Color32[sz * sz];
        for (int y = 0; y < sz; y++)
        for (int x = 0; x < sz; x++)
        {
            float d = Mathf.Sqrt((x - ctr) * (x - ctr) + (y - ctr) * (y - ctr));
            byte a;
            if      (d < innerR - aaW || d > outerR + aaW)  a = 0;
            else if (d >= innerR && d <= outerR)              a = 255;
            else if (d < innerR)  a = (byte)Mathf.RoundToInt(255f * Mathf.InverseLerp(innerR - aaW, innerR, d));
            else                  a = (byte)Mathf.RoundToInt(255f * Mathf.InverseLerp(outerR + aaW, outerR, d));
            px[y * sz + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        _ringSprite = Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f), sz);
        return _ringSprite;
    }

    // ── Floor Beam Effects ─────────────────────────────────────────────────────

    // Sequence: orbs converge from zone corners → burst ring at center
    //           → floor shake → energy sweeps bottom-to-top → flash to white → fade out
    private IEnumerator FloorBeamEffectsCo(Vector2[] positions, Color[] colors)
    {
        if (_receiver == null || !_receiver.TryGetArenaBounds(out Vector2 bMin, out Vector2 bMax))
            yield break;

        Vector2 arenaCenter = (bMin + bMax) * 0.5f;
        float   arenaWidth  = bMax.x - bMin.x;

        // Sync: wait for the same duration that BeamSequenceCo uses for the zone flash
        yield return new WaitForSeconds(_zoneFlashHold);

        // ── Phase 1: Orbs converge from zone corners to center ────────────────
        var orbGos = new GameObject[positions.Length];
        var orbSrs = new SpriteRenderer[positions.Length];
        var startPositions = new Vector3[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            Color c = colors[i]; c.a = 0.85f;
            var (go, sr) = SpawnCircle($"BeamOrb_{i}", positions[i], 0.45f, c, 6);
            orbGos[i]        = go;
            orbSrs[i]        = sr;
            startPositions[i] = go != null ? go.transform.position : Vector3.zero;
        }

        Vector3 worldCenter = (Vector3)_receiver.ArenaToWorldPosition(arenaCenter);

        float elapsed = 0f;
        while (elapsed < _orbConvergeTime)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.SmoothStep(0f, 1f, elapsed / _orbConvergeTime);
            float scale = Mathf.Lerp(1f, 0.25f, t);
            for (int i = 0; i < orbGos.Length; i++)
            {
                if (orbGos[i] == null) continue;
                orbGos[i].transform.position   = Vector3.Lerp(startPositions[i], worldCenter, t);
                orbGos[i].transform.localScale  = Vector3.one * 0.45f * scale;
            }
            yield return null;
        }

        for (int i = 0; i < orbGos.Length; i++)
            if (orbGos[i] != null) Destroy(orbGos[i]);

        // ── Phase 2: Burst ring expands (gray) — desaturates floor pixels as it passes ─
        var (burstGo, burstSr) = SpawnCircle("BeamBurst", arenaCenter, 0.3f,
            new Color(0.65f, 0.65f, 0.65f, 0.9f), 6, useRingSprite: true);

        Vector2 uvCenter   = _floorRenderer != null
            ? _floorRenderer.WorldToUV(arenaCenter)
            : new Vector2(0.5f, 0.5f);
        float prevUVRadius = 0f;

        elapsed = 0f;
        while (elapsed < _burstRingTime)
        {
            elapsed += Time.deltaTime;
            float t         = elapsed / _burstRingTime;
            float worldDiam = Mathf.Lerp(0.3f, arenaWidth * 1.8f, t);
            if (burstGo != null) burstGo.transform.localScale = Vector3.one * worldDiam;
            if (burstSr != null) { Color c = burstSr.color; c.a = Mathf.Lerp(0.9f, 0f, t); burstSr.color = c; }

            if (_floorRenderer != null)
            {
                float curUVRadius = _floorRenderer.WorldRadiusToUV(worldDiam * 0.5f);
                _floorRenderer.DesaturateRingBand(uvCenter, prevUVRadius, curUVRadius);
                prevUVRadius = curUVRadius;
            }
            yield return null;
        }
        if (burstGo != null) Destroy(burstGo);
        if (_floorRenderer != null) _floorRenderer.DesaturateAll();

        // ── Phase 3: Floor camera shake (0.55s, parallel with energy sweep) ──
        if (_floorShakeCoroutine != null) StopCoroutine(_floorShakeCoroutine);
        _floorShakeCoroutine = StartCoroutine(ShakeFloorCameraCo(_floorShakeTime, _floorShakeAmplitude));

        // ── Phase 4: Color floods back bottom-to-top directly in floor pixels ──
        if (_floorRenderer != null)
        {
            int res     = _floorRenderer.resolution;
            int prevRow = -1;
            elapsed     = 0f;
            while (elapsed < _energySweepTime)
            {
                elapsed += Time.deltaTime;
                int curRow = Mathf.Clamp(
                    Mathf.RoundToInt((elapsed / _energySweepTime) * (res - 1)), 0, res - 1);
                if (curRow > prevRow)
                {
                    _floorRenderer.RestoreColorRows(prevRow + 1, curRow);
                    prevRow = curRow;
                }
                yield return null;
            }
            if (prevRow < res - 1)
                _floorRenderer.RestoreColorRows(prevRow + 1, res - 1);
        }

        // ── Phase 5: White flash over floor ─────────────────────────────────
        if (_floorEnergyOverlay != null)
        {
            _floorEnergyOverlay.color      = new Color(1f, 1f, 1f, 0.75f);
            _floorEnergyOverlay.fillAmount = 1f;

            elapsed = 0f;
            while (elapsed < _energyFlashTime)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // ── Phase 6: Fade out ────────────────────────────────────────────
            elapsed = 0f;
            while (elapsed < _energyFadeTime)
            {
                elapsed += Time.deltaTime;
                Color c = _floorEnergyOverlay.color;
                c.a = Mathf.Lerp(0.75f, 0f, elapsed / _energyFadeTime);
                _floorEnergyOverlay.color = c;
                yield return null;
            }

            _floorEnergyOverlay.color      = new Color(1f, 1f, 1f, 0f);
            _floorEnergyOverlay.fillAmount = 0f;
        }
    }

    private IEnumerator ShakeFloorCameraCo(float duration, float magnitude)
    {
        if (_floorCamera == null) yield break;
        Vector3 origin  = _floorCamera.transform.localPosition;
        float   elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float decay = 1f - elapsed / duration;
            _floorCamera.transform.localPosition = origin + (Vector3)(Random.insideUnitCircle * magnitude * decay);
            yield return null;
        }
        _floorCamera.transform.localPosition = origin;
        _floorShakeCoroutine = null;
    }

    // ── Difficulty Scaling ─────────────────────────────────────────────────────

    // t = 0 at full HP (base difficulty), t = 1 at 1 HP (hardest).
    // Called right after _bossHP-- so the next repair phase is harder.
    private void ApplyDifficultyScaling()
    {
        if (_bossMaxHP <= 1) return;
        float t = Mathf.Clamp01(1f - ((float)(_bossHP - 1) / (_bossMaxHP - 1)));

        _holeSpawnInterval     = Mathf.Lerp(_holeSpawnIntervalBase, _holeSpawnIntervalMin, t);
        _maxHoles              = Mathf.RoundToInt(Mathf.Lerp(_maxHolesBase,          (float)_maxHolesMax,        t));
        _attackInterval        = Mathf.Lerp(_attackIntervalBase,    _attackIntervalMin,    t);
        _shadowsPerAttack      = Mathf.RoundToInt(Mathf.Lerp(_shadowsPerAttackBase, (float)_shadowsPerAttackMax, t));
        _shadowWarningDuration = Mathf.Lerp(_shadowWarningBase,     _shadowWarningMin,     t);

        Debug.Log($"[Game3] Difficulty t={t:F2} | holeInterval={_holeSpawnInterval:F1}s " +
                  $"maxHoles={_maxHoles} attackInterval={_attackInterval:F1}s " +
                  $"shadows={_shadowsPerAttack} warning={_shadowWarningDuration:F1}s");
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    private void ClearAll() { ClearHoles(); ClearShadows(); ClearZones(); }

    private void ClearHoles()
    {
        _holes.Clear();
    }

    private void ClearShadows()
    {
        foreach (var s in _shadows)
        {
            if (s.fillGo != null) Destroy(s.fillGo);
            if (s.ringGo != null) Destroy(s.ringGo);
        }
        _shadows.Clear();
    }

    private void ClearZones()
    {
        foreach (var z in _zones)
        {
            if (z.fillGo != null) Destroy(z.fillGo);
            if (z.ringGo != null) Destroy(z.ringGo);
        }
        _zones.Clear();
    }

    private void HandlePlayerJoined(int slot) { }
    private void HandlePlayerLeft(int slot)   { }
}
