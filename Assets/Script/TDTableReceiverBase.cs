using System;
using System.Collections.Generic;
using OscJack;
using UnityEngine;

[AddComponentMenu("TD/TD Table Receiver (Base)")]
public class TDTableReceiverBase : MonoBehaviour
{
    public enum CoordinateMode { Normalized01, ValueRange, WorldSpace }
    public enum ArenaPlane { XY, XZ }

    struct PacketPoint
    {
        public int Id;
        public float U;
        public float V;
    }

    [Header("OSC")]
    [SerializeField] OscConnection _connection;
    [SerializeField] string _oscAddress = "/table";
    [SerializeField] int _fallbackPort = 10000;

    [Header("Input Mapping")]
    [SerializeField] CoordinateMode _coordinateMode = CoordinateMode.Normalized01;
    [SerializeField] Vector2 _uRange = new Vector2(0, 1);
    [SerializeField] Vector2 _vRange = new Vector2(0, 1);
    [SerializeField] bool _flipV = true;

    [Header("Arena")]
    [SerializeField] ArenaPlane _arenaPlane = ArenaPlane.XY;
    [SerializeField] float _arenaWidth = 12f;
    [SerializeField] float _arenaHeight = 12f;
    [SerializeField] bool _useArenaBoundsForSize = true;
    [SerializeField] float _markerPlaneOffset = 0.01f;
    [SerializeField] float _playerMarkerRadius = 0.25f;
    [SerializeField] float _playerMarkerHeight = 0.08f;

    [Header("Player Visual")]
    [SerializeField] bool _enablePlayerOutline = true;
    [SerializeField] Color _playerOutlineColor = Color.black;
    [SerializeField] float _playerOutlineScale = 1.2f;
    [SerializeField] int _playerOutlineSortingOffset = -1;

    [Header("Output")]
    [SerializeField] bool _useDualDisplay = true;
    [SerializeField] bool _useRenderTextures = false;
    [SerializeField] Vector2Int _renderTextureSize = new Vector2Int(1920, 1080);
    [SerializeField] RenderTexture _wallOutput;
    [SerializeField] RenderTexture _floorOutput;

    [Header("Scene References")]
    [SerializeField] Renderer _arenaRenderer;
    [SerializeField] Transform[] _playerMarkers = new Transform[4];
    [SerializeField] Camera _wallCamera;
    [SerializeField] Camera _floorCamera;

    // Fired on the main thread when a player slot becomes active/inactive.
    public event Action<int> OnPlayerJoined;
    public event Action<int> OnPlayerLeft;

    readonly object _queueLock = new object();
    readonly List<PacketPoint> _queueFront = new List<PacketPoint>(16);
    readonly List<PacketPoint> _queueBack  = new List<PacketPoint>(16);
    bool _hasQueuedFrame;

    readonly bool[]    _playerActive    = new bool[4];
    readonly Vector2[] _playerPos       = new Vector2[4];
    readonly bool[]    _seenThisFrame   = new bool[4];
    readonly Vector2[] _pendingPosition = new Vector2[4];
    Sprite _runtimeMarkerSprite;

    float _markerPlaneValue;
    float _axisMinA, _axisMaxA, _axisMinB, _axisMaxB;

    int    _listeningPort;
    string _registeredAddress;
    bool   _isConfigured;

    static readonly Color32[] PlayerColors =
    {
        new Color32(236, 91,  91,  255),
        new Color32(81,  172, 255, 255),
        new Color32(92,  220, 137, 255),
        new Color32(246, 196, 83,  255)
    };

    public RenderTexture WallOutput   => _wallOutput;
    public RenderTexture FloorOutput  => _floorOutput;
    public bool          IsConfigured => _isConfigured;

    public bool TryGetArenaBounds(out Vector2 min, out Vector2 max)
    {
        if (!_isConfigured) { min = Vector2.zero; max = Vector2.one; return false; }
        min = new Vector2(_axisMinA, _axisMinB);
        max = new Vector2(_axisMaxA, _axisMaxB);
        return true;
    }

