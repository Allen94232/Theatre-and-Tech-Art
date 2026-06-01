using System.Collections.Generic;
using OscJack;
using TMPro;
using UnityEngine;

[AddComponentMenu("TD/TD Table Receiver")]
public class TDTableReceiver : MonoBehaviour
{
    public enum CoordinateMode
    {
        Normalized01,
        ValueRange,
        WorldSpace
    }

    public enum ArenaPlane
    {
        XY,
        XZ
    }

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
    [SerializeField] Color _canvasBackgroundColor = Color.white;
    [SerializeField] int _paintResolution = 512;
    [SerializeField] int _brushRadiusPixels = 8;
    [SerializeField] bool _matchBrushToMarkerSize = true;
    [SerializeField] float _brushRadiusScale = 1f;
    [SerializeField] float _playerMarkerRadius = 0.25f;
    [SerializeField] float _playerMarkerHeight = 0.08f;

    [Header("Player Visual")]
    [SerializeField] bool _enablePlayerOutline = true;
    [SerializeField] Color _playerOutlineColor = Color.black;
    [SerializeField] float _playerOutlineScale = 1.2f;
    [SerializeField] int _playerOutlineSortingOffset = -1;

    [Header("Game")]
    [SerializeField] float _gameDurationSeconds = 60f;
    [SerializeField] bool _autoStartOnPlay = true;

    [Header("Spawn and Collision")]
    [SerializeField] Transform[] _spawnPoints = new Transform[4];
    [SerializeField] float _spawnRadius = 0.8f;
    [SerializeField] float _collisionDistance = 1.0f;
    [SerializeField] float _readyDelaySeconds = 3f;

