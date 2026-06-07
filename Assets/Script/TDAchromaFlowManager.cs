using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;
using TMPro;

[AddComponentMenu("TD/Achroma/Achroma Game Flow Manager")]
public class TDAchromaFlowManager : MonoBehaviour
{
    public enum AchromaState
    {
        Story1,
        Game1,
        Story2,
        Game2,
        Story3,
        Game3,
        Story4,
        Completed
    }

    [Header("Current State")]
    [SerializeField] private AchromaState _currentState = AchromaState.Story1;
    public AchromaState CurrentState => _currentState;

    [Header("Debug & Testing Controls")]
    [Tooltip("If checked, the game will start from this initial state on Awake/Play")]
    public bool useInitialDebugState = false;
    public AchromaState initialDebugState = AchromaState.Story1;
    
    [Tooltip("Press this key during a Story state to skip directly to the next Game state")]
    public KeyCode skipStoryKey = KeyCode.Space;
    
    [Tooltip("Press this key during a Game state to force end the round and move to the next Story")]
    public KeyCode forceEndGameKey = KeyCode.Alpha0;

    [Header("Game Durations")]
    public float game3Duration = 90f;

    [Header("Screen Managers")]
    [Tooltip("AchromaScreenManager on the Wall Screen GameObject")]
    [SerializeField] private AchromaScreenManager _wallScreen;
    [Tooltip("AchromaScreenManager on the Floor Screen GameObject")]
    [SerializeField] private AchromaScreenManager _floorScreen;

    [Header("Game Controllers (Optional)")]
    [Tooltip("If assigned, Game 1 will use this controller instead of the Core Receiver.")]
    [SerializeField] private MonoBehaviour _game1Controller;
    [Tooltip("If assigned, Game 2 will use this controller instead of the Core Receiver.")]
    [SerializeField] private MonoBehaviour _game2Controller;
    [Tooltip("If assigned, Game 3 will use this controller instead of the Core Receiver.")]
    [SerializeField] private MonoBehaviour _game3Controller;

    public enum AudioMode { UnityNative, QLab }

    [Header("Audio")]
    [Tooltip("UnityNative = use TDGameAudioManager (in-editor testing). QLab = use TDGameAudioManagerQlab (theatre playback).")]
    public AudioMode audioMode = AudioMode.QLab;
    [SerializeField] private TDGameAudioManager _audioManagerNative;
    [SerializeField] private TDGameAudioManagerQlab _audioManagerQlab;

    // Read-only accessors so game controllers can retrieve the correct manager.
    public TDGameAudioManager      AudioManagerNative => _audioManagerNative;
    public TDGameAudioManagerQlab  AudioManagerQlab   => _audioManagerQlab;
    public bool                    UseQLab            => audioMode == AudioMode.QLab;

    [Header("Core References")]
    [SerializeField] private MonoBehaviour _receiver;
    [SerializeField] private TMP_Text _debugStateText;

    private VideoPlayer _storyVideoPlayer;
    private bool _gameWasRunningLastFrame = false;

    private void Start()
    {
        if (_receiver == null)
        {
            _receiver = FindFirstObjectByType<TDTableReceiverBase>();
            if (_receiver == null) _receiver = FindFirstObjectByType<TDTableReceiver>();
            if (_receiver == null) _receiver = FindFirstObjectByType<TDTableReceiverAchroma>();

            if (_receiver == null)
            {
                Debug.LogError("[AchromaFlowManager] No TD receiver found in scene!");
                enabled = false;
                return;
            }
        }

        if (_audioManagerNative == null)
            _audioManagerNative = FindFirstObjectByType<TDGameAudioManager>();

        if (_audioManagerQlab == null)
            _audioManagerQlab = FindFirstObjectByType<TDGameAudioManagerQlab>();

        Debug.Log($"[AchromaFlowManager] Audio mode: {audioMode}");

        // Apply initial debug state if requested
        if (useInitialDebugState)
        {
            Debug.Log($"[AchromaFlowManager] Debug mode enabled. Starting from: {initialDebugState}");
            TransitionToState(initialDebugState);
        }
        else
        {
            TransitionToState(AchromaState.Story1);
        }
    }

    private void Update()
    {
        HandleDebugInputs();

        // State Machine logic
        switch (_currentState)
        {
            case AchromaState.Story1:
            case AchromaState.Story2:
            case AchromaState.Story3:
            case AchromaState.Story4:
                // Stories are static or waiting for user interaction/triggers
                // For safety, ensure all game rounds are not running
                EnsureAllGamesStopped();
                break;

            case AchromaState.Game1:
            case AchromaState.Game2:
            case AchromaState.Game3:
                UpdateGameRoundCheck();
                break;

            case AchromaState.Completed:
                break;
        }

        UpdateDebugUI();
    }

