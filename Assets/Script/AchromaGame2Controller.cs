using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─── Data classes ────────────────────────────────────────────────────────────

[System.Serializable]
public struct PercentColorThreshold
{
    [Tooltip("Apply this color when completion ratio is >= this value")]
    [Range(0f, 1f)] public float minRatio;
    public Color color;
}

[System.Serializable]
public class AchromaGame2Region
{
    [Tooltip("Display name shown in the Scene view label and Inspector")]
    public string label = "Region";

    [Tooltip("Player slot (0-3) that is allowed to paint this region")]
    [Range(0, 3)] public int playerSlot = 0;

    [Tooltip("Visualisation colour in Scene view — not visible in game")]
    public Color editorColor = Color.red;

    [Tooltip("Region boundary in UV space [0,1]^2. Edit by dragging handles in the Scene view.")]
    public List<Vector2> uvVertices = new List<Vector2>();
}

[System.Serializable]
public class AchromaGame2Level
{
    [Tooltip("Black and white city image shown on the floor at the start of this level")]
    public Texture2D grayscaleImage;

    [Tooltip("Fully coloured city image — shown on the wall as reference and revealed on floor at completion")]
    public Texture2D coloredImage;

    [Tooltip("Color for the level indicator text (e.g. 1/3) while this level is active")]
    public Color levelTextColor = Color.white;

    [Tooltip("One entry per colour region. Define each region's shape by editing its UV polygon in the Scene view.")]
    public List<AchromaGame2Region> regions = new List<AchromaGame2Region>();
}

// ─── Game 2 Controller ───────────────────────────────────────────────────────

