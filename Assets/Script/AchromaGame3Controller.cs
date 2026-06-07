using UnityEngine;

[AddComponentMenu("TD/Achroma/Game 3 Controller")]
public class AchromaGame3Controller : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TDTableReceiverBase _receiver;
    [SerializeField] private TDAchromaFlowManager _flowManager;

    [HideInInspector] public float _gameDurationSeconds = 90f;

    private bool  _gameRunning = false;
    private float _timeLeft    = 0f;

    public bool IsRoundRunning => _gameRunning;

    private void Awake()
    {
        if (_receiver    == null) _receiver    = FindFirstObjectByType<TDTableReceiverBase>();
        if (_flowManager == null) _flowManager = FindFirstObjectByType<TDAchromaFlowManager>();
    }

    private void Update()
    {
        if (!_gameRunning) return;

        _timeLeft -= Time.deltaTime;
        if (_timeLeft <= 0f) EndGame();
    }

    public void StartGame(bool keepPlayers = true)
    {
        if (_gameRunning) return;
        _gameRunning = true;
        _timeLeft    = _gameDurationSeconds;

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined += HandlePlayerJoined;
            _receiver.OnPlayerLeft   += HandlePlayerLeft;
        }

        // TODO: initialize game 3 state here.
        Debug.Log("[Game3] StartGame");
    }

    public void EndGame()
    {
        if (!_gameRunning) return;
        _gameRunning = false;

        if (_receiver != null)
        {
            _receiver.OnPlayerJoined -= HandlePlayerJoined;
            _receiver.OnPlayerLeft   -= HandlePlayerLeft;
        }

        // TODO: handle game 3 end here.
        Debug.Log("[Game3] EndGame");
    }

    private void HandlePlayerJoined(int slot) { }
    private void HandlePlayerLeft(int slot)   { }
}