    private void HandleDebugInputs()
    {
        // Debug Key to skip story
        if (IsStoryState(_currentState))
        {
            if (Input.GetKeyDown(skipStoryKey))
            {
                Debug.Log($"[AchromaFlowManager] Debug skipping {_currentState} state.");
                AdvanceToNextState();
            }
        }

        // Debug Key to force end a game round
        if (IsGameState(_currentState))
        {
            if (Input.GetKeyDown(forceEndGameKey))
            {
                Debug.Log($"[AchromaFlowManager] Debug force ending game round for {_currentState}.");
                ForceEndGameInState(_currentState);
            }
        }
    }

    private MonoBehaviour GetControllerForState(AchromaState state)
    {
        switch (state)
         {
             case AchromaState.Game1:
                 return _game1Controller != null ? _game1Controller : _receiver;
             case AchromaState.Game2:
                 return _game2Controller != null ? _game2Controller : _receiver;
             case AchromaState.Game3:
                 return _game3Controller != null ? _game3Controller : _receiver;
             default:
                 return null;
         }
    }

    private bool IsGameRunningInState(AchromaState state)
    {
        MonoBehaviour controller = GetControllerForState(state);
        if (controller == null) return false;

        try
        {
            PropertyInfo prop = controller.GetType().GetProperty("IsRoundRunning", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return (bool)prop.GetValue(controller);
            }
            
            PropertyInfo propRunning = controller.GetType().GetProperty("IsGameRunning", BindingFlags.Public | BindingFlags.Instance);
            if (propRunning != null)
            {
                return (bool)propRunning.GetValue(controller);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AchromaFlowManager] Error reading running state from {controller.name}: {e.Message}");
        }
        return false;
    }

    private void UpdateGameRoundCheck()
    {
        bool isRunning = IsGameRunningInState(_currentState);

        // Detect transition from Running to Stopped (Game over)
        if (_gameWasRunningLastFrame && !isRunning)
        {
            Debug.Log($"[AchromaFlowManager] Game round finished for {_currentState}. Moving to next story.");
            AdvanceToNextState();
        }

        _gameWasRunningLastFrame = isRunning;
    }

    public void AdvanceToNextState()
    {
        switch (_currentState)
        {
            case AchromaState.Story1:
                TransitionToState(AchromaState.Game1);
                break;
            case AchromaState.Game1:
                TransitionToState(AchromaState.Story2);
                break;
            case AchromaState.Story2:
                TransitionToState(AchromaState.Game2);
                break;
            case AchromaState.Game2:
                TransitionToState(AchromaState.Story3);
                break;
            case AchromaState.Story3:
                TransitionToState(AchromaState.Game3);
                break;
            case AchromaState.Game3:
                TransitionToState(AchromaState.Story4);
                break;
            case AchromaState.Story4:
                TransitionToState(AchromaState.Completed);
                break;
            case AchromaState.Completed:
                break;
        }
    }

    public void TransitionToState(AchromaState newState)
    {
        Debug.Log($"[AchromaFlowManager] State Transition: {_currentState} -> {newState}");
        _currentState = newState;

        // 1. Manage visual objects
        UpdateVisualsForState(newState);

        // 2. State setup logic
        switch (newState)
        {
            case AchromaState.Story1:
            case AchromaState.Story2:
            case AchromaState.Story3:
            case AchromaState.Story4:
                _gameWasRunningLastFrame = false;
                SubscribeToStoryVideo(newState);
                break;

            case AchromaState.Game1:
                UnsubscribeFromStoryVideo();
                StartGameWithState(AchromaState.Game1);
                break;

            case AchromaState.Game2:
                UnsubscribeFromStoryVideo();
                StartGameWithState(AchromaState.Game2);
                break;

            case AchromaState.Game3:
                UnsubscribeFromStoryVideo();
                StartGameWithState(AchromaState.Game3, game3Duration);
                break;

            case AchromaState.Completed:
                UnsubscribeFromStoryVideo();
                _gameWasRunningLastFrame = false;
                Debug.Log("[AchromaFlowManager] Achroma Game Experience Completed!");
                break;
        }
    }

    private void StartGameWithState(AchromaState state, float duration = 0f)
    {
        MonoBehaviour controller = GetControllerForState(state);
        if (controller == null) return;

        Debug.Log($"[AchromaFlowManager] Starting Game State {state} with controller: {controller.name}" + (duration > 0f ? $" ({duration}s)." : "."));
        if (duration > 0f) SetControllerDuration(controller, duration);

        try
        {
            // Start game round
            // 1. Try StartGame(bool keepPlayers)
            MethodInfo methodWithBool = controller.GetType().GetMethod("StartGame", new Type[] { typeof(bool) });
            if (methodWithBool != null)
            {
                methodWithBool.Invoke(controller, new object[] { true });
            }
            else
            {
                // 2. Try StartGame()
                MethodInfo methodNoParam = controller.GetType().GetMethod("StartGame", Type.EmptyTypes);
                if (methodNoParam != null)
                {
                    methodNoParam.Invoke(controller, null);
                }
                else
                {
                    // 3. Try PlayGame()
                    MethodInfo methodPlay = controller.GetType().GetMethod("PlayGame", Type.EmptyTypes);
                    if (methodPlay != null)
                    {
                        methodPlay.Invoke(controller, null);
                    }
                    else
                    {
                        Debug.LogWarning($"[AchromaFlowManager] Could not find StartGame(bool), StartGame(), or PlayGame() in {controller.name}.");
                    }
                }
            }
            _gameWasRunningLastFrame = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[AchromaFlowManager] Error starting game on {controller.name}: {e.Message}");
        }
    }