[AddComponentMenu("TD/Achroma/Game 2 Controller")]
public class AchromaGame2Controller : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TDTableReceiverBase    _receiver;
    [SerializeField] private TDAchromaFlowManager   _flowManager;
    [SerializeField] private AchromaAudioManager    _audioManager;
    [SerializeField] private ColoringFloorRenderer  _floorRenderer;
    [Tooltip("Optional: SpriteRenderer on the wall — its sprite is swapped to each level's coloured reference image")]
    [SerializeField] private SpriteRenderer _wallSpriteRenderer;

    // Read by the custom Editor to project UV polygons onto the floor in Scene view.
    public ColoringFloorRenderer FloorRenderer => _floorRenderer;

    [Header("Levels")]
    [Tooltip("One or two level pairs. If two are provided the player must complete both before Game 2 ends.")]
    public List<AchromaGame2Level> levels = new List<AchromaGame2Level>();

    [Header("Painting")]
    [Tooltip("Paint brush radius in world units")]
    public float paintRadius = 0.3f;

    [Tooltip("Fraction of total paintable area that counts as completion (0.9 = 90%)")]
    [Range(0.5f, 1f)] public float completionThreshold = 0.9f;

    [Tooltip("Seconds to hold the fully revealed colour image before the crossfade begins")]
    public float levelHoldDuration = 2f;
    [Tooltip("Duration in seconds for both the fade-out and fade-in between levels")]
    public float fadeDuration = 0.5f;

    [Header("UI")]
    [Tooltip("TextMeshPro text showing the restoration percentage (e.g. 90%)")]
    [SerializeField] private TMP_Text _completionText;
    [Tooltip("TextMeshPro text showing the current level indicator (e.g. 1/3). Hidden when there is only one level.")]
    [SerializeField] private TMP_Text _levelText;

    [Tooltip("Text colour at different completion ratios. Evaluated highest-minRatio-first; add entries in any order.")]
    public List<PercentColorThreshold> completionTextColors = new List<PercentColorThreshold>();

    [Header("Hint")]
    [SerializeField] private TMP_Text _hintText;

    [Header("Transition")]
    [Tooltip("SpriteRenderer on the floor background object — faded during level transitions")]
    [SerializeField] private SpriteRenderer _floorBgRenderer;

    // ── Runtime state ──
    private bool           _gameRunning        = false;
    private int            _currentLevelIndex  = 0;
    private bool           _levelCompleting    = false;
    private volatile bool  _preloadReady       = true;
    private SpriteRenderer _floorCanvasRenderer;

    // Per-player previous UV position for stroke interpolation (avoids gaps at fast movement speed).
    private readonly Vector2[] _prevPlayerUV    = new Vector2[4];
    private readonly bool[]    _hasPrevPlayerUV = new bool[4];

    // TDAchromaFlowManager polls this property to detect when the round ends.
    public bool IsRoundRunning => _gameRunning;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_receiver      == null) _receiver      = FindFirstObjectByType<TDTableReceiverBase>();
        if (_flowManager   == null) _flowManager   = FindFirstObjectByType<TDAchromaFlowManager>();
        if (_audioManager  == null) _audioManager  = FindFirstObjectByType<AchromaAudioManager>();
        if (_floorRenderer == null) _floorRenderer = FindFirstObjectByType<ColoringFloorRenderer>();
        if (_floorRenderer != null) _floorCanvasRenderer = _floorRenderer.GetComponent<SpriteRenderer>();
    }

    // Triggered when this GameObject becomes active (during ApplyTransition, while overlay is still black).
    // Kicks off async GPU preload so the pixel data is ready before StartGame() calls LoadLevel(0).
    // The FlowManager's blackHold gives enough time for the async readback to complete.
    private void OnEnable()
    {
        if (levels == null || levels.Count == 0 || _floorRenderer == null) return;
        _floorRenderer.PreloadNextLevel(levels[0]);
    }

    private void Update()
    {
        if (!_gameRunning || _levelCompleting) return;

        PaintActivePlayers();
        CheckLevelCompletion();
        UpdateCompletionUI();
    }

    // ── Public API (called by TDAchromaFlowManager via reflection) ─────────

    public void StartGame(bool keepPlayers = true)
    {
        if (_gameRunning) return;

        if (levels == null || levels.Count == 0)
        {
            Debug.LogError("[Game2] No levels assigned — cannot start.");
            return;
        }

        _gameRunning       = true;
        _currentLevelIndex = 0;
        _levelCompleting   = false;
        System.Array.Clear(_hasPrevPlayerUV, 0, _hasPrevPlayerUV.Length);
        if (_completionText != null) _completionText.text = string.Empty;

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined += HandlePlayerJoined;
            _receiver.OnPlayerLeft   += HandlePlayerLeft;
        }

        _audioManager?.Game2_OnGameStart();

        if (_hintText  != null) _hintText.gameObject.SetActive(true);
        if (_levelText != null) _levelText.gameObject.SetActive(levels.Count > 1);

        LoadLevel(_currentLevelIndex);
        Debug.Log("[Game2] StartGame");
    }

    public void EndGame()
    {
        if (!_gameRunning) return;
        _gameRunning = false;
        StopAllCoroutines();

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined -= HandlePlayerJoined;
            _receiver.OnPlayerLeft   -= HandlePlayerLeft;
        }

        if (_completionText != null) _completionText.text = string.Empty;
        if (_hintText       != null) _hintText.gameObject.SetActive(false);
        if (_levelText      != null) _levelText.gameObject.SetActive(false);

        Debug.Log("[Game2] EndGame");
    }

    // ── Painting ───────────────────────────────────────────────────────────

    private void PaintActivePlayers()
    {
        if (_receiver == null || _floorRenderer == null) return;

        // Derive UV radii from the floor renderer's CURRENT world-space bounds (not TDTableReceiver's cached bounds).
        // This ensures paint aligns with the displayed texture even if Initialize() changed the sprite/scale.
        // Separate X/Y radii compensate for non-square arenas so the brush is circular in world space.
        _floorRenderer.GetPaintRadii(paintRadius, out float uvRadiusX, out float uvRadiusY);

        for (int slot = 0; slot < 4; slot++)
        {
            if (!_receiver.IsPlayerActive(slot)) continue;
            if (!_receiver.TryGetPlayerInfo(slot, out Vector2 worldPos, out float _)) continue;

            // Skip players outside the canvas bounds — the arena is intentionally larger than the
            // canvas so players have a safe border zone. InverseLerp would clamp them to the
            // canvas edge and cause incorrect edge painting without this check.
            if (!_floorRenderer.IsInsideCanvas(worldPos))
            {
                // Reset stroke origin so re-entering the canvas doesn't draw a line from outside.
                _hasPrevPlayerUV[slot] = false;
                continue;
            }

            // Convert world position to UV using the floor renderer's own current bounds.
            Vector2 uv = _floorRenderer.WorldToUV(worldPos);

            if (_hasPrevPlayerUV[slot])
                _floorRenderer.PaintStroke(_prevPlayerUV[slot], uv, slot, uvRadiusX, uvRadiusY);
            else
                _floorRenderer.Paint(uv, slot, uvRadiusX, uvRadiusY);

            _prevPlayerUV[slot]    = uv;
            _hasPrevPlayerUV[slot] = true;
        }
    }

    // ── Level management ───────────────────────────────────────────────────

    private void LoadLevel(int index)
    {
        if (index >= levels.Count)
        {
            EndGame();
            return;
        }

        var level = levels[index];

        // Reset stroke origins — Initialize() replaces the sprite and may shift bounds.
        System.Array.Clear(_hasPrevPlayerUV, 0, _hasPrevPlayerUV.Length);

        // Uses the fast path (preloaded pixel data) if OnEnable's async preload completed in time;
        // falls back to the synchronous path otherwise. blackHold in the transition ensures preload is done.
        if (_floorRenderer != null)
            _floorRenderer.Initialize(level);

        if (_wallSpriteRenderer != null && level.coloredImage != null)
        {
            var tex = level.coloredImage;
            _wallSpriteRenderer.sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        Debug.Log($"[Game2] Level {index + 1}/{levels.Count} loaded.");
    }

    private void CheckLevelCompletion()
    {
        if (_floorRenderer == null || _levelCompleting) return;
        if (_floorRenderer.CompletionRatio >= completionThreshold)
            StartCoroutine(OnLevelComplete());
    }

    private IEnumerator OnLevelComplete()
    {
        _levelCompleting = true;

        // 1. Instantly reveal any remaining unpainted pixels
        if (_floorRenderer != null)
            _floorRenderer.ShowCompleted();

        _audioManager?.Game2_OnLevelComplete();
        Debug.Log($"[Game2] Level {_currentLevelIndex + 1} complete. Holding {levelHoldDuration}s then crossfading.");

        // 2. Kick off async GPU preload for the next level — returns immediately, no main-thread stall.
        //    The callback sets _preloadReady=true a few frames later when data arrives.
        int nextIndex = _currentLevelIndex + 1;
        _preloadReady = nextIndex >= levels.Count; // no next level → already "ready"
        if (nextIndex < levels.Count && _floorRenderer != null)
            _floorRenderer.PreloadNextLevel(levels[nextIndex], () => _preloadReady = true);

        // 3. Hold the fully revealed image; also wait until preload completes (usually finishes
        //    within 2-3 frames, well before levelHoldDuration expires).
        float holdEnd = Time.time + levelHoldDuration;
        yield return new WaitUntil(() => Time.time >= holdEnd && _preloadReady);

        _currentLevelIndex++;
        bool hasNextLevel = _currentLevelIndex < levels.Count;

        // 4. Fade out floor + wall together
        yield return StartCoroutine(FadeTransitionCo(1f, 0f));

        // 5. Swap content while invisible
        if (hasNextLevel)
        {
            LoadLevel(_currentLevelIndex);
            SetTransitionAlpha(0f); // re-enforce alpha=0 after sprite reassignment in LoadLevel

            // 6. Fade in the new level
            yield return StartCoroutine(FadeTransitionCo(0f, 1f));
            _levelCompleting = false;
        }
        else
        {
            _audioManager?.Game2_OnAllComplete();
            EndGame();
        }
    }

    private IEnumerator FadeTransitionCo(float from, float to)
    {
        float elapsed = 0f;
        float dur     = Mathf.Max(fadeDuration, 0.01f);
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Lerp(from, to, elapsed / dur);
            SetTransitionAlpha(a);
            yield return null;
        }
        SetTransitionAlpha(to);
    }

    private void SetTransitionAlpha(float a)
    {
        if (_floorCanvasRenderer != null)
        {
            Color c = _floorCanvasRenderer.color; c.a = a; _floorCanvasRenderer.color = c;
        }
        if (_floorBgRenderer != null)
        {
            Color c = _floorBgRenderer.color; c.a = a; _floorBgRenderer.color = c;
        }
        if (_wallSpriteRenderer != null)
        {
            Color c = _wallSpriteRenderer.color; c.a = a; _wallSpriteRenderer.color = c;
        }
    }

    // ── Completion UI ──────────────────────────────────────────────────────

    private void UpdateCompletionUI()
    {
        float ratio = _floorRenderer != null ? _floorRenderer.CompletionRatio : 0f;
        int   pct   = Mathf.RoundToInt(ratio * 100f);

        // Percentage text
        if (_completionText != null)
            _completionText.text = $"{pct}%";

        // Level indicator text with per-level color
        if (_levelText != null && levels.Count > 1 && _currentLevelIndex < levels.Count)
        {
            _levelText.text  = $"{_currentLevelIndex + 1} / {levels.Count}";
            _levelText.color = levels[_currentLevelIndex].levelTextColor;
        }

        // Completion color applied to both percentage and hint text
        if (completionTextColors != null && completionTextColors.Count > 0)
        {
            Color best      = completionTextColors[0].color;
            float bestRatio = -1f;
            foreach (var entry in completionTextColors)
            {
                if (ratio >= entry.minRatio && entry.minRatio >= bestRatio)
                {
                    bestRatio = entry.minRatio;
                    best      = entry.color;
                }
            }
            if (_completionText != null) _completionText.color = best;
            if (_hintText       != null) _hintText.color       = best;
        }
    }

    // ── Player events ──────────────────────────────────────────────────────

    private void HandlePlayerJoined(int slot) { }

    private void HandlePlayerLeft(int slot)
    {
        // Clear the stroke origin so re-entering this slot doesn't draw a line from the last known position.
        if (slot >= 0 && slot < _hasPrevPlayerUV.Length)
            _hasPrevPlayerUV[slot] = false;
    }
}