    public Vector3 ArenaToWorldPosition(Vector2 arenaPos) => MarkerWorldPosition(arenaPos);

    public bool TryGetPlayerInfo(int playerIndex, out Vector2 position, out float radius)
    {
        if (!IsValidPlayerIndex(playerIndex) || !_playerActive[playerIndex])
        {
            position = Vector2.zero;
            radius   = _playerMarkerRadius;
            return false;
        }
        position = _playerPos[playerIndex];
        radius   = _playerMarkerRadius;
        return true;
    }

    public bool IsPlayerActive(int playerIndex) =>
        IsValidPlayerIndex(playerIndex) && _playerActive[playerIndex];

    public void SetPlayerMarkerColor(int slot, Color color)
    {
        if (!IsValidPlayerIndex(slot) || _playerMarkers == null || slot >= _playerMarkers.Length || _playerMarkers[slot] == null) return;
        var sr = _playerMarkers[slot].GetComponent<SpriteRenderer>();
        if (sr != null) { sr.color = color; return; }
        var r = _playerMarkers[slot].GetComponent<Renderer>();
        if (r != null && r.material != null) r.material.color = color;
    }

    void Awake()
    {
        _isConfigured = ConfigureSceneReferences();
        if (!_isConfigured) { enabled = false; return; }
        ResetTracking();
    }

    void OnEnable()
    {
        if (!_isConfigured) return;
        RegisterOscCallback();
    }

    void OnDisable()
    {
        UnregisterOscCallback();
    }

    void Update()
    {
        if (!_isConfigured) return;

        // Press Y at runtime to toggle coordinate V-flip for setup/calibration.
        if (Input.GetKeyDown(KeyCode.Y))
        {
            _flipV = !_flipV;
            Debug.Log("[TDTableReceiverBase] Flip V: " + _flipV);
        }

        PumpIncomingData();
    }

    void OnValidate()
    {
        _arenaWidth         = Mathf.Max(1f, _arenaWidth);
        _arenaHeight        = Mathf.Max(1f, _arenaHeight);
        _playerMarkerRadius = Mathf.Max(0.05f, _playerMarkerRadius);
        _playerMarkerHeight = Mathf.Max(0.02f, _playerMarkerHeight);
        _playerOutlineColor.a = 1f;
        _playerOutlineScale = Mathf.Clamp(_playerOutlineScale, 1.01f, 3f);

        if (_playerMarkers == null || _playerMarkers.Length != 4)
            Array.Resize(ref _playerMarkers, 4);
    }

    // -------------------------------------------------------------------------
    // OSC
    // -------------------------------------------------------------------------

    void RegisterOscCallback()
    {
        var port = _connection != null ? _connection.port : _fallbackPort;
        if (port <= 0 || string.IsNullOrEmpty(_oscAddress)) return;
        OscMaster.GetSharedServer(port).MessageDispatcher.AddCallback(_oscAddress, OnOscTableData);
        _listeningPort      = port;
        _registeredAddress  = _oscAddress;
    }

    void UnregisterOscCallback()
    {
        if (_listeningPort <= 0 || string.IsNullOrEmpty(_registeredAddress)) return;
        OscMaster.GetSharedServer(_listeningPort).MessageDispatcher.RemoveCallback(_registeredAddress, OnOscTableData);
        _listeningPort     = 0;
        _registeredAddress = null;
    }

    void OnOscTableData(string address, OscDataHandle data)
    {
        var count = data.GetElementCount();
        lock (_queueLock)
        {
            // Write directly to _queueFront; _queueBack is main-thread-only and must never be
            // touched here, otherwise ProcessFrame's foreach would see a concurrent modification.
            _queueFront.Clear();
            for (var i = 0; i + 2 < count; i += 3)
            {
                _queueFront.Add(new PacketPoint
                {
                    Id = Mathf.RoundToInt(data.GetElementAsFloat(i)),
                    U  = data.GetElementAsFloat(i + 1),
                    V  = data.GetElementAsFloat(i + 2)
                });
            }
            _hasQueuedFrame = true;
        }
    }

