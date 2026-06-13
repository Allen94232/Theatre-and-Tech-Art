using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─── Data classes ────────────────────────────────────────────────────────────

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
    [Tooltip("Optional: wall-side Renderer whose mainTexture will be set to each level's coloured reference image")]
    [SerializeField] private Renderer _wallRenderer;

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

    [Tooltip("Seconds to hold the fully revealed image before advancing to the next level")]
    public float levelCompletionPause = 2f;

    [Header("UI")]
    [Tooltip("TextMeshPro text on the wall screen — shows restoration percentage during Game 2")]
    [SerializeField] private TMP_Text _completionText;

    [Header("Floor Effects")]
    [Tooltip("Full-screen Image on the Floor Canvas for the completion flash. Set Color Alpha to 0 in editor.")]
    [SerializeField] private Image _floorFlashOverlay;

    // ── Runtime state ──
    private bool _gameRunning       = false;
    private int  _currentLevelIndex = 0;
    private bool _levelCompleting   = false;

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

        if (_completionText    != null) _completionText.text    = string.Empty;
        if (_floorFlashOverlay != null) _floorFlashOverlay.color = new Color(0f, 0f, 0f, 0f);
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

        if (_floorRenderer != null)
            _floorRenderer.Initialize(level);

        if (_wallRenderer != null && level.coloredImage != null)
            _wallRenderer.material.mainTexture = level.coloredImage;

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

        if (_floorRenderer != null)
            _floorRenderer.ShowCompleted();

        StartCoroutine(FloorCompletionFlashCo());
        _audioManager?.Game2_OnLevelComplete();
        Debug.Log($"[Game2] Level {_currentLevelIndex + 1} complete. Waiting {levelCompletionPause}s.");
        yield return new WaitForSeconds(levelCompletionPause);

        _currentLevelIndex++;

        if (_currentLevelIndex < levels.Count)
        {
            _levelCompleting = false;
            LoadLevel(_currentLevelIndex);
        }
        else
        {
            _audioManager?.Game2_OnAllComplete();
            EndGame();
        }
    }

    // ── Completion UI ──────────────────────────────────────────────────────

    private void UpdateCompletionUI()
    {
        if (_completionText == null) return;
        float ratio = _floorRenderer != null ? _floorRenderer.CompletionRatio : 0f;
        int pct = Mathf.RoundToInt(ratio * 100f);
        _completionText.text = levels.Count > 1
            ? $"{_currentLevelIndex + 1} / {levels.Count}\n{pct}%"
            : $"{pct}%";
    }

    // ── Floor Effects ──────────────────────────────────────────────────────

    // Warm white burst when the city image is fully revealed.
    private IEnumerator FloorCompletionFlashCo()
    {
        if (_floorFlashOverlay == null) yield break;
        _floorFlashOverlay.color = new Color(1f, 0.97f, 0.8f, 0.9f);
        yield return new WaitForSeconds(0.06f);
        float elapsed  = 0f;
        float fadeTime = 1.0f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            Color c = _floorFlashOverlay.color;
            c.a = Mathf.Lerp(0.9f, 0f, elapsed / fadeTime);
            _floorFlashOverlay.color = c;
            yield return null;
        }
        _floorFlashOverlay.color = new Color(0f, 0f, 0f, 0f);
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
