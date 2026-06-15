using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("TD/Achroma/Game 1 Controller")]
public class AchromaGame1Controller : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TDTableReceiverBase _receiver;
    [SerializeField] private TDAchromaFlowManager _flowManager;
    [SerializeField] private AchromaAudioManager _audioManager;
    [Tooltip("Renderer of the floor image — bottles spawn within its bounds, not the full arena")]
    [SerializeField] private Renderer _floorImageRenderer;

    [Header("Bottle Settings")]
    [Tooltip("Seconds before an uncollected bottle disappears")]
    [SerializeField] private float _bottleLifetime = 6f;
    [Tooltip("Seconds between each bottle spawn attempt")]
    [SerializeField] private float _bottleSpawnInterval = 1.5f;
    [Tooltip("World-space size (width × height) of each bottle. Used for both the pickup rectangle and spawn edge margin.")]
    [SerializeField] private Vector2 _bottleWorldSize = new Vector2(0.25f, 0.65f);
    [Tooltip("Total bottles needed to complete the game")]
    [SerializeField] private int _totalBottlesRequired = 20;
    [Tooltip("Maximum simultaneous bottles on the floor")]
    [SerializeField] private int _maxActiveBottles = 8;
    [Tooltip("Minimum world-unit distance from any active player when spawning a bottle. Set to 0 to disable.")]
    [SerializeField] private float _playerExclusionRadius = 0.6f;

    [Header("Visual")]
    [Tooltip("Sprites for each bottle color: index 0=red, 1=blue, 2=green, 3=yellow. Leave empty to use a procedural circle.")]
    [SerializeField] private Sprite[] _bottleSprites = new Sprite[4];
    [Tooltip("Enable if the arena uses the XZ plane (3D floor) so bottles are rotated correctly")]
    [SerializeField] private bool _arenaIsXZPlane = false;
    [Tooltip("Sorting order for bottle sprites (set higher than floor, lower than player markers)")]
    [SerializeField] private int _bottleSortingOrder = 1;

    [Header("UI")]
    [SerializeField] private Slider   _progressSlider;
    [SerializeField] private TMP_Text _progressText;
    [Tooltip("Hue offset (0-1) for the rainbow progress colour. 0 = starts red, 0.33 = starts green, etc.")]
    [Range(0f, 1f)]
    [SerializeField] private float _progressHueOffset = 0f;

    [Header("Hint")]
    [SerializeField] private TMP_Text _hintText;

    // Final player colors (red, blue, green, yellow) — matches TDTableReceiverBase.PlayerColors
    private static readonly Color[] FinalPlayerColors =
    {
        new Color(236 / 255f,  91 / 255f,  91 / 255f),
        new Color( 81 / 255f, 172 / 255f, 255 / 255f),
        new Color( 92 / 255f, 220 / 255f, 137 / 255f),
        new Color(246 / 255f, 196 / 255f,  83 / 255f),
    };

    // TDAchromaFlowManager polls this to detect when the round ends.
    public bool IsRoundRunning => _gameRunning;

    private bool   _gameRunning;
    private int    _bottlesCollected;
    private float  _spawnTimer;
    private Sprite _bottleSprite;
    private Image  _sliderFillImage;

    private struct BottleData
    {
        public int            colorIndex;
        public Vector2        arenaPos;
        public float          expireTime;
        public GameObject     visual;
        public SpriteRenderer renderer;
    }

    private readonly List<BottleData> _activeBottles = new List<BottleData>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_receiver      == null) _receiver      = FindFirstObjectByType<TDTableReceiverBase>();
        if (_flowManager   == null) _flowManager   = FindFirstObjectByType<TDAchromaFlowManager>();
        if (_audioManager  == null) _audioManager  = FindFirstObjectByType<AchromaAudioManager>();
        _bottleSprite = BuildCircleSprite();
        if (_progressSlider != null && _progressSlider.fillRect != null)
            _sliderFillImage = _progressSlider.fillRect.GetComponent<Image>();
    }

    private void Update()
    {
        if (!_gameRunning) return;

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f && _activeBottles.Count < _maxActiveBottles)
        {
            _spawnTimer = _bottleSpawnInterval;
            SpawnBottle();
        }

        UpdateBottles();
        CheckPickups();
        UpdatePlayerColors();
        UpdateUI();
    }

    // ── Public API (called by TDAchromaFlowManager) ────────────────────────────

    public void StartGame(bool keepPlayers = true)
    {
        if (_gameRunning) return;
        _gameRunning      = true;
        _bottlesCollected = 0;
        _spawnTimer       = 0f;

        ClearAllBottles();

        // All players start fully desaturated (grayscale)
        for (int i = 0; i < 4; i++)
            _receiver?.SetPlayerMarkerSaturation(i, 0f);

        if (_progressSlider != null)
        {
            _progressSlider.minValue = 0;
            _progressSlider.maxValue = _totalBottlesRequired;
            _progressSlider.value    = 0;
        }

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined += HandlePlayerJoined;
            _receiver.OnPlayerLeft   += HandlePlayerLeft;
        }

        _audioManager?.Game1_OnGameStart();

        if (_hintText != null) _hintText.gameObject.SetActive(true);

        Debug.Log("[Game1] StartGame");
    }

    public void EndGame()
    {
        if (!_gameRunning) return;
        _gameRunning = false;

        ClearAllBottles();

        // Restore full saturation so players carry their natural sprite colours into Story 2
        for (int i = 0; i < 4; i++)
            _receiver?.SetPlayerMarkerSaturation(i, 1f);

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined -= HandlePlayerJoined;
            _receiver.OnPlayerLeft   -= HandlePlayerLeft;
        }

        _audioManager?.Game1_OnGameComplete();

        if (_hintText != null) _hintText.gameObject.SetActive(false);

        Debug.Log("[Game1] EndGame");
    }

    // ── Bottle spawning ────────────────────────────────────────────────────────

    private void SpawnBottle()
    {
        if (_receiver == null) return;

        // Use floor image bounds when available; fall back to full arena bounds.
        Vector2 spawnMin = Vector2.zero, spawnMax = Vector2.one;
        bool hasFloor = TryGetFloorImageBounds(out spawnMin, out spawnMax);
        if (!hasFloor && !_receiver.TryGetArenaBounds(out spawnMin, out spawnMax)) return;

        // Shrink spawn area so no bottle centre is placed where any part would stick outside the image.
        float halfW = _bottleWorldSize.x * 0.5f;
        float halfH = _bottleWorldSize.y * 0.5f;
        spawnMin += new Vector2(halfW, halfH);
        spawnMax -= new Vector2(halfW, halfH);
        if (spawnMin.x >= spawnMax.x || spawnMin.y >= spawnMax.y) return;

        int     colorIndex = Random.Range(0, 4);
        Vector2 arenaPos   = Vector2.zero;
        bool    posFound   = false;
        for (int attempt = 0; attempt < 15; attempt++)
        {
            Vector2 c = new Vector2(Random.Range(spawnMin.x, spawnMax.x), Random.Range(spawnMin.y, spawnMax.y));
            if (!IsTooCloseToAnyPlayer(c)) { arenaPos = c; posFound = true; break; }
        }
        if (!posFound) return;

        var go = new GameObject("Bottle_" + colorIndex);
        go.layer = LayerMask.NameToLayer("Floor");
        go.transform.SetParent(transform, false);

        if (_arenaIsXZPlane)
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var sr = go.AddComponent<SpriteRenderer>();
        Sprite s = (_bottleSprites != null && colorIndex < _bottleSprites.Length && _bottleSprites[colorIndex] != null)
            ? _bottleSprites[colorIndex]
            : _bottleSprite;
        sr.sprite       = s;
        sr.color        = FinalPlayerColors[colorIndex];
        sr.sortingOrder = _bottleSortingOrder;

        // Scale the sprite so it displays at exactly _bottleWorldSize in world units,
        // regardless of the sprite's imported pixels-per-unit or pixel dimensions.
        Vector2 natural = (s != null && s.pixelsPerUnit > 0f)
            ? new Vector2(s.rect.width / s.pixelsPerUnit, s.rect.height / s.pixelsPerUnit)
            : Vector2.one;
        go.transform.localScale = new Vector3(
            natural.x > 0.001f ? _bottleWorldSize.x / natural.x : _bottleWorldSize.x,
            natural.y > 0.001f ? _bottleWorldSize.y / natural.y : _bottleWorldSize.y,
            1f);

        go.transform.position = _receiver.ArenaToWorldPosition(arenaPos);

        _activeBottles.Add(new BottleData
        {
            colorIndex = colorIndex,
            arenaPos   = arenaPos,
            expireTime = Time.time + _bottleLifetime,
            visual     = go,
            renderer   = sr,
        });
    }

    // ── Per-frame updates ──────────────────────────────────────────────────────

    private void UpdateBottles()
    {
        for (int i = _activeBottles.Count - 1; i >= 0; i--)
        {
            var   b         = _activeBottles[i];
            float remaining = b.expireTime - Time.time;

            if (remaining <= 0f)
            {
                if (b.visual != null) Destroy(b.visual);
                _activeBottles.RemoveAt(i);
                continue;
            }

            // Fade alpha in the final second before despawn
            if (b.renderer != null)
            {
                Color c = b.renderer.color;
                c.a             = Mathf.Clamp01(remaining);
                b.renderer.color = c;
            }
        }
    }

    private void CheckPickups()
    {
        if (_receiver == null) return;

        for (int slot = 0; slot < 4; slot++)
        {
            if (!_receiver.IsPlayerActive(slot)) continue;

            bool hasSpriteBounds = _receiver.TryGetPlayerBounds(slot, out Rect playerBounds);
            if (!hasSpriteBounds)
            {
                // Fallback: player-center inside bottle rectangle
                if (!_receiver.TryGetPlayerInfo(slot, out Vector2 playerPos, out float _)) continue;
                float halfW = _bottleWorldSize.x * 0.5f;
                float halfH = _bottleWorldSize.y * 0.5f;
                for (int i = _activeBottles.Count - 1; i >= 0; i--)
                {
                    Vector2 diff = playerPos - _activeBottles[i].arenaPos;
                    if (Mathf.Abs(diff.x) <= halfW && Mathf.Abs(diff.y) <= halfH)
                    { CollectBottle(i); break; }
                }
                continue;
            }

            for (int i = _activeBottles.Count - 1; i >= 0; i--)
            {
                if (playerBounds.Overlaps(GetBottleRect(_activeBottles[i])))
                {
                    CollectBottle(i);
                    break; // one bottle per player per frame
                }
            }
        }
    }

    private void CollectBottle(int index)
    {
        var b = _activeBottles[index];
        if (b.visual != null) Destroy(b.visual);
        _activeBottles.RemoveAt(index);

        _bottlesCollected = Mathf.Min(_bottlesCollected + 1, _totalBottlesRequired);
        _audioManager?.Game1_OnBottleCollected();
        Debug.Log($"[Game1] Bottle collected {_bottlesCollected}/{_totalBottlesRequired}");

        if (_bottlesCollected >= _totalBottlesRequired)
            EndGame();
    }

    // Saturation lerps 0→1 as bottles are collected, gradually revealing each robot's natural colour.
    private void UpdatePlayerColors()
    {
        float t = Mathf.Clamp01((float)_bottlesCollected / Mathf.Max(1, _totalBottlesRequired));
        for (int i = 0; i < 4; i++)
            _receiver?.SetPlayerMarkerSaturation(i, t);
    }

    private void UpdateUI()
    {
        if (_progressSlider != null)
            _progressSlider.value = _bottlesCollected;

        if (_progressText != null)
            _progressText.text = $"{_bottlesCollected} / {_totalBottlesRequired}";

        {
            float ratio  = Mathf.Clamp01((float)_bottlesCollected / Mathf.Max(1, _totalBottlesRequired));
            float hue    = Mathf.Repeat(_progressHueOffset + ratio, 1f);
            Color gray   = new Color(0.6f, 0.6f, 0.6f, 1f);
            Color vivid  = Color.HSVToRGB(hue, 1f, 1f);
            Color tinted = Color.Lerp(gray, vivid, ratio);
            if (_progressText    != null) _progressText.color    = tinted;
            if (_sliderFillImage != null) _sliderFillImage.color = tinted;
            if (_hintText        != null) _hintText.color        = tinted;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private Rect GetBottleRect(BottleData b)
    {
        if (b.renderer != null)
        {
            var bounds = b.renderer.bounds;
            return _arenaIsXZPlane
                ? new Rect(bounds.min.x, bounds.min.z, bounds.size.x, bounds.size.z)
                : new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
        }
        float halfW = _bottleWorldSize.x * 0.5f;
        float halfH = _bottleWorldSize.y * 0.5f;
        return new Rect(b.arenaPos.x - halfW, b.arenaPos.y - halfH, _bottleWorldSize.x, _bottleWorldSize.y);
    }

    private bool TryGetFloorImageBounds(out Vector2 min, out Vector2 max)
    {
        if (_floorImageRenderer == null) { min = max = Vector2.zero; return false; }
        Bounds b = _floorImageRenderer.bounds;
        if (_arenaIsXZPlane)
        {
            min = new Vector2(b.min.x, b.min.z);
            max = new Vector2(b.max.x, b.max.z);
        }
        else
        {
            min = new Vector2(b.min.x, b.min.y);
            max = new Vector2(b.max.x, b.max.y);
        }
        return true;
    }

    private void ClearAllBottles()
    {
        foreach (var b in _activeBottles)
            if (b.visual != null) Destroy(b.visual);
        _activeBottles.Clear();
    }

    private bool IsTooCloseToAnyPlayer(Vector2 worldPos)
    {
        if (_receiver == null || _playerExclusionRadius <= 0f) return false;
        for (int slot = 0; slot < 4; slot++)
        {
            if (!_receiver.IsPlayerActive(slot)) continue;
            if (!_receiver.TryGetPlayerInfo(slot, out Vector2 playerPos, out float _)) continue;
            if (Vector2.Distance(worldPos, playerPos) < _playerExclusionRadius) return true;
        }
        return false;
    }

    private void HandlePlayerJoined(int slot)
    {
        float t = Mathf.Clamp01((float)_bottlesCollected / Mathf.Max(1, _totalBottlesRequired));
        _receiver?.SetPlayerMarkerSaturation(slot, t);
    }

    private void HandlePlayerLeft(int slot) { }

    private static Sprite BuildCircleSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode    = TextureWrapMode.Clamp,
            filterMode  = FilterMode.Bilinear,
        };
        float center = (size - 1) * 0.5f;
        float r      = center - 1f;
        var   pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
            byte alpha = dist <= r - 1f ? (byte)255
                : dist <= r + 1f ? (byte)Mathf.RoundToInt(255f * Mathf.InverseLerp(r + 1f, r - 1f, dist))
                : (byte)0;
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