    void PumpIncomingData()
    {
        _queueBack.Clear();
        lock (_queueLock)
        {
            if (!_hasQueuedFrame) return;
            _queueBack.AddRange(_queueFront);
            _queueFront.Clear();
            _hasQueuedFrame = false;
        }
        ProcessFrame(_queueBack);
    }

    // -------------------------------------------------------------------------
    // Player tracking
    // -------------------------------------------------------------------------

    void ProcessFrame(IReadOnlyList<PacketPoint> points)
    {
        for (var i = 0; i < _seenThisFrame.Length; i++) _seenThisFrame[i] = false;

        foreach (var point in points)
        {
            // Positive and negative IDs both indicate a live presence.
            // |1| -> slot 0, |2| -> slot 1, |3| -> slot 2, |4| -> slot 3.
            var slot = Mathf.Abs(point.Id) - 1;
            if (slot < 0 || slot >= _playerActive.Length) continue;

            _seenThisFrame[slot]    = true;
            _pendingPosition[slot]  = ClampToArena(MapToArena(point.U, point.V));
        }

        for (var slot = 0; slot < _playerActive.Length; slot++)
        {
            if (_seenThisFrame[slot])
            {
                if (!_playerActive[slot])
                    AssignPlayerToSlot(slot, _pendingPosition[slot]);
                else
                    _playerPos[slot] = _pendingPosition[slot];
            }
            else if (_playerActive[slot])
            {
                RemovePlayerFromSlot(slot);
            }
        }

        UpdatePlayerMarkers();
    }

    void AssignPlayerToSlot(int slot, Vector2 worldPos)
    {
        _playerActive[slot] = true;
        _playerPos[slot]    = worldPos;
        OnPlayerJoined?.Invoke(slot);
    }

    void RemovePlayerFromSlot(int slot)
    {
        _playerActive[slot] = false;

        if (_playerMarkers != null && slot < _playerMarkers.Length && _playerMarkers[slot] != null)
            _playerMarkers[slot].gameObject.SetActive(false);

        OnPlayerLeft?.Invoke(slot);
    }

    void ResetTracking()
    {
        lock (_queueLock)
        {
            _queueFront.Clear();
            _queueBack.Clear();
            _hasQueuedFrame = false;
        }

        for (var i = 0; i < 4; i++)
        {
            _playerActive[i] = false;
            if (_playerMarkers != null && i < _playerMarkers.Length && _playerMarkers[i] != null)
                _playerMarkers[i].gameObject.SetActive(false);
        }
    }

    // -------------------------------------------------------------------------
    // Visuals
    // -------------------------------------------------------------------------

    void UpdatePlayerMarkers()
    {
        for (var i = 0; i < _playerMarkers.Length; i++)
        {
            var marker = _playerMarkers[i];
            if (marker == null) continue;

            if (!_playerActive[i]) { marker.gameObject.SetActive(false); continue; }

            marker.gameObject.SetActive(true);
            marker.localScale = _arenaPlane == ArenaPlane.XY
                ? new Vector3(_playerMarkerRadius * 2f, _playerMarkerRadius * 2f, 1f)
                : new Vector3(_playerMarkerRadius * 2f, _playerMarkerHeight * 0.5f, _playerMarkerRadius * 2f);
            marker.position = MarkerWorldPosition(_playerPos[i]);
        }
    }

    Sprite GetRuntimeMarkerSprite()
    {
        if (_runtimeMarkerSprite != null) return _runtimeMarkerSprite;

        if (_playerMarkers != null)
        {
            foreach (var marker in _playerMarkers)
            {
                if (marker == null) continue;
                var sr = marker.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) { _runtimeMarkerSprite = sr.sprite; return _runtimeMarkerSprite; }
            }
        }