    [Header("Debug Markers")]
    [SerializeField] bool _showNonPlayerDebugMarkers = false;
    [SerializeField] float _nonPlayerMarkerRadius = 0.14f;
    [SerializeField] Color _nonPlayerMarkerColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);
    [SerializeField] int _maxNonPlayerDebugMarkers = 16;

    [Header("Spawn Hint")]
    [SerializeField] bool _showSpawnHints = true;
    [SerializeField] float _spawnHintRadius = 0.5f;
    [SerializeField] float _spawnHintCoreRadius = 0.14f;
    [SerializeField] Color _spawnHintCoreColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] float _spawnHintPulseSpeed = 2.5f;
    [SerializeField] float _spawnHintMinAlpha = 0.2f;
    [SerializeField] float _spawnHintMaxAlpha = 0.65f;
    [SerializeField] float _spawnHintActiveAlphaScale = 0.45f;
    [SerializeField] float _spawnHintActiveMinAlpha = 0.3f;
    [SerializeField, Range(0f, 1f)] float _spawnHintContrastBlend = 0.55f;
    [SerializeField] int _spawnHintSortingOffset = 4;
    [SerializeField] int _nonPlayerMarkerSortingOffset = -3;

    [Header("Score Display")]
    [SerializeField] bool _playerPercentUsesPaintedArea = true;

    [Header("Accuracy")]
    [SerializeField] bool _usePointFilterForPaintTexture = true;
    [SerializeField] bool _recountScoresFromOwnerMap = true;

    [Header("Debug Testing")]
    [SerializeField] bool _enableDebugFillHotkeys = true;
    [SerializeField] bool _autoAssignReceivedIdsAsPlayers = false;

    [Header("TD Stable ID Integration")]
    [SerializeField] bool _useTdSignedStableIds = true;

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
    [SerializeField] TMP_Text _timerText;
    [SerializeField] TMP_Text _scoreText;
    [SerializeField] TMP_Text _scoreTextP2;
    [SerializeField] TMP_Text _scoreTextP3;
    [SerializeField] TMP_Text _scoreTextP4;
    [SerializeField] TMP_Text _scoreHintText;
    [SerializeField] TMP_Text _winnerText;

    [Header("Managers")]
    [SerializeField] TDGameAudioManager _audioManager;

    readonly object _queueLock = new object();
    readonly List<PacketPoint> _queueFront = new List<PacketPoint>(16);
    readonly List<PacketPoint> _queueBack = new List<PacketPoint>(16);
    bool _hasQueuedFrame;

    readonly Dictionary<int, int> _rawIdToPlayerSlot = new Dictionary<int, int>();
    readonly Dictionary<int, Vector2> _frameRawPositions = new Dictionary<int, Vector2>(16);
    readonly List<int> _allFrameRawIds = new List<int>(16);
    readonly List<Vector2> _allFrameRawPos = new List<Vector2>(16);
    readonly int[] _playerRawIds = new int[4];
    readonly Vector2[] _spawnPos = new Vector2[4];
    readonly bool[] _killBuffer = new bool[4];
    readonly bool[] _missingThisFrame = new bool[4];
    readonly Vector2[] _missingLastPos = new Vector2[4];
    readonly bool[] _stableIdAliveThisFrame = new bool[4];
    readonly bool[] _stableIdDeadThisFrame = new bool[4];
    readonly Vector2[] _stableIdPosThisFrame = new Vector2[4];
    readonly bool[] _playerActive = new bool[4];
    readonly Vector2[] _playerPos = new Vector2[4];
    readonly Vector2[] _playerPrevPos = new Vector2[4];
    readonly bool[] _hasPrevPos = new bool[4];
    readonly float[] _playerRadiusMultiplier = new float[4];
    readonly float[] _playerGrowBuffUntil = new float[4];
    readonly int[] _scorePixels = new int[4];
    readonly List<Vector2> _nonPlayerDebugPosBuffer = new List<Vector2>(16);
    readonly List<Transform> _nonPlayerDebugMarkers = new List<Transform>(16);

    readonly Transform[] _spawnHintMarkers = new Transform[4];
    readonly Transform[] _spawnHintCoreMarkers = new Transform[4];
    Sprite _runtimeMarkerSprite;

    Texture2D _paintTexture;
    Color32[] _paintPixels;
    byte[] _ownerMap;
    bool _textureDirty;

    float _markerPlaneValue;
    float _axisMinA;
    float _axisMaxA;
    float _axisMinB;
    float _axisMaxB;
    float _pixelWorldSizeA;
    float _pixelWorldSizeB;
    int _brushRadiusPixelsA;
    int _brushRadiusPixelsB;

    int _listeningPort;
    string _registeredAddress;
    float _timeLeft;
    float _readyCountdown;
    bool _countdownAudioActive;
    bool _roundEndedAwaitRestart;
    bool _gameRunning;
    bool _isConfigured;

    static readonly Color32[] PlayerColors =
    {
        new Color32(236, 91, 91, 255),
        new Color32(81, 172, 255, 255),
        new Color32(92, 220, 137, 255),
        new Color32(246, 196, 83, 255)
    };

    public RenderTexture WallOutput => _wallOutput;
    public RenderTexture FloorOutput => _floorOutput;
    public bool IsRoundRunning => _gameRunning;
    public bool IsCountdownRunning => !_gameRunning && _countdownAudioActive;
    public bool IsConfigured => _isConfigured;

    public bool TryGetArenaBounds(out Vector2 min, out Vector2 max)
    {
        if (!_isConfigured)
        {
            min = Vector2.zero;
            max = Vector2.one;
            return false;
        }

        min = new Vector2(_axisMinA, _axisMinB);
        max = new Vector2(_axisMaxA, _axisMaxB);
        return true;
    }

    public Vector3 ArenaToWorldPosition(Vector2 arenaPos)
    {
        return MarkerWorldPosition(arenaPos);
    }

    public bool TryGetPlayerInfo(int playerIndex, out Vector2 position, out float radius)
    {
        if (!IsValidPlayerIndex(playerIndex) || !_playerActive[playerIndex])
        {
            position = Vector2.zero;
            radius = _playerMarkerRadius;
            return false;
        }

        position = _playerPos[playerIndex];
        radius = GetPlayerEffectiveRadius(playerIndex);
        return true;
    }

    public bool TryGetSpawnPoint(int spawnIndex, out Vector2 position)
    {
        if (spawnIndex < 0 || spawnIndex >= _spawnPos.Length)
        {
            position = Vector2.zero;
            return false;
        }

        position = _spawnPos[spawnIndex];
        return true;
    }

    public void ApplyGrowPowerup(int playerIndex, float radiusMultiplier, float durationSeconds)
    {
        if (!IsValidPlayerIndex(playerIndex) || !_playerActive[playerIndex])
            return;

        radiusMultiplier = Mathf.Max(1f, radiusMultiplier);
        durationSeconds = Mathf.Max(0.1f, durationSeconds);

        _playerRadiusMultiplier[playerIndex] = Mathf.Max(_playerRadiusMultiplier[playerIndex], radiusMultiplier);
        _playerGrowBuffUntil[playerIndex] = Mathf.Max(_playerGrowBuffUntil[playerIndex], Time.time + durationSeconds);
    }

    public void ApplyPaintBurstPowerup(int playerIndex, float radiusMultiplier)
    {
        if (!IsValidPlayerIndex(playerIndex) || !_playerActive[playerIndex])
            return;

        var worldRadius = GetPlayerEffectiveRadius(playerIndex) * Mathf.Max(0.1f, radiusMultiplier);
        PaintCircleWorld(_playerPos[playerIndex], worldRadius, playerIndex + 1);
    }

    public void ApplyCleanseBurstPowerup(int playerIndex, float radiusMultiplier)
    {
        if (!IsValidPlayerIndex(playerIndex) || !_playerActive[playerIndex])
            return;

        var worldRadius = GetPlayerEffectiveRadius(playerIndex) * Mathf.Max(0.1f, radiusMultiplier);
        ClearOtherPlayersInCircle(_playerPos[playerIndex], worldRadius, playerIndex + 1);
    }

    void Awake()
    {
        _isConfigured = ConfigureSceneReferences();
        if (!_isConfigured)
        {
            enabled = false;
            return;
        }

        ResetBoard();
    }

    void OnEnable()
    {
        if (!_isConfigured)
            return;

        RegisterOscCallback();
        if (_autoStartOnPlay)
            StartGame();
    }

    void OnDisable()
    {
        UnregisterOscCallback();
    }

    void Update()
    {
        if (!_isConfigured)
            return;

        if (Input.GetKeyDown(KeyCode.R))
            StartGame(_roundEndedAwaitRestart);

        if (_enableDebugFillHotkeys)
        {
            if (Input.GetKeyDown(KeyCode.A))
            {
                _autoAssignReceivedIdsAsPlayers = !_autoAssignReceivedIdsAsPlayers;
                Debug.Log("Auto assign received IDs: " + (_autoAssignReceivedIdsAsPlayers ? "ON" : "OFF"));
            }
            if (Input.GetKeyDown(KeyCode.Y))
            {
                _flipV = !_flipV;
                Debug.Log("Input V axis flip: " + (_flipV ? "ON" : "OFF"));
            }
            if (Input.GetKeyDown(KeyCode.F6)) DebugFillBoard(1);
            if (Input.GetKeyDown(KeyCode.F7)) DebugFillBoard(2);
            if (Input.GetKeyDown(KeyCode.F8)) DebugFillBoard(3);
            if (Input.GetKeyDown(KeyCode.F9)) DebugFillBoard(4);
            if (Input.GetKeyDown(KeyCode.F10)) DebugFillBoard(0);
        }

        PumpIncomingData();
        UpdateTimedPlayerBuffs();
        UpdateSpawnHintVisuals();

        if (_textureDirty && _recountScoresFromOwnerMap)
            RecountScoresFromOwnerMap();

        if (_textureDirty)
        {
            _paintTexture.SetPixels32(_paintPixels);
            _paintTexture.Apply(false, false);
            _textureDirty = false;
        }

        if (_gameRunning)
        {
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0)
            {
                _timeLeft = 0;
                _gameRunning = false;
                _countdownAudioActive = false;
                _roundEndedAwaitRestart = true;
                if (_audioManager != null)
                {
                    _audioManager.PlayRoundEnded();
                        _audioManager.EnterPreStart();
                }
                ShowWinner();
            }
        }
        else if (_autoStartOnPlay && !_roundEndedAwaitRestart)
        {
            UpdateReadyCountdown();
        }

        UpdateUI();
    }

    void OnValidate()
    {
        _paintResolution = Mathf.Clamp(_paintResolution, 64, 2048);
        _brushRadiusPixels = Mathf.Clamp(_brushRadiusPixels, 1, 128);
        _brushRadiusScale = Mathf.Clamp(_brushRadiusScale, 0.1f, 4f);
        _arenaWidth = Mathf.Max(1f, _arenaWidth);
        _arenaHeight = Mathf.Max(1f, _arenaHeight);
        _canvasBackgroundColor.a = 1f;
        _playerOutlineColor.a = 1f;
        _playerOutlineScale = Mathf.Clamp(_playerOutlineScale, 1.01f, 3f);
        _playerMarkerRadius = Mathf.Max(0.05f, _playerMarkerRadius);
        _playerMarkerHeight = Mathf.Max(0.02f, _playerMarkerHeight);
        _spawnRadius = Mathf.Max(0.05f, _spawnRadius);
        _collisionDistance = Mathf.Max(0.05f, _collisionDistance);
        _readyDelaySeconds = Mathf.Max(0f, _readyDelaySeconds);
        _nonPlayerMarkerRadius = Mathf.Max(0.03f, _nonPlayerMarkerRadius);
        _maxNonPlayerDebugMarkers = Mathf.Clamp(_maxNonPlayerDebugMarkers, 0, 256);
        _spawnHintRadius = Mathf.Max(0.05f, _spawnHintRadius);
        _spawnHintCoreRadius = Mathf.Clamp(_spawnHintCoreRadius, 0.01f, _spawnHintRadius);
        _spawnHintPulseSpeed = Mathf.Max(0f, _spawnHintPulseSpeed);
        _spawnHintMinAlpha = Mathf.Clamp01(_spawnHintMinAlpha);
        _spawnHintMaxAlpha = Mathf.Clamp(_spawnHintMaxAlpha, _spawnHintMinAlpha, 1f);
        _spawnHintActiveAlphaScale = Mathf.Clamp(_spawnHintActiveAlphaScale, 0f, 1f);
        _spawnHintActiveMinAlpha = Mathf.Clamp01(_spawnHintActiveMinAlpha);
        _spawnHintContrastBlend = Mathf.Clamp01(_spawnHintContrastBlend);
        _spawnHintCoreColor.a = Mathf.Clamp01(_spawnHintCoreColor.a);
        _nonPlayerMarkerColor.a = Mathf.Clamp01(_nonPlayerMarkerColor.a);

        if (_playerMarkers == null || _playerMarkers.Length != 4)
            System.Array.Resize(ref _playerMarkers, 4);

        if (_spawnPoints == null || _spawnPoints.Length != 4)
            System.Array.Resize(ref _spawnPoints, 4);
    }

    void StartGame()
    {
        StartGame(false);
    }

    void StartGame(bool keepCurrentPlayers)
    {
        _gameRunning = false;
        _countdownAudioActive = false;
        _roundEndedAwaitRestart = false;
        _timeLeft = _gameDurationSeconds;
        _readyCountdown = _readyDelaySeconds;
        if (_winnerText != null)
            _winnerText.text = "";
        if (_audioManager != null)
        {
            // Restarting from round-end hold should keep the already-playing pregame BGM.
            if (!keepCurrentPlayers)
                _audioManager.EnterPreStart();
        }
        CacheSpawnPositions();

        if (keepCurrentPlayers)
            ResetRoundForReadyKeepingPlayers();
        else
            ResetBoard();
    }

    void ResetRoundForReadyKeepingPlayers()
    {
        _frameRawPositions.Clear();
        _allFrameRawIds.Clear();
        _allFrameRawPos.Clear();

        lock (_queueLock)
        {
            _queueFront.Clear();
            _queueBack.Clear();
            _hasQueuedFrame = false;
        }

        for (var i = 0; i < 4; i++)
        {
            _scorePixels[i] = 0;
            _killBuffer[i] = false;
            _missingThisFrame[i] = false;
            _playerRadiusMultiplier[i] = 1f;
            _playerGrowBuffUntil[i] = 0f;

            if (!_playerActive[i])
            {
                _playerRawIds[i] = -1;
                _hasPrevPos[i] = false;
                if (_playerMarkers != null && i < _playerMarkers.Length && _playerMarkers[i] != null)
                    _playerMarkers[i].gameObject.SetActive(false);
                continue;
            }

            _hasPrevPos[i] = false;
        }

        SetNonPlayerDebugMarkerVisibleCount(0);
        ClearPaintBoard();
    }

    void RegisterOscCallback()
    {
        var port = _connection != null ? _connection.port : _fallbackPort;
        if (port <= 0 || string.IsNullOrEmpty(_oscAddress))
            return;

        var server = OscMaster.GetSharedServer(port);
        server.MessageDispatcher.AddCallback(_oscAddress, OnOscTableData);

        _listeningPort = port;
        _registeredAddress = _oscAddress;
    }

    void UnregisterOscCallback()
    {
        if (_listeningPort <= 0 || string.IsNullOrEmpty(_registeredAddress))
            return;

        var server = OscMaster.GetSharedServer(_listeningPort);
        server.MessageDispatcher.RemoveCallback(_registeredAddress, OnOscTableData);

        _listeningPort = 0;
        _registeredAddress = null;
    }

    void OnOscTableData(string address, OscDataHandle data)
    {
        var count = data.GetElementCount();

        lock (_queueLock)
        {
            _queueBack.Clear();

            for (var i = 0; i + 2 < count; i += 3)
            {
                var point = new PacketPoint
                {
                    Id = Mathf.RoundToInt(data.GetElementAsFloat(i)),
                    U = data.GetElementAsFloat(i + 1),
                    V = data.GetElementAsFloat(i + 2)
                };
                _queueBack.Add(point);
            }

            _queueFront.Clear();
            _queueFront.AddRange(_queueBack);
            _hasQueuedFrame = true;
        }
    }

    void PumpIncomingData()
    {
        _queueBack.Clear();

        lock (_queueLock)
        {
            if (!_hasQueuedFrame)
                return;

            _queueBack.AddRange(_queueFront);
            _queueFront.Clear();
            _hasQueuedFrame = false;
        }

        ProcessFrame(_queueBack);
    }

    void ProcessFrame(IReadOnlyList<PacketPoint> points)
    {
        _frameRawPositions.Clear();
        _allFrameRawIds.Clear();
        _allFrameRawPos.Clear();
        ResetFrameTrackingState();

        if (_useTdSignedStableIds)
        {
            ProcessFrameUsingTdSignedStableIds(points);
            UpdatePlayerMarkers();
            UpdateNonPlayerDebugMarkers();
            return;
        }

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var worldPos = MapToArena(point.U, point.V);
            worldPos.x = Mathf.Clamp(worldPos.x, _axisMinA, _axisMaxA);
            worldPos.y = Mathf.Clamp(worldPos.y, _axisMinB, _axisMaxB);
            _frameRawPositions[point.Id] = worldPos;
            _allFrameRawIds.Add(point.Id);
            _allFrameRawPos.Add(worldPos);
        }

        UpdateTrackedPlayersFromFrame();
        ResolveMergeAndLossDeaths();

        if (_autoAssignReceivedIdsAsPlayers)
            AssignPlayersFromIncomingIds();
        else
            AssignPlayersFromSpawnZones();

        UpdatePlayerMarkers();
        UpdateNonPlayerDebugMarkers();
    }

    void ProcessFrameUsingTdSignedStableIds(IReadOnlyList<PacketPoint> points)
    {
        ResetTdStableIdFrameState();

        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (!TryParseTdStableId(point.Id, out var slot, out var isAlive))
                continue;

            var worldPos = MapToArena(point.U, point.V);
            worldPos.x = Mathf.Clamp(worldPos.x, _axisMinA, _axisMaxA);
            worldPos.y = Mathf.Clamp(worldPos.y, _axisMinB, _axisMaxB);

            if (isAlive)
            {
                _stableIdAliveThisFrame[slot] = true;
                _stableIdPosThisFrame[slot] = worldPos;
                _allFrameRawIds.Add(slot + 1);
                _allFrameRawPos.Add(worldPos);
            }
            else
            {
                _stableIdDeadThisFrame[slot] = true;
                _stableIdPosThisFrame[slot] = worldPos;
            }
        }

        for (var slot = 0; slot < _playerActive.Length; slot++)
        {
            var stableId = slot + 1;

            // TD negative stable ID means this slot is dead in the current frame.
            if (_stableIdDeadThisFrame[slot])
            {
                if (_playerActive[slot])
                    RemovePlayerFromSlot(slot);

                continue;
            }

            if (_stableIdAliveThisFrame[slot])
            {
                if (!_playerActive[slot])
                {
                    // Recover from any stale mapping before binding this stable ID back to its slot.
                    if (_rawIdToPlayerSlot.TryGetValue(stableId, out var mappedSlot) && mappedSlot != slot)
                        RemovePlayerFromSlot(mappedSlot);

                    AssignPlayerToSlot(slot, stableId, _stableIdPosThisFrame[slot]);
                }
                else
                {
                    var oldRawId = _playerRawIds[slot];
                    if (oldRawId != stableId)
                    {
                        if (oldRawId >= 0)
                            _rawIdToPlayerSlot.Remove(oldRawId);

                        _playerRawIds[slot] = stableId;
                    }

                    _rawIdToPlayerSlot[stableId] = slot;
                    UpdatePlayerPosition(slot, _stableIdPosThisFrame[slot]);
                }

                continue;
            }
        }

        ApplyTdDeadCollisionDeaths();
    }

    void ApplyTdDeadCollisionDeaths()
    {
        for (var i = 0; i < _killBuffer.Length; i++)
            _killBuffer[i] = false;

        for (var deadSlot = 0; deadSlot < _stableIdDeadThisFrame.Length; deadSlot++)
        {
            if (!_stableIdDeadThisFrame[deadSlot])
                continue;

            var victimSlot = FindNearestActiveSlotToDeadPosition(_stableIdPosThisFrame[deadSlot], deadSlot);
            if (victimSlot >= 0)
                _killBuffer[victimSlot] = true;
        }

        for (var slot = 0; slot < _killBuffer.Length; slot++)
        {
            if (_killBuffer[slot])
                RemovePlayerFromSlot(slot);
        }
    }

    int FindNearestActiveSlotToDeadPosition(Vector2 deadPosition, int deadSlot)
    {
        var maxDistanceSqr = _collisionDistance * _collisionDistance;
        var bestSlot = -1;
        var bestDistSqr = maxDistanceSqr;

        for (var slot = 0; slot < _playerActive.Length; slot++)
        {
            if (slot == deadSlot || !_playerActive[slot])
                continue;

            var distSqr = (_playerPos[slot] - deadPosition).sqrMagnitude;
            if (distSqr > bestDistSqr)
                continue;

            bestDistSqr = distSqr;
            bestSlot = slot;
        }

        return bestSlot;
    }

    void ResetTdStableIdFrameState()
    {
        for (var i = 0; i < _stableIdAliveThisFrame.Length; i++)
        {
            _stableIdAliveThisFrame[i] = false;
            _stableIdDeadThisFrame[i] = false;
        }
    }

    bool TryParseTdStableId(int rawId, out int slot, out bool isAlive)
    {
        isAlive = rawId > 0;
        var absId = Mathf.Abs(rawId);
        slot = absId - 1;
        return absId >= 1 && absId <= _playerActive.Length;
    }

    void ResetFrameTrackingState()
    {
        for (var i = 0; i < _missingThisFrame.Length; i++)
            _missingThisFrame[i] = false;
    }

    void UpdateTrackedPlayersFromFrame()
    {
        for (var slot = 0; slot < _playerActive.Length; slot++)
        {
            if (!_playerActive[slot])
                continue;

            var rawId = _playerRawIds[slot];
            if (rawId < 0 || !_frameRawPositions.TryGetValue(rawId, out var worldPos))
            {
                _missingThisFrame[slot] = true;
                _missingLastPos[slot] = _playerPos[slot];
                continue;
            }

            _frameRawPositions.Remove(rawId);
            UpdatePlayerPosition(slot, worldPos);
        }
    }

    void ResolveMergeAndLossDeaths()
    {
        for (var i = 0; i < _killBuffer.Length; i++)
            _killBuffer[i] = false;

        for (var slot = 0; slot < _playerActive.Length; slot++)
        {
            if (!_playerActive[slot] || !_missingThisFrame[slot])
                continue;

            // TD 追蹤在這幀丟失玩家，先判定為死亡。
            _killBuffer[slot] = true;

            // 若同時存在鄰近玩家，視為 TD 合併事件，立即雙方死亡。
            var partner = FindMergePartnerSlot(slot);
            if (partner >= 0)
                _killBuffer[partner] = true;
        }

        for (var slot = 0; slot < _killBuffer.Length; slot++)
        {
            if (_killBuffer[slot])
                RemovePlayerFromSlot(slot);
        }
    }

    int FindMergePartnerSlot(int lostSlot)
    {
        var maxDistanceSqr = _collisionDistance * _collisionDistance;
        var bestSlot = -1;
        var bestDistSqr = maxDistanceSqr;
        var lostPos = _missingLastPos[lostSlot];

        for (var slot = 0; slot < _playerActive.Length; slot++)
        {
            if (slot == lostSlot || !_playerActive[slot] || _missingThisFrame[slot])
                continue;

            var distSqr = (_playerPos[slot] - lostPos).sqrMagnitude;
            if (distSqr > bestDistSqr)
                continue;

            bestDistSqr = distSqr;
            bestSlot = slot;
        }

        return bestSlot;
    }

    void AssignPlayersFromSpawnZones()
    {
        if (_frameRawPositions.Count == 0)
            return;

        foreach (var kv in _frameRawPositions)
        {
            if (_rawIdToPlayerSlot.ContainsKey(kv.Key))
                continue;

            var spawnSlot = FindSpawnSlotForPosition(kv.Value);
            if (spawnSlot < 0)
                continue;

            AssignPlayerToSlot(spawnSlot, kv.Key, kv.Value);
        }
    }

    void AssignPlayersFromIncomingIds()
    {
        if (_frameRawPositions.Count == 0)
            return;

        var ids = new List<int>(_frameRawPositions.Keys);
        ids.Sort();

        for (var i = 0; i < ids.Count; i++)
        {
            var rawId = ids[i];
            if (_rawIdToPlayerSlot.ContainsKey(rawId))
                continue;

            var slot = FindFirstFreePlayerSlot();
            if (slot < 0)
                return;

            AssignPlayerToSlot(slot, rawId, _frameRawPositions[rawId]);
        }
    }

    int FindFirstFreePlayerSlot()
    {
        for (var i = 0; i < _playerActive.Length; i++)
        {
            if (!_playerActive[i])
                return i;
        }

        return -1;
    }

    int FindSpawnSlotForPosition(Vector2 worldPos)
    {
        var bestSlot = -1;
        var bestDistSqr = float.MaxValue;
        var maxDistSqr = _spawnRadius * _spawnRadius;

        for (var i = 0; i < _spawnPos.Length; i++)
        {
            if (_playerActive[i])
                continue;

            var distSqr = (_spawnPos[i] - worldPos).sqrMagnitude;
            if (distSqr > maxDistSqr || distSqr >= bestDistSqr)
                continue;

            bestDistSqr = distSqr;
            bestSlot = i;
        }

        return bestSlot;
    }

    void AssignPlayerToSlot(int slot, int rawId, Vector2 worldPos)
    {
        if (slot < 0 || slot >= _playerActive.Length || _playerActive[slot])
            return;

        if (_rawIdToPlayerSlot.ContainsKey(rawId))
            return;

        _rawIdToPlayerSlot[rawId] = slot;
        _playerRawIds[slot] = rawId;
        _playerActive[slot] = true;
        _hasPrevPos[slot] = false;
        _playerRadiusMultiplier[slot] = 1f;
        _playerGrowBuffUntil[slot] = 0f;

        if (_audioManager != null)
            _audioManager.PlayPlayerBecameActive();

        UpdatePlayerPosition(slot, worldPos);
    }

    void RemovePlayerFromSlot(int slot)
    {
        if (slot < 0 || slot >= _playerActive.Length || !_playerActive[slot])
            return;

        var rawId = _playerRawIds[slot];
        if (rawId >= 0)
            _rawIdToPlayerSlot.Remove(rawId);

        _playerRawIds[slot] = -1;
        _playerActive[slot] = false;
        _hasPrevPos[slot] = false;
        _playerRadiusMultiplier[slot] = 1f;
        _playerGrowBuffUntil[slot] = 0f;

        var marker = _playerMarkers[slot];
        if (marker != null)
            marker.gameObject.SetActive(false);
    }

    void UpdatePlayerPosition(int slot, Vector2 worldPos)
    {
        _playerPos[slot] = worldPos;
        var radiusMultiplier = GetPlayerRadiusMultiplier(slot);

        if (!_hasPrevPos[slot])
        {
            _playerPrevPos[slot] = worldPos;
            _hasPrevPos[slot] = true;

            if (_gameRunning)
                PaintDot(worldPos, slot + 1, radiusMultiplier);

            return;
        }

        if (_gameRunning)
            PaintSegment(_playerPrevPos[slot], worldPos, slot + 1, radiusMultiplier);

        _playerPrevPos[slot] = worldPos;
    }

    void UpdatePlayerMarkers()
    {
        for (var i = 0; i < _playerMarkers.Length; i++)
        {
            var marker = _playerMarkers[i];
            if (marker == null)
                continue;

            if (!_playerActive[i])
            {
                marker.gameObject.SetActive(false);
                continue;
            }

            marker.gameObject.SetActive(true);
            var radius = GetPlayerEffectiveRadius(i);
            if (_arenaPlane == ArenaPlane.XY)
                marker.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            else
                marker.localScale = new Vector3(radius * 2f, _playerMarkerHeight * 0.5f, radius * 2f);

            marker.position = MarkerWorldPosition(_playerPos[i]);
        }
    }

    void UpdateNonPlayerDebugMarkers()
    {
        if (!_showNonPlayerDebugMarkers || _maxNonPlayerDebugMarkers <= 0)
        {
            SetNonPlayerDebugMarkerVisibleCount(0);
            return;
        }

        _nonPlayerDebugPosBuffer.Clear();

        for (var i = 0; i < _allFrameRawPos.Count; i++)
        {
            var rawId = _allFrameRawIds[i];
            var rawPos = _allFrameRawPos[i];

            if (_rawIdToPlayerSlot.TryGetValue(rawId, out var slot) &&
                slot >= 0 &&
                slot < _playerActive.Length &&
                _playerActive[slot] &&
                _playerRawIds[slot] == rawId)
            {
                continue;
            }

            _nonPlayerDebugPosBuffer.Add(rawPos);
            if (_nonPlayerDebugPosBuffer.Count >= _maxNonPlayerDebugMarkers)
                break;
        }

        EnsureNonPlayerDebugMarkerPool(_nonPlayerDebugPosBuffer.Count);

        for (var i = 0; i < _nonPlayerDebugPosBuffer.Count; i++)
        {
            var marker = _nonPlayerDebugMarkers[i];
            var renderer = marker.GetComponent<SpriteRenderer>();
            if (renderer != null)
                renderer.color = _nonPlayerMarkerColor;

            marker.localScale = MarkerVisualScale(_nonPlayerMarkerRadius);
            marker.position = MarkerWorldPosition(_nonPlayerDebugPosBuffer[i]);
            marker.gameObject.SetActive(true);
        }

        SetNonPlayerDebugMarkerVisibleCount(_nonPlayerDebugPosBuffer.Count);
    }

    void EnsureNonPlayerDebugMarkerPool(int requiredCount)
    {
        while (_nonPlayerDebugMarkers.Count < requiredCount)
        {
            var marker = CreateRuntimeSpriteMarker("__DebugNonPlayer_" + (_nonPlayerDebugMarkers.Count + 1), _nonPlayerMarkerSortingOffset);
            _nonPlayerDebugMarkers.Add(marker);
        }
    }

    void SetNonPlayerDebugMarkerVisibleCount(int visibleCount)
    {
        for (var i = visibleCount; i < _nonPlayerDebugMarkers.Count; i++)
        {
            if (_nonPlayerDebugMarkers[i] != null)
                _nonPlayerDebugMarkers[i].gameObject.SetActive(false);
        }
    }

    Transform CreateRuntimeSpriteMarker(string objectName, int sortingOffset)
    {
        var marker = new GameObject(objectName);
        marker.transform.SetParent(transform, false);

        if (_arenaPlane == ArenaPlane.XZ)
            marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = GetRuntimeMarkerSprite();

        var sourceRenderer = _playerMarkers != null && _playerMarkers.Length > 0 && _playerMarkers[0] != null
            ? _playerMarkers[0].GetComponent<SpriteRenderer>()
            : null;

        if (sourceRenderer != null)
        {
            renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            renderer.sortingOrder = sourceRenderer.sortingOrder + sortingOffset;
        }

        marker.SetActive(false);
        return marker.transform;
    }

    Sprite GetRuntimeMarkerSprite()
    {
        if (_runtimeMarkerSprite != null)
            return _runtimeMarkerSprite;

        if (_playerMarkers != null)
        {
            for (var i = 0; i < _playerMarkers.Length; i++)
            {
                var marker = _playerMarkers[i];
                if (marker == null)
                    continue;

                var spriteRenderer = marker.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null && spriteRenderer.sprite != null)
                {
                    _runtimeMarkerSprite = spriteRenderer.sprite;
                    return _runtimeMarkerSprite;
                }
            }
        }

        _runtimeMarkerSprite = BuildFallbackCircleSprite();
        return _runtimeMarkerSprite;
    }

    Sprite BuildFallbackCircleSprite()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var center = (size - 1) * 0.5f;
        var radius = center - 1f;
        var pixels = new Color32[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var dist = Mathf.Sqrt(dx * dx + dy * dy);

                byte alpha;
                if (dist <= radius - 1f)
                {
                    alpha = 255;
                }
                else if (dist <= radius + 1f)
                {
                    var t = Mathf.InverseLerp(radius + 1f, radius - 1f, dist);
                    alpha = (byte)Mathf.RoundToInt(255f * t);
                }
                else
                {
                    alpha = 0;
                }

                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    Vector3 MarkerVisualScale(float radius)
    {
        var diameter = Mathf.Max(0.02f, radius * 2f);
        return new Vector3(diameter, diameter, 1f);
    }

    void ConfigureSpawnHints()
    {
        for (var i = 0; i < _spawnHintMarkers.Length; i++)
        {
            if (_spawnHintMarkers[i] == null)
                _spawnHintMarkers[i] = CreateRuntimeSpriteMarker("__SpawnHint_" + (i + 1), _spawnHintSortingOffset);

            if (_spawnHintCoreMarkers[i] == null)
                _spawnHintCoreMarkers[i] = CreateRuntimeSpriteMarker("__SpawnHintCore_" + (i + 1), _spawnHintSortingOffset + 1);
        }

        UpdateSpawnHintVisuals();
    }

    void UpdateSpawnHintVisuals()
    {
        if (!_showSpawnHints)
        {
            for (var i = 0; i < _spawnHintMarkers.Length; i++)
            {
                if (_spawnHintMarkers[i] != null)
                    _spawnHintMarkers[i].gameObject.SetActive(false);

                if (_spawnHintCoreMarkers[i] != null)
                    _spawnHintCoreMarkers[i].gameObject.SetActive(false);
            }

            return;
        }

        var pulse = _spawnHintPulseSpeed <= 0.001f
            ? 1f
            : 0.5f * (Mathf.Sin(Time.time * _spawnHintPulseSpeed) + 1f);

        var baseAlpha = Mathf.Lerp(_spawnHintMinAlpha, _spawnHintMaxAlpha, pulse);
        var pulseScale = 1f + pulse * 0.12f;

        for (var i = 0; i < _spawnHintMarkers.Length; i++)
        {
            if (_spawnHintMarkers[i] == null)
                _spawnHintMarkers[i] = CreateRuntimeSpriteMarker("__SpawnHint_" + (i + 1), _spawnHintSortingOffset);

            var marker = _spawnHintMarkers[i];
            marker.gameObject.SetActive(true);
            marker.position = MarkerWorldPosition(_spawnPos[i]);
            marker.localScale = MarkerVisualScale(_spawnHintRadius * pulseScale);

            var spriteRenderer = marker.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                var alpha = _playerActive[i]
                    ? Mathf.Max(baseAlpha * _spawnHintActiveAlphaScale, _spawnHintActiveMinAlpha)
                    : baseAlpha;
                var c = BuildSpawnHintRingColor(i);
                c.a = Mathf.Clamp01(alpha);
                spriteRenderer.color = c;
            }

            var core = _spawnHintCoreMarkers[i];
            if (core != null)
            {
                core.gameObject.SetActive(true);
                core.position = marker.position;
                core.localScale = MarkerVisualScale(_spawnHintCoreRadius);

                var coreRenderer = core.GetComponent<SpriteRenderer>();
                if (coreRenderer != null)
                {
                    var coreColor = Color.Lerp(_spawnHintCoreColor, (Color)PlayerColors[i], 0.25f);
                    coreColor.a = Mathf.Clamp01(_spawnHintCoreColor.a * (0.85f + pulse * 0.15f));
                    coreRenderer.color = coreColor;
                }
            }
        }
    }

    Color BuildSpawnHintRingColor(int playerIndex)
    {
        var clampedIndex = Mathf.Clamp(playerIndex, 0, PlayerColors.Length - 1);
        var playerColor = (Color)PlayerColors[clampedIndex];
        var luminance = playerColor.r * 0.299f + playerColor.g * 0.587f + playerColor.b * 0.114f;
        var contrastColor = luminance >= 0.5f ? Color.black : Color.white;
        return Color.Lerp(playerColor, contrastColor, _spawnHintContrastBlend);
    }

    void UpdateTimedPlayerBuffs()
    {
        var now = Time.time;
        for (var i = 0; i < _playerRadiusMultiplier.Length; i++)
        {
            if (_playerRadiusMultiplier[i] <= 1f)
                continue;

            if (now < _playerGrowBuffUntil[i])
                continue;

            _playerRadiusMultiplier[i] = 1f;
            _playerGrowBuffUntil[i] = 0f;
        }
    }

    bool IsValidPlayerIndex(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < _playerActive.Length;
    }

    float GetPlayerRadiusMultiplier(int playerIndex)
    {
        if (!IsValidPlayerIndex(playerIndex))
            return 1f;

        return Mathf.Max(1f, _playerRadiusMultiplier[playerIndex]);
    }

    float GetPlayerEffectiveRadius(int playerIndex)
    {
        return _playerMarkerRadius * GetPlayerRadiusMultiplier(playerIndex);
    }

    void PaintCircleWorld(Vector2 worldCenter, float worldRadius, int owner)
    {
        worldRadius = Mathf.Max(0.01f, worldRadius);
        var u = Mathf.InverseLerp(_axisMinA, _axisMaxA, worldCenter.x);
        var v = Mathf.InverseLerp(_axisMinB, _axisMaxB, worldCenter.y);

        var px = Mathf.RoundToInt(u * (_paintResolution - 1));
        var py = Mathf.RoundToInt(v * (_paintResolution - 1));
        var rx = Mathf.Max(1, Mathf.CeilToInt(worldRadius / Mathf.Max(0.0001f, _pixelWorldSizeA)));
        var ry = Mathf.Max(1, Mathf.CeilToInt(worldRadius / Mathf.Max(0.0001f, _pixelWorldSizeB)));
        var invRx2 = 1f / (rx * rx);
        var invRy2 = 1f / (ry * ry);

        for (var y = -ry; y <= ry; y++)
        {
            var yy = py + y;
            if (yy < 0 || yy >= _paintResolution)
                continue;

            for (var x = -rx; x <= rx; x++)
            {
                var ellipseCheck = x * x * invRx2 + y * y * invRy2;
                if (ellipseCheck > 1f)
                    continue;

                var xx = px + x;
                if (xx < 0 || xx >= _paintResolution)
                    continue;

                var index = yy * _paintResolution + xx;
                var previousOwner = _ownerMap[index];
                if (previousOwner == owner)
                    continue;

                if (previousOwner > 0)
                    _scorePixels[previousOwner - 1]--;

                _ownerMap[index] = (byte)owner;
                _scorePixels[owner - 1]++;
                _paintPixels[index] = PlayerColors[owner - 1];
                _textureDirty = true;
            }
        }
    }

    void ClearOtherPlayersInCircle(Vector2 worldCenter, float worldRadius, int keepOwner)
    {
        worldRadius = Mathf.Max(0.01f, worldRadius);
        var u = Mathf.InverseLerp(_axisMinA, _axisMaxA, worldCenter.x);
        var v = Mathf.InverseLerp(_axisMinB, _axisMaxB, worldCenter.y);

        var px = Mathf.RoundToInt(u * (_paintResolution - 1));
        var py = Mathf.RoundToInt(v * (_paintResolution - 1));
        var rx = Mathf.Max(1, Mathf.CeilToInt(worldRadius / Mathf.Max(0.0001f, _pixelWorldSizeA)));
        var ry = Mathf.Max(1, Mathf.CeilToInt(worldRadius / Mathf.Max(0.0001f, _pixelWorldSizeB)));
        var invRx2 = 1f / (rx * rx);
        var invRy2 = 1f / (ry * ry);
        var background = (Color32)_canvasBackgroundColor;

        for (var y = -ry; y <= ry; y++)
        {
            var yy = py + y;
            if (yy < 0 || yy >= _paintResolution)
                continue;

            for (var x = -rx; x <= rx; x++)
            {
                var ellipseCheck = x * x * invRx2 + y * y * invRy2;
                if (ellipseCheck > 1f)
                    continue;

                var xx = px + x;
                if (xx < 0 || xx >= _paintResolution)
                    continue;

                var index = yy * _paintResolution + xx;
                var previousOwner = _ownerMap[index];
                if (previousOwner == 0 || previousOwner == keepOwner)
                    continue;

                _scorePixels[previousOwner - 1]--;
                _ownerMap[index] = 0;
                _paintPixels[index] = background;
                _textureDirty = true;
            }
        }
    }

    Vector2 MapToArena(float u, float v)
    {
        if (_coordinateMode == CoordinateMode.WorldSpace)
            return new Vector2(u, v);

        if (_coordinateMode == CoordinateMode.ValueRange)
        {
            u = Mathf.InverseLerp(_uRange.x, _uRange.y, u);
            v = Mathf.InverseLerp(_vRange.x, _vRange.y, v);
        }

        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);
        if (_flipV)
            v = 1f - v;

        var axisA = Mathf.Lerp(_axisMinA, _axisMaxA, u);
        var axisB = Mathf.Lerp(_axisMinB, _axisMaxB, v);
        return new Vector2(axisA, axisB);
    }

    void PaintSegment(Vector2 a, Vector2 b, int owner, float radiusMultiplier = 1f)
    {
        var distance = Vector2.Distance(a, b);
        radiusMultiplier = Mathf.Max(0.1f, radiusMultiplier);

        var brushWorldA = _brushRadiusPixelsA * _pixelWorldSizeA * radiusMultiplier;
        var brushWorldB = _brushRadiusPixelsB * _pixelWorldSizeB * radiusMultiplier;
        var brushWorld = Mathf.Max(0.02f, Mathf.Min(brushWorldA, brushWorldB));
        var step = Mathf.Max(brushWorld * 0.6f, 0.01f);
        var steps = Mathf.Max(1, Mathf.CeilToInt(distance / step));

        for (var i = 0; i <= steps; i++)
        {
            var t = steps == 0 ? 0 : i / (float)steps;
            var p = Vector2.Lerp(a, b, t);
            PaintDot(p, owner, radiusMultiplier);
        }
    }

    void PaintDot(Vector2 worldPos, int owner, float radiusMultiplier = 1f)
    {
        radiusMultiplier = Mathf.Max(0.1f, radiusMultiplier);

        var u = Mathf.InverseLerp(_axisMinA, _axisMaxA, worldPos.x);
        var v = Mathf.InverseLerp(_axisMinB, _axisMaxB, worldPos.y);

        var px = Mathf.RoundToInt(u * (_paintResolution - 1));
        var py = Mathf.RoundToInt(v * (_paintResolution - 1));
        var rx = Mathf.Max(1, Mathf.RoundToInt(_brushRadiusPixelsA * radiusMultiplier));
        var ry = Mathf.Max(1, Mathf.RoundToInt(_brushRadiusPixelsB * radiusMultiplier));
        var invRx2 = 1f / (rx * rx);
        var invRy2 = 1f / (ry * ry);

        for (var y = -ry; y <= ry; y++)
        {
            var yy = py + y;
            if (yy < 0 || yy >= _paintResolution)
                continue;

            for (var x = -rx; x <= rx; x++)
            {
                var ellipseCheck = x * x * invRx2 + y * y * invRy2;
                if (ellipseCheck > 1f)
                    continue;

                var xx = px + x;
                if (xx < 0 || xx >= _paintResolution)
                    continue;

                var index = yy * _paintResolution + xx;
                var previousOwner = _ownerMap[index];

                if (previousOwner == owner)
                    continue;

                if (previousOwner > 0)
                    _scorePixels[previousOwner - 1]--;

                _ownerMap[index] = (byte)owner;
                _scorePixels[owner - 1]++;
                _paintPixels[index] = PlayerColors[owner - 1];
                _textureDirty = true;
            }
        }
    }

    void ResetBoard()
    {
        _rawIdToPlayerSlot.Clear();
        _frameRawPositions.Clear();
        _allFrameRawIds.Clear();
        _allFrameRawPos.Clear();

        lock (_queueLock)
        {
            _queueFront.Clear();
            _queueBack.Clear();
            _hasQueuedFrame = false;
        }

        for (var i = 0; i < 4; i++)
        {
            _playerRawIds[i] = -1;
            _scorePixels[i] = 0;
            _playerActive[i] = false;
            _hasPrevPos[i] = false;
            _playerRadiusMultiplier[i] = 1f;
            _playerGrowBuffUntil[i] = 0f;
            _killBuffer[i] = false;
            _missingThisFrame[i] = false;
            if (_playerMarkers != null && i < _playerMarkers.Length && _playerMarkers[i] != null)
                _playerMarkers[i].gameObject.SetActive(false);
        }

        SetNonPlayerDebugMarkerVisibleCount(0);

        ClearPaintBoard();
    }

    void ClearPaintBoard()
    {
        if (_paintPixels == null || _ownerMap == null)
            return;

        _scorePixels[0] = 0;
        _scorePixels[1] = 0;
        _scorePixels[2] = 0;
        _scorePixels[3] = 0;

        var background = (Color32)_canvasBackgroundColor;
        for (var i = 0; i < _paintPixels.Length; i++)
        {
            _paintPixels[i] = background;
            _ownerMap[i] = 0;
        }

        _textureDirty = true;
    }

    void RecountScoresFromOwnerMap()
    {
        _scorePixels[0] = 0;
        _scorePixels[1] = 0;
        _scorePixels[2] = 0;
        _scorePixels[3] = 0;

        for (var i = 0; i < _ownerMap.Length; i++)
        {
            var owner = _ownerMap[i];
            if (owner >= 1 && owner <= 4)
                _scorePixels[owner - 1]++;
        }
    }

    void DebugFillBoard(int owner)
    {
        if (_paintPixels == null || _ownerMap == null)
            return;

        owner = Mathf.Clamp(owner, 0, 4);
        var fillColor = owner == 0 ? (Color32)_canvasBackgroundColor : PlayerColors[owner - 1];

        for (var i = 0; i < _ownerMap.Length; i++)
        {
            _ownerMap[i] = (byte)owner;
            _paintPixels[i] = fillColor;
        }

        _scorePixels[0] = 0;
        _scorePixels[1] = 0;
        _scorePixels[2] = 0;
        _scorePixels[3] = 0;

        if (owner >= 1 && owner <= 4)
            _scorePixels[owner - 1] = _ownerMap.Length;

        _textureDirty = true;
    }

    void ShowWinner()
    {
        if (_winnerText == null)
            return;

        var bestScore = -1;
        var winner = -1;
        var tie = false;

        for (var i = 0; i < _scorePixels.Length; i++)
        {
            if (_scorePixels[i] > bestScore)
            {
                bestScore = _scorePixels[i];
                winner = i;
                tie = false;
            }
            else if (_scorePixels[i] == bestScore)
            {
                tie = true;
            }
        }

        if (winner < 0 || bestScore <= 0)
        {
            _winnerText.text = "Time Up - No Winner";
            _winnerText.color = Color.white;
            return;
        }

        if (tie)
        {
            _winnerText.text = "Time Up - Draw";
            _winnerText.color = Color.white;
        }
        else
        {
            _winnerText.text = "Winner: Player " + (winner + 1);
            _winnerText.color = PlayerColors[winner];
        }
    }

    void UpdateReadyCountdown()
    {
        if (_gameRunning || _roundEndedAwaitRestart)
            return;

        if (ActivePlayerCount() < 4)
        {
            if (_countdownAudioActive)
            {
                _countdownAudioActive = false;
                if (_audioManager != null)
                    _audioManager.EnterPreStart();
            }

            _readyCountdown = _readyDelaySeconds;
            return;
        }

        if (!_countdownAudioActive)
        {
            _countdownAudioActive = true;
            if (_audioManager != null)
                _audioManager.EnterCountdown();
        }

        _readyCountdown -= Time.deltaTime;
        if (_readyCountdown > 0f)
            return;

        BeginRound();
    }

    void BeginRound()
    {
        _gameRunning = true;
        _countdownAudioActive = false;
        _roundEndedAwaitRestart = false;
        _timeLeft = _gameDurationSeconds;
        _readyCountdown = _readyDelaySeconds;

        if (_winnerText != null)
            _winnerText.text = "";

        if (_audioManager != null)
            _audioManager.EnterGameplay();

        var paintedPixels = _scorePixels[0] + _scorePixels[1] + _scorePixels[2] + _scorePixels[3];
        if (paintedPixels > 0)
            ClearPaintBoard();

        for (var i = 0; i < _playerActive.Length; i++)
        {
            if (!_playerActive[i])
                continue;

            _playerPrevPos[i] = _playerPos[i];
            _hasPrevPos[i] = true;
        }
    }

    int ActivePlayerCount()
    {
        var count = 0;
        for (var i = 0; i < _playerActive.Length; i++)
        {
            if (_playerActive[i])
                count++;
        }

        return count;
    }

    void UpdateUI()
    {
        if (_timerText != null)
        {
            if (_gameRunning)
            {
                _timerText.text = "Time: " + _timeLeft.ToString("00.0");
            }
            else if (_roundEndedAwaitRestart)
            {
                _timerText.text = "Round End";
            }
            else
            {
                var activePlayers = ActivePlayerCount();
                if (_autoStartOnPlay && activePlayers == 4)
                {
                    _timerText.text = "Start In: " + Mathf.Max(0f, _readyCountdown).ToString("0.0");
                }
                else
                {
                    _timerText.text = string.Format("Ready: {0}/4", activePlayers);
                }
            }
        }

        var total = Mathf.Max(1, _paintResolution * _paintResolution);
        var paintedPixels = _scorePixels[0] + _scorePixels[1] + _scorePixels[2] + _scorePixels[3];
        var denominator = _playerPercentUsesPaintedArea
            ? Mathf.Max(1, paintedPixels)
            : total;

        UpdateSingleScoreText(_scoreText, 0, denominator);
        UpdateSingleScoreText(_scoreTextP2, 1, denominator);
        UpdateSingleScoreText(_scoreTextP3, 2, denominator);
        UpdateSingleScoreText(_scoreTextP4, 3, denominator);

        if (_scoreHintText != null)
        {
            var covered = paintedPixels * 100f / total;
            var mode = _playerPercentUsesPaintedArea ? "Painted Share" : "Arena Share";
            var restartHint = _roundEndedAwaitRestart ? "" : "  Press R to restart";
            _scoreHintText.text = string.Format("Covered {0:0.0}%  ({1}){2}", covered, mode, restartHint);
        }
    }

    void UpdateSingleScoreText(TMP_Text scoreText, int playerIndex, int totalPixels)
    {
        if (scoreText == null)
            return;

        var value = _scorePixels[playerIndex] * 100f / totalPixels;
        scoreText.text = string.Format("P{0}  {1,5:0.0}%", playerIndex + 1, value);
    }

    bool ConfigureSceneReferences()
    {
        var missing = new List<string>();

        if (_arenaRenderer == null)
            missing.Add("Arena Renderer");

        if (_wallCamera == null)
            missing.Add("Wall Camera");

        if (_floorCamera == null)
            missing.Add("Floor Camera");

        if (_playerMarkers == null || _playerMarkers.Length < 4)
        {
            missing.Add("4 Player Markers");
        }
        else
        {
            for (var i = 0; i < 4; i++)
            {
                if (_playerMarkers[i] == null)
                    missing.Add("Player Marker " + (i + 1));
            }
        }

        if (missing.Count > 0)
        {
            Debug.LogError("TDTableReceiver missing scene references: " + string.Join(", ", missing));
            return false;
        }

        LogOptionalBindingStatus();

        ConfigureArenaMaterial();
        CacheSpawnPositions();
        ConfigurePlayerMarkers();
        ConfigureSpawnHints();
        ConfigureBrushRadius();
        ConfigureCameras();
        ConfigureScoreTexts();
        return true;
    }

    void LogOptionalBindingStatus()
    {
        var optionalMissing = new List<string>();

        var assignedSpawnCount = 0;
        if (_spawnPoints != null)
        {
            for (var i = 0; i < 4; i++)
            {
                if (i >= _spawnPoints.Length || _spawnPoints[i] == null)
                    continue;

                assignedSpawnCount++;
            }
        }

        // 全部留空代表刻意使用 Player Marker fallback，不視為警告。
        if (assignedSpawnCount > 0 && assignedSpawnCount < 4)
        {
            for (var i = 0; i < 4; i++)
            {
                var hasSpawn = _spawnPoints != null && i < _spawnPoints.Length && _spawnPoints[i] != null;
                if (!hasSpawn)
                    optionalMissing.Add("Spawn Point " + (i + 1) + " (fallback to Player Marker " + (i + 1) + ")");
            }
        }

        if (_useRenderTextures)
        {
            if (_wallOutput == null)
                optionalMissing.Add("Wall RenderTexture");

            if (_floorOutput == null)
                optionalMissing.Add("Floor RenderTexture");
        }

        if (_audioManager == null)
            optionalMissing.Add("TDGameAudioManager");

        if (optionalMissing.Count > 0)
        {
            Debug.LogWarning("TDTableReceiver optional references not bound: " + string.Join(", ", optionalMissing));
        }
    }

    void ConfigureBrushRadius()
    {
        var effectiveWorldRadius = _playerMarkerRadius;

        if (_matchBrushToMarkerSize && _playerMarkers != null && _playerMarkers.Length > 0 && _playerMarkers[0] != null)
        {
            var markerRenderer = _playerMarkers[0].GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                var extents = markerRenderer.bounds.extents;
                effectiveWorldRadius = _arenaPlane == ArenaPlane.XY
                    ? Mathf.Max(extents.x, extents.y)
                    : Mathf.Max(extents.x, extents.z);
            }
        }

        effectiveWorldRadius *= _brushRadiusScale;

        _brushRadiusPixelsA = _matchBrushToMarkerSize
            ? Mathf.Clamp(Mathf.RoundToInt(effectiveWorldRadius / Mathf.Max(0.0001f, _pixelWorldSizeA)), 1, 256)
            : _brushRadiusPixels;

        _brushRadiusPixelsB = _matchBrushToMarkerSize
            ? Mathf.Clamp(Mathf.RoundToInt(effectiveWorldRadius / Mathf.Max(0.0001f, _pixelWorldSizeB)), 1, 256)
            : _brushRadiusPixels;
    }

    void ConfigureScoreTexts()
    {
        ConfigureSingleScoreText(_scoreText, 0);
        ConfigureSingleScoreText(_scoreTextP2, 1);
        ConfigureSingleScoreText(_scoreTextP3, 2);
        ConfigureSingleScoreText(_scoreTextP4, 3);

        if (_scoreText == null || _scoreTextP2 == null || _scoreTextP3 == null || _scoreTextP4 == null)
        {
            Debug.LogWarning("TDTableReceiver: assign 4 score TMP texts (P1~P4) for split colored score UI.");
        }

        if (_scoreHintText != null && string.IsNullOrWhiteSpace(_scoreHintText.text))
            _scoreHintText.text = "Waiting for players";
    }

    void ConfigureSingleScoreText(TMP_Text scoreText, int playerIndex)
    {
        if (scoreText == null)
            return;

        scoreText.color = PlayerColors[playerIndex];

        var current = scoreText.text == null ? "" : scoreText.text.Trim();
        if (current.Length == 0 || current == "New Text")
            scoreText.text = $"P{playerIndex + 1}  0.0%";
    }

    void CacheSpawnPositions()
    {
        for (var i = 0; i < _spawnPos.Length; i++)
        {
            Transform source = null;
            if (_spawnPoints != null && i < _spawnPoints.Length)
                source = _spawnPoints[i];

            if (source == null && _playerMarkers != null && i < _playerMarkers.Length)
                source = _playerMarkers[i];

            if (source != null)
            {
                var p = source.position;
                _spawnPos[i] = _arenaPlane == ArenaPlane.XY
                    ? new Vector2(p.x, p.y)
                    : new Vector2(p.x, p.z);
            }
            else
            {
                _spawnPos[i] = FallbackSpawnPosition(i);
            }
        }
    }

    Vector2 FallbackSpawnPosition(int playerSlot)
    {
        switch (playerSlot)
        {
            case 0:
                return new Vector2(_axisMaxA, _axisMaxB);
            case 1:
                return new Vector2(_axisMaxA, _axisMinB);
            case 2:
                return new Vector2(_axisMinA, _axisMaxB);
            default:
                return new Vector2(_axisMinA, _axisMinB);
        }
    }

    void ConfigureArenaMaterial()
    {
        var bounds = _arenaRenderer.bounds;

        if (_useArenaBoundsForSize)
        {
            if (_arenaPlane == ArenaPlane.XY)
            {
                _arenaWidth = Mathf.Max(1f, bounds.size.x);
                _arenaHeight = Mathf.Max(1f, bounds.size.y);
            }
            else
            {
                _arenaWidth = Mathf.Max(1f, bounds.size.x);
                _arenaHeight = Mathf.Max(1f, bounds.size.z);
            }
        }

        if (_arenaPlane == ArenaPlane.XY)
        {
            _axisMinA = bounds.min.x;
            _axisMaxA = bounds.max.x;
            _axisMinB = bounds.min.y;
            _axisMaxB = bounds.max.y;
            _markerPlaneValue = bounds.center.z + _markerPlaneOffset;
        }
        else
        {
            _axisMinA = bounds.min.x;
            _axisMaxA = bounds.max.x;
            _axisMinB = bounds.min.z;
            _axisMaxB = bounds.max.z;
            _markerPlaneValue = bounds.max.y + _playerMarkerHeight * 0.5f + _markerPlaneOffset;
        }

        _pixelWorldSizeA = (_axisMaxA - _axisMinA) / Mathf.Max(1, _paintResolution - 1);
        _pixelWorldSizeB = (_axisMaxB - _axisMinB) / Mathf.Max(1, _paintResolution - 1);

        _paintTexture = new Texture2D(_paintResolution, _paintResolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = _usePointFilterForPaintTexture ? FilterMode.Point : FilterMode.Bilinear
        };

        _paintPixels = new Color32[_paintResolution * _paintResolution];
        _ownerMap = new byte[_paintResolution * _paintResolution];

        var spriteRenderer = _arenaRenderer as SpriteRenderer;
        if (spriteRenderer != null)
        {
            var lossyX = Mathf.Max(0.0001f, Mathf.Abs(spriteRenderer.transform.lossyScale.x));
            var targetWorldWidth = Mathf.Max(0.0001f, bounds.size.x);
            var pixelsPerUnit = (_paintResolution * lossyX) / targetWorldWidth;

            var runtimeSprite = Sprite.Create(
                _paintTexture,
                new Rect(0, 0, _paintResolution, _paintResolution),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit
            );
            runtimeSprite.name = "ArenaRuntimePaint";

            spriteRenderer.sprite = runtimeSprite;
            spriteRenderer.color = Color.white;
            return;
        }

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");

        var material = _arenaRenderer.sharedMaterial != null
            ? new Material(_arenaRenderer.sharedMaterial)
            : new Material(shader);

        material.mainTexture = _paintTexture;
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", _paintTexture);
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", _paintTexture);

        _arenaRenderer.sharedMaterial = material;
    }

    void ConfigurePlayerMarkers()
    {
        for (var i = 0; i < 4; i++)
        {
            var marker = _playerMarkers[i];
            marker.gameObject.SetActive(false);
            if (_arenaPlane == ArenaPlane.XY)
                marker.localScale = new Vector3(_playerMarkerRadius * 2f, _playerMarkerRadius * 2f, 1f);
            else
                marker.localScale = new Vector3(_playerMarkerRadius * 2f, _playerMarkerHeight * 0.5f, _playerMarkerRadius * 2f);

            var spriteRenderer = marker.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.color = PlayerColors[i];
                ConfigureSpriteOutline(marker, spriteRenderer);
                continue;
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Standard");

                var mat = renderer.sharedMaterial != null
                    ? new Material(renderer.sharedMaterial)
                    : new Material(shader);

                mat.color = PlayerColors[i];
                renderer.sharedMaterial = mat;
            }
        }
    }

    void ConfigureSpriteOutline(Transform marker, SpriteRenderer markerRenderer)
    {
        const string outlineName = "__MarkerOutline";
        var outlineTransform = marker.Find(outlineName);

        if (!_enablePlayerOutline)
        {
            if (outlineTransform != null)
                outlineTransform.gameObject.SetActive(false);
            return;
        }

        SpriteRenderer outlineRenderer;
        if (outlineTransform == null)
        {
            var outlineObject = new GameObject(outlineName);
            outlineObject.transform.SetParent(marker, false);
            outlineObject.transform.localPosition = Vector3.zero;
            outlineObject.transform.localRotation = Quaternion.identity;
            outlineTransform = outlineObject.transform;
            outlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
        }
        else
        {
            outlineRenderer = outlineTransform.GetComponent<SpriteRenderer>();
            if (outlineRenderer == null)
                outlineRenderer = outlineTransform.gameObject.AddComponent<SpriteRenderer>();
        }

        outlineTransform.gameObject.SetActive(true);
        outlineTransform.localScale = new Vector3(_playerOutlineScale, _playerOutlineScale, 1f);

        outlineRenderer.sprite = markerRenderer.sprite;
        outlineRenderer.color = _playerOutlineColor;
        outlineRenderer.sortingLayerID = markerRenderer.sortingLayerID;
        outlineRenderer.sortingOrder = markerRenderer.sortingOrder + _playerOutlineSortingOffset;
        outlineRenderer.flipX = markerRenderer.flipX;
        outlineRenderer.flipY = markerRenderer.flipY;
        outlineRenderer.maskInteraction = markerRenderer.maskInteraction;
        outlineRenderer.drawMode = markerRenderer.drawMode;
        outlineRenderer.size = markerRenderer.size;
        outlineRenderer.sharedMaterial = markerRenderer.sharedMaterial;
    }

    void ConfigureCameras()
    {
        var listener = _floorCamera.GetComponent<AudioListener>();
        if (listener != null)
            Destroy(listener);

        if (_useDualDisplay)
        {
            if (_wallCamera.targetDisplay == _floorCamera.targetDisplay)
            {
                _wallCamera.targetDisplay = 0;
                _floorCamera.targetDisplay = 1;
            }

            for (var i = 1; i < Display.displays.Length && i < 3; i++)
                Display.displays[i].Activate();
        }

        if (_useRenderTextures)
        {
            if (_wallOutput == null)
            {
                _wallOutput = new RenderTexture(_renderTextureSize.x, _renderTextureSize.y, 24)
                {
                    name = "RT_Wall"
                };
            }

            if (_floorOutput == null)
            {
                _floorOutput = new RenderTexture(_renderTextureSize.x, _renderTextureSize.y, 24)
                {
                    name = "RT_Floor"
                };
            }

            _wallCamera.targetTexture = _wallOutput;
            _floorCamera.targetTexture = _floorOutput;
        }
        else
        {
            _wallCamera.targetTexture = null;
            _floorCamera.targetTexture = null;
        }
    }

    Vector3 MarkerWorldPosition(Vector2 mappedPos)
    {
        if (_arenaPlane == ArenaPlane.XY)
            return new Vector3(mappedPos.x, mappedPos.y, _markerPlaneValue);

        return new Vector3(mappedPos.x, _markerPlaneValue, mappedPos.y);
    }
}
