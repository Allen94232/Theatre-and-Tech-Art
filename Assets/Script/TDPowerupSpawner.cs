using UnityEngine;
using System.Collections.Generic;

[AddComponentMenu("TD/Powerups/Powerup Spawner")]
public class TDPowerupSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] TDTableReceiver _receiver;
    [SerializeField] TDGameAudioManager _audioManager;
    [SerializeField] Transform _spawnRoot;

    [Header("Prefabs")]
    [SerializeField] TDPowerupPickup[] _powerupPrefabs;

    [Header("Spawn")]
    [SerializeField] float _spawnIntervalMin = 8f;
    [SerializeField] float _spawnIntervalMax = 15f;
    [SerializeField] float _edgePadding = 0.6f;
    [SerializeField] bool _avoidSpawnPoints = true;
    [SerializeField] float _spawnPointAvoidRadius = 1.25f;

    [Header("Pickup")]
    [SerializeField] bool _expandPickupRectByPlayerRadius = true;

    readonly List<TDPowerupPickup> _activePowerups = new List<TDPowerupPickup>(8);
    float _spawnTimer;
    bool _hasLastSpawnedKind;
    TDPowerupKind _lastSpawnedKind;
    bool _wasSpawningAllowed;

    void Awake()
    {
        if (_spawnRoot == null)
            _spawnRoot = transform;

        ResetSpawnTimer();
    }

    void Update()
    {
        if (_receiver == null || !_receiver.IsConfigured)
            return;

        CleanupMissingPowerups();
        var canSpawn = CanSpawnNow();
        if (!canSpawn)
        {
            if (_wasSpawningAllowed)
            {
                ClearAllPowerups();
                ResetSpawnTimer();
            }

            _wasSpawningAllowed = false;
            return;
        }

        if (!_wasSpawningAllowed)
        {
            _wasSpawningAllowed = true;
            ResetSpawnTimer();
        }

        TryCollectActivePowerups();

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
            SpawnOnePowerup();
    }

    bool CanSpawnNow()
    {
        return _receiver.IsRoundRunning;
    }

    void SpawnOnePowerup()
    {
        if (_powerupPrefabs == null || _powerupPrefabs.Length == 0)
        {
            ResetSpawnTimer();
            return;
        }

        if (!_receiver.TryGetArenaBounds(out var min, out var max))
        {
            ResetSpawnTimer();
            return;
        }

        var prefabIndex = SelectPrefabIndex();
        if (prefabIndex < 0)
        {
            ResetSpawnTimer();
            return;
        }

        var prefab = _powerupPrefabs[prefabIndex];
        if (prefab == null)
        {
            ResetSpawnTimer();
            return;
        }

        const int maxAttempts = 20;
        Vector2 spawnPos = Vector2.zero;
        var found = false;

        for (var i = 0; i < maxAttempts; i++)
        {
            var x = Random.Range(min.x + _edgePadding, max.x - _edgePadding);
            var y = Random.Range(min.y + _edgePadding, max.y - _edgePadding);
            var candidate = new Vector2(x, y);

            if (_avoidSpawnPoints && IsNearAnySpawnPoint(candidate, _spawnPointAvoidRadius))
                continue;

            spawnPos = candidate;
            found = true;
            break;
        }

        if (!found)
        {
            spawnPos = (min + max) * 0.5f;
        }

        var instance = Instantiate(prefab, _spawnRoot);
        instance.Place(_receiver.ArenaToWorldPosition(spawnPos), spawnPos);
        _activePowerups.Add(instance);
        _hasLastSpawnedKind = true;
        _lastSpawnedKind = instance.Kind;
        ResetSpawnTimer();
    }

    int SelectPrefabIndex()
    {
        var fallback = -1;
        var candidates = new List<int>(_powerupPrefabs.Length);

        for (var i = 0; i < _powerupPrefabs.Length; i++)
        {
            var prefab = _powerupPrefabs[i];
            if (prefab == null)
                continue;

            if (fallback < 0)
                fallback = i;

            if (_hasLastSpawnedKind && prefab.Kind == _lastSpawnedKind)
                continue;

            candidates.Add(i);
        }

        if (candidates.Count > 0)
            return candidates[Random.Range(0, candidates.Count)];

        return fallback;
    }

    bool IsNearAnySpawnPoint(Vector2 candidate, float avoidRadius)
    {
        var avoidRadiusSqr = avoidRadius * avoidRadius;

        for (var i = 0; i < 4; i++)
        {
            if (!_receiver.TryGetSpawnPoint(i, out var spawnPos))
                continue;

            if ((candidate - spawnPos).sqrMagnitude <= avoidRadiusSqr)
                return true;
        }

        return false;
    }

    void TryCollectActivePowerups()
    {
        if (_activePowerups.Count == 0)
            return;

        for (var pickupIndex = _activePowerups.Count - 1; pickupIndex >= 0; pickupIndex--)
        {
            var pickup = _activePowerups[pickupIndex];
            if (pickup == null)
            {
                _activePowerups.RemoveAt(pickupIndex);
                continue;
            }

            if (!TryCollectSinglePowerup(pickup))
                continue;

            Destroy(pickup.gameObject);
            _activePowerups.RemoveAt(pickupIndex);
        }
    }

    bool TryCollectSinglePowerup(TDPowerupPickup pickup)
    {
        var pickupCenter = pickup.ArenaPosition;
        var pickupRect = pickup.PickupRectSize;
        var halfRect = pickupRect * 0.5f;

        for (var player = 0; player < 4; player++)
        {
            if (!_receiver.TryGetPlayerInfo(player, out var playerPosArena, out var playerRadius))
                continue;

            var expand = _expandPickupRectByPlayerRadius ? playerRadius : 0f;
            var dx = Mathf.Abs(playerPosArena.x - pickupCenter.x);
            var dy = Mathf.Abs(playerPosArena.y - pickupCenter.y);
            if (dx > halfRect.x + expand || dy > halfRect.y + expand)
                continue;

            var kind = pickup.Kind;
            pickup.Apply(_receiver, player);
            if (_audioManager != null)
                _audioManager.PlayPickupSfx(kind);

            return true;
        }

        return false;
    }

    void CleanupMissingPowerups()
    {
        for (var i = _activePowerups.Count - 1; i >= 0; i--)
        {
            if (_activePowerups[i] == null)
                _activePowerups.RemoveAt(i);
        }
    }

    void ClearAllPowerups()
    {
        for (var i = _activePowerups.Count - 1; i >= 0; i--)
        {
            var powerup = _activePowerups[i];
            if (powerup != null)
                Destroy(powerup.gameObject);
        }

        _activePowerups.Clear();
    }

    void ResetSpawnTimer()
    {
        var min = Mathf.Max(0.1f, _spawnIntervalMin);
        var max = Mathf.Max(min, _spawnIntervalMax);
        _spawnTimer = Random.Range(min, max);
    }
}