        _runtimeMarkerSprite = BuildFallbackCircleSprite();
        return _runtimeMarkerSprite;
    }

    Sprite BuildFallbackCircleSprite()
    {
        const int size   = 64;
        var tex    = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var center = (size - 1) * 0.5f;
        var radius = center - 1f;
        var pixels = new Color32[size * size];

        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
            byte alpha;
            if      (dist <= radius - 1f) alpha = 255;
            else if (dist <= radius + 1f) alpha = (byte)Mathf.RoundToInt(255f * Mathf.InverseLerp(radius + 1f, radius - 1f, dist));
            else                          alpha = 0;
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // -------------------------------------------------------------------------
    // Coordinate helpers
    // -------------------------------------------------------------------------

    Vector2 MapToArena(float u, float v)
    {
        if (_coordinateMode == CoordinateMode.WorldSpace) return new Vector2(u, v);

        if (_coordinateMode == CoordinateMode.ValueRange)
        {
            u = Mathf.InverseLerp(_uRange.x, _uRange.y, u);
            v = Mathf.InverseLerp(_vRange.x, _vRange.y, v);
        }

        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);
        if (_flipV) v = 1f - v;

        return new Vector2(Mathf.Lerp(_axisMinA, _axisMaxA, u), Mathf.Lerp(_axisMinB, _axisMaxB, v));
    }

    Vector2 ClampToArena(Vector2 pos) =>
        new Vector2(Mathf.Clamp(pos.x, _axisMinA, _axisMaxA), Mathf.Clamp(pos.y, _axisMinB, _axisMaxB));

    Vector3 MarkerWorldPosition(Vector2 mappedPos)
    {
        if (_arenaPlane == ArenaPlane.XY)
            return new Vector3(mappedPos.x, mappedPos.y, _markerPlaneValue);
        return new Vector3(mappedPos.x, _markerPlaneValue, mappedPos.y);
    }

    bool IsValidPlayerIndex(int i) => i >= 0 && i < _playerActive.Length;

    // -------------------------------------------------------------------------
    // Scene setup
    // -------------------------------------------------------------------------

    bool ConfigureSceneReferences()
    {
        var missing = new List<string>();
        if (_arenaRenderer == null) missing.Add("Arena Renderer");
        if (_wallCamera    == null) missing.Add("Wall Camera");
        if (_floorCamera   == null) missing.Add("Floor Camera");

        if (_playerMarkers == null || _playerMarkers.Length < 4)
        {
            missing.Add("4 Player Markers");
        }
        else
        {
            for (var i = 0; i < 4; i++)
                if (_playerMarkers[i] == null)
                    missing.Add("Player Marker " + (i + 1));
        }

        if (missing.Count > 0)
        {
            Debug.LogError("[TDTableReceiverBase] Missing scene references: " + string.Join(", ", missing));
            return false;
        }

        ConfigureArenaBounds();
        ConfigurePlayerMarkers();
        ConfigureCameras();
        return true;
    }

    void ConfigureArenaBounds()
    {
        var bounds = _arenaRenderer.bounds;

        if (_useArenaBoundsForSize)
        {
            _arenaWidth  = Mathf.Max(1f, _arenaPlane == ArenaPlane.XY ? bounds.size.x : bounds.size.x);
            _arenaHeight = Mathf.Max(1f, _arenaPlane == ArenaPlane.XY ? bounds.size.y : bounds.size.z);
        }

        if (_arenaPlane == ArenaPlane.XY)
        {
            _axisMinA = bounds.min.x; _axisMaxA = bounds.max.x;
            _axisMinB = bounds.min.y; _axisMaxB = bounds.max.y;
            _markerPlaneValue = bounds.center.z + _markerPlaneOffset;
        }
        else
        {
            _axisMinA = bounds.min.x; _axisMaxA = bounds.max.x;
            _axisMinB = bounds.min.z; _axisMaxB = bounds.max.z;
            _markerPlaneValue = bounds.max.y + _playerMarkerHeight * 0.5f + _markerPlaneOffset;
        }
    }

    void ConfigurePlayerMarkers()
    {
        for (var i = 0; i < 4; i++)
        {
            var marker = _playerMarkers[i];
            marker.gameObject.SetActive(false);
            marker.localScale = _arenaPlane == ArenaPlane.XY
                ? new Vector3(_playerMarkerRadius * 2f, _playerMarkerRadius * 2f, 1f)
                : new Vector3(_playerMarkerRadius * 2f, _playerMarkerHeight * 0.5f, _playerMarkerRadius * 2f);

            var sr = marker.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = PlayerColors[i];
                ConfigureSpriteOutline(marker, sr);
                continue;
            }

            var meshRenderer = marker.GetComponent<Renderer>();
            if (meshRenderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat    = meshRenderer.sharedMaterial != null
                    ? new Material(meshRenderer.sharedMaterial)
                    : new Material(shader);
                mat.color = PlayerColors[i];
                meshRenderer.sharedMaterial = mat;
            }
        }
    }

    void ConfigureSpriteOutline(Transform marker, SpriteRenderer markerRenderer)
    {
        const string outlineName = "__MarkerOutline";
        var outlineTransform = marker.Find(outlineName);

        if (!_enablePlayerOutline)
        {
            if (outlineTransform != null) outlineTransform.gameObject.SetActive(false);
            return;
        }

        SpriteRenderer outlineRenderer;
        if (outlineTransform == null)
        {
            var outlineGo = new GameObject(outlineName);
            outlineGo.layer = marker.gameObject.layer;
            outlineGo.transform.SetParent(marker, false);
            outlineGo.transform.localPosition = Vector3.zero;
            outlineGo.transform.localRotation = Quaternion.identity;
            outlineTransform = outlineGo.transform;
            outlineRenderer  = outlineGo.AddComponent<SpriteRenderer>();
        }
        else
        {
            outlineTransform.gameObject.layer = marker.gameObject.layer;
            outlineRenderer = outlineTransform.GetComponent<SpriteRenderer>()
                           ?? outlineTransform.gameObject.AddComponent<SpriteRenderer>();
        }

        outlineTransform.gameObject.SetActive(true);
        outlineTransform.localScale    = new Vector3(_playerOutlineScale, _playerOutlineScale, 1f);
        outlineRenderer.sprite         = markerRenderer.sprite;
        outlineRenderer.color          = _playerOutlineColor;
        outlineRenderer.sortingLayerID = markerRenderer.sortingLayerID;
        outlineRenderer.sortingOrder   = markerRenderer.sortingOrder + _playerOutlineSortingOffset;
        outlineRenderer.flipX          = markerRenderer.flipX;
        outlineRenderer.flipY          = markerRenderer.flipY;
        outlineRenderer.maskInteraction = markerRenderer.maskInteraction;
        outlineRenderer.drawMode       = markerRenderer.drawMode;
        outlineRenderer.size           = markerRenderer.size;
        outlineRenderer.sharedMaterial = markerRenderer.sharedMaterial;
    }

    void ConfigureCameras()
    {
        var listener = _floorCamera.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);

        if (_useDualDisplay)
        {
            if (_wallCamera.targetDisplay == _floorCamera.targetDisplay)
            {
                _wallCamera.targetDisplay  = 0;
                _floorCamera.targetDisplay = 1;
            }
            for (var i = 1; i < Display.displays.Length && i < 3; i++)
                Display.displays[i].Activate();
        }

        if (_useRenderTextures)
        {
            if (_wallOutput == null)
                _wallOutput  = new RenderTexture(_renderTextureSize.x, _renderTextureSize.y, 24) { name = "RT_Wall" };
            if (_floorOutput == null)
                _floorOutput = new RenderTexture(_renderTextureSize.x, _renderTextureSize.y, 24) { name = "RT_Floor" };

            _wallCamera.targetTexture  = _wallOutput;
            _floorCamera.targetTexture = _floorOutput;
        }
        // When _useRenderTextures is false, leave camera targetTextures as-is
        // so manually assigned RenderTextures (e.g. for NDI output) are preserved.
    }
}