    private void SetControllerDuration(MonoBehaviour controller, float duration)
    {
        try
        {
            FieldInfo field = controller.GetType().GetField("_gameDurationSeconds", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(controller, duration);
                return;
            }

            PropertyInfo prop = controller.GetType().GetProperty("GameDuration", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                prop.SetValue(controller, duration);
                return;
            }

            FieldInfo durationField = controller.GetType().GetField("duration", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (durationField != null)
            {
                durationField.SetValue(controller, duration);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AchromaFlowManager] Error setting duration on {controller.name}: {e.Message}");
        }
    }

    private void ForceEndGameInState(AchromaState state)
    {
        MonoBehaviour controller = GetControllerForState(state);
        if (controller == null) return;

        try
        {
            FieldInfo field = controller.GetType().GetField("_timeLeft", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(controller, 0f);
                return;
            }

            MethodInfo methodEnd = controller.GetType().GetMethod("EndGame", Type.EmptyTypes);
            if (methodEnd != null)
            {
                methodEnd.Invoke(controller, null);
                return;
            }

            MethodInfo methodStop = controller.GetType().GetMethod("StopGame", Type.EmptyTypes);
            if (methodStop != null)
            {
                methodStop.Invoke(controller, null);
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[AchromaFlowManager] Error force ending game on {controller.name}: {e.Message}");
        }
    }

    private void EnsureAllGamesStopped()
    {
        if (_receiver != null)
        {
            try
            {
                PropertyInfo prop = _receiver.GetType().GetProperty("IsRoundRunning", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && (bool)prop.GetValue(_receiver))
                {
                    ForceStopGameForController(_receiver);
                }
            }
            catch {}
        }

        StopControllerIfRunning(_game1Controller);
        StopControllerIfRunning(_game2Controller);
        StopControllerIfRunning(_game3Controller);
    }

    private void StopControllerIfRunning(MonoBehaviour controller)
    {
        if (controller == null || controller == _receiver) return;
        try
        {
            PropertyInfo prop = controller.GetType().GetProperty("IsRoundRunning", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && (bool)prop.GetValue(controller))
            {
                ForceStopGameForController(controller);
            }
        }
        catch {}
    }

    private void ForceStopGameForController(MonoBehaviour controller)
    {
        if (controller == null) return;
        try
        {
            FieldInfo runningField = controller.GetType().GetField("_gameRunning", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (runningField != null)
            {
                runningField.SetValue(controller, false);
            }
            FieldInfo audioField = controller.GetType().GetField("_countdownAudioActive", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (audioField != null)
            {
                audioField.SetValue(controller, false);
            }
        }
        catch {}
    }

    private void UpdateVisualsForState(AchromaState state)
    {
        _wallScreen?.ShowState(state);
        _floorScreen?.ShowState(state);
    }

    private void SubscribeToStoryVideo(AchromaState state)
    {
        UnsubscribeFromStoryVideo();
        var content = _wallScreen?.GetContentForState(state) ?? _floorScreen?.GetContentForState(state);
        if (content == null) return;
        _storyVideoPlayer = content.GetComponentInChildren<VideoPlayer>();
        if (_storyVideoPlayer != null)
            _storyVideoPlayer.loopPointReached += OnStoryVideoFinished;
    }

    private void UnsubscribeFromStoryVideo()
    {
        if (_storyVideoPlayer == null) return;
        _storyVideoPlayer.loopPointReached -= OnStoryVideoFinished;
        _storyVideoPlayer = null;
    }

    private void OnStoryVideoFinished(VideoPlayer vp)
    {
        UnsubscribeFromStoryVideo();
        AdvanceToNextState();
    }

    private bool IsStoryState(AchromaState state)
    {
        return state == AchromaState.Story1 ||
               state == AchromaState.Story2 ||
               state == AchromaState.Story3 ||
               state == AchromaState.Story4;
    }

    private bool IsGameState(AchromaState state)
    {
        return state == AchromaState.Game1 ||
               state == AchromaState.Game2 ||
               state == AchromaState.Game3;
    }

    private void UpdateDebugUI()
    {
        if (_debugStateText != null)
        {
            _debugStateText.text = $"State: {_currentState}";
        }
    }

    private void UpdateDebugUIColor()
    {
        if (_debugStateText != null)
        {
            _debugStateText.color = IsGameState(_currentState) ? Color.green : Color.yellow;
        }
    }
}
