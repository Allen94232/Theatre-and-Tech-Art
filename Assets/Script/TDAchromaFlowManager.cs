using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

[System.Serializable]
public struct TransitionFade
{
    [Tooltip("Seconds to fade to black before content switches")]
    [Min(0f)] public float fadeOut;
    [Tooltip("Seconds to hold on black between fade-out and fade-in. Use this to let heavy initializations (e.g. Game 2 texture preload) finish before revealing content.")]
    [Min(0f)] public float blackHold;
    [Tooltip("Seconds to fade from black after content switches")]
    [Min(0f)] public float fadeIn;
}

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

    [Header("Fade Overlays")]
    [Tooltip("Full-screen black Image on the wall canvas. Must render above all other wall content.")]
    [SerializeField] private Image _wallFadeOverlay;
    [Tooltip("Full-screen black Image on the floor canvas. Must render above all other floor content.")]
    [SerializeField] private Image _floorFadeOverlay;

    [Header("Transition Fades")]
    [Tooltip("Fade-in duration when the experience first starts (fades from black — no fade-out on initial load)")]
    [SerializeField] [Min(0f)] private float _initialFadeIn = 1f;
    [SerializeField] private TransitionFade _fadeStory1ToGame1     = new TransitionFade { fadeOut = 0.5f, fadeIn = 0.5f };
    [SerializeField] private TransitionFade _fadeGame1ToStory2     = new TransitionFade { fadeOut = 0.5f, fadeIn = 0.5f };
    [SerializeField] private TransitionFade _fadeStory2ToGame2     = new TransitionFade { fadeOut = 0.5f, fadeIn = 0.5f };
    [SerializeField] private TransitionFade _fadeGame2ToStory3     = new TransitionFade { fadeOut = 0.5f, fadeIn = 0.5f };
    [SerializeField] private TransitionFade _fadeStory3ToGame3     = new TransitionFade { fadeOut = 0.5f, fadeIn = 0.5f };
    [SerializeField] private TransitionFade _fadeGame3ToStory4     = new TransitionFade { fadeOut = 0.5f, fadeIn = 0.5f };
    [SerializeField] private TransitionFade _fadeStory4ToCompleted = new TransitionFade { fadeOut = 0.5f, fadeIn = 0f   };

    [Header("Game Controllers (Optional)")]
    [Tooltip("If assigned, Game 1 will use this controller instead of the Core Receiver.")]
    [SerializeField] private MonoBehaviour _game1Controller;
    [Tooltip("If assigned, Game 2 will use this controller instead of the Core Receiver.")]
    [SerializeField] private MonoBehaviour _game2Controller;
    [Tooltip("If assigned, Game 3 will use this controller instead of the Core Receiver.")]
    [SerializeField] private MonoBehaviour _game3Controller;

    public enum AudioMode { UnityNative, QLab }

    [Header("Audio")]
    [Tooltip("UnityNative = local AudioClips for testing. QLab = OSC to QLab for theatre.")]
    public AudioMode audioMode = AudioMode.QLab;
    [SerializeField] private AchromaAudioManager _audioManager;
    public AchromaAudioManager Audio => _audioManager;

    [Header("Video Render Textures")]
    [Tooltip("RenderTextures used by story VideoPlayers. Cleared to black on each transition so the previous story's last frame never bleeds through.")]
    [SerializeField] private RenderTexture[] _videoRenderTextures;

    [Header("Core References")]
    [SerializeField] private MonoBehaviour _receiver;
    [SerializeField] private TMP_Text _debugStateText;

    private VideoPlayer[] _storyVideoPlayers;
    private int           _storyVideoFinishedCount;
    private bool          _gameWasRunningLastFrame = false;
    private bool          _isTransitioning         = false;
    private bool          _isFirstTransition       = true;
    private Coroutine     _transitionCoroutine;

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

        if (_audioManager == null)
            _audioManager = FindFirstObjectByType<AchromaAudioManager>();

        if (_audioManager != null)
            _audioManager.SetMode(audioMode == AudioMode.QLab);

        Debug.Log($"[AchromaFlowManager] Audio mode: {audioMode}");

        // Start fully black; initial fade-in will reveal content.
        SetOverlayAlpha(1f);

        AchromaState startState = useInitialDebugState ? initialDebugState : AchromaState.Story1;
        if (useInitialDebugState)
            Debug.Log($"[AchromaFlowManager] Debug mode enabled. Starting from: {startState}");

        TransitionToState(startState);
    }

    private void Update()
    {
        if (_isTransitioning) return;

        HandleDebugInputs();

        switch (_currentState)
        {
            case AchromaState.Story1:
            case AchromaState.Story2:
            case AchromaState.Story3:
            case AchromaState.Story4:
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
        if (IsStoryState(_currentState) && Input.GetKeyDown(skipStoryKey))
        {
            Debug.Log($"[AchromaFlowManager] Debug skipping {_currentState} state.");
            AdvanceToNextState();
        }

        if (IsGameState(_currentState) && Input.GetKeyDown(forceEndGameKey))
        {
            Debug.Log($"[AchromaFlowManager] Debug force ending game round for {_currentState}.");
            ForceEndGameInState(_currentState);
        }
    }

    public void AdvanceToNextState()
    {
        switch (_currentState)
        {
            case AchromaState.Story1:    TransitionToState(AchromaState.Game1);      break;
            case AchromaState.Game1:     TransitionToState(AchromaState.Story2);     break;
            case AchromaState.Story2:    TransitionToState(AchromaState.Game2);      break;
            case AchromaState.Game2:     TransitionToState(AchromaState.Story3);     break;
            case AchromaState.Story3:    TransitionToState(AchromaState.Game3);      break;
            case AchromaState.Game3:     TransitionToState(AchromaState.Story4);     break;
            case AchromaState.Story4:    TransitionToState(AchromaState.Completed);  break;
            case AchromaState.Completed: break;
        }
    }

    // ── Transition Coroutine ───────────────────────────────────────────────────

    public void TransitionToState(AchromaState newState)
    {
        if (_transitionCoroutine != null) StopCoroutine(_transitionCoroutine);
        _transitionCoroutine = StartCoroutine(TransitionCo(newState));
    }

    private IEnumerator TransitionCo(AchromaState newState)
    {
        _isTransitioning = true;

        TransitionFade fade = _isFirstTransition
            ? new TransitionFade { fadeOut = 0f, fadeIn = _initialFadeIn }
            : GetFadeForTransition(_currentState, newState);
        _isFirstTransition = false;

        // Fade to black — simultaneously fade out content CanvasGroup and fade in overlay
        if (fade.fadeOut > 0f)
        {
            _wallScreen?.FadeOutActiveContent(fade.fadeOut);
            _floorScreen?.FadeOutActiveContent(fade.fadeOut);
            yield return StartCoroutine(SetOverlayAlphaCo(0f, 1f, fade.fadeOut));
        }
        else
            SetOverlayAlpha(1f);

        // Switch content while screen is black (ShowStateInstant sets new content alpha to 0)
        ApplyTransition(newState);
        yield return null; // one frame for Unity to activate new content

        // Clear shared video RenderTextures to black so the previous story's last frame
        // doesn't bleed through while the new VideoPlayer decodes its first frame.
        ClearVideoTextures();

        // Hold on black — lets async initializations (e.g. Game 2 texture preload) finish before reveal
        if (fade.blackHold > 0f)
            yield return new WaitForSeconds(fade.blackHold);

        // Initialize game before fade-in so all UI (progress, hints, level text) is set up
        // when content first becomes visible. The overlay is still fully black at this point.
        if (IsGameState(newState))
            StartGameWithState(newState, newState == AchromaState.Game3 ? game3Duration : 0f);

        // Fade from black — simultaneously fade in content CanvasGroup and fade out overlay
        if (fade.fadeIn > 0f)
        {
            _wallScreen?.FadeInActiveContent(fade.fadeIn);
            _floorScreen?.FadeInActiveContent(fade.fadeIn);
            yield return StartCoroutine(SetOverlayAlphaCo(1f, 0f, fade.fadeIn));
        }
        else
        {
            _wallScreen?.SetActiveContentAlpha(1f);
            _floorScreen?.SetActiveContentAlpha(1f);
            SetOverlayAlpha(0f);
        }

        _isTransitioning   = false;
        _transitionCoroutine = null;
    }

    private void ApplyTransition(AchromaState newState)
    {
        Debug.Log($"[AchromaFlowManager] State Transition: {_currentState} -> {newState}");
        _currentState = newState;

        (_receiver as TDTableReceiverBase)?.SetPlayersVisible(IsGameState(newState));

        // Switch screen content instantly (screen is black, no visual pop)
        _wallScreen?.ShowStateInstant(newState);
        _floorScreen?.ShowStateInstant(newState);

        switch (newState)
        {
            case AchromaState.Story1:
            case AchromaState.Story2:
            case AchromaState.Story3:
            case AchromaState.Story4:
                _audioManager?.StopAll();
                _audioManager?.Story_OnEnter(newState);
                _gameWasRunningLastFrame = false;
                SubscribeToStoryVideo(newState);
                break;

            case AchromaState.Game1:
                _audioManager?.Story_OnExit(AchromaState.Story1);
                _audioManager?.Game1_OnGameStart();   // BGM starts during black screen so music is present on fade-in
                UnsubscribeFromStoryVideo();
                break;

            case AchromaState.Game2:
                _audioManager?.Story_OnExit(AchromaState.Story2);
                _audioManager?.Game2_OnGameStart();   // BGM starts during black screen
                UnsubscribeFromStoryVideo();
                break;

            case AchromaState.Game3:
                _audioManager?.Story_OnExit(AchromaState.Story3);
                _audioManager?.Game3_OnRepairPhase(); // repair BGM starts during black screen; sting plays later in StartGame
                UnsubscribeFromStoryVideo();
                break;

            case AchromaState.Completed:
                UnsubscribeFromStoryVideo();
                _audioManager?.StopAll();
                _gameWasRunningLastFrame = false;
                Debug.Log("[AchromaFlowManager] Achroma Game Experience Completed!");
                break;
        }
    }

    private TransitionFade GetFadeForTransition(AchromaState from, AchromaState to) =>
        (from, to) switch
        {
            (AchromaState.Story1, AchromaState.Game1)      => _fadeStory1ToGame1,
            (AchromaState.Game1,  AchromaState.Story2)     => _fadeGame1ToStory2,
            (AchromaState.Story2, AchromaState.Game2)      => _fadeStory2ToGame2,
            (AchromaState.Game2,  AchromaState.Story3)     => _fadeGame2ToStory3,
            (AchromaState.Story3, AchromaState.Game3)      => _fadeStory3ToGame3,
            (AchromaState.Game3,  AchromaState.Story4)     => _fadeGame3ToStory4,
            (AchromaState.Story4, AchromaState.Completed)  => _fadeStory4ToCompleted,
            _                                               => new TransitionFade { fadeOut = 0.4f, fadeIn = 0.4f }
        };

    // ── Overlay helpers ───────────────────────────────────────────────────────

    private void SetOverlayAlpha(float a)
    {
        if (_wallFadeOverlay  != null) _wallFadeOverlay.color  = new Color(0f, 0f, 0f, a);
        if (_floorFadeOverlay != null) _floorFadeOverlay.color = new Color(0f, 0f, 0f, a);
    }

    private IEnumerator SetOverlayAlphaCo(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetOverlayAlpha(to);
    }

    // ── Game round helpers ────────────────────────────────────────────────────

    private MonoBehaviour GetControllerForState(AchromaState state)
    {
        switch (state)
        {
            case AchromaState.Game1: return _game1Controller != null ? _game1Controller : _receiver;
            case AchromaState.Game2: return _game2Controller != null ? _game2Controller : _receiver;
            case AchromaState.Game3: return _game3Controller != null ? _game3Controller : _receiver;
            default: return null;
        }
    }

    private bool IsGameRunningInState(AchromaState state)
    {
        MonoBehaviour controller = GetControllerForState(state);
        if (controller == null) return false;

        try
        {
            PropertyInfo prop = controller.GetType().GetProperty("IsRoundRunning", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) return (bool)prop.GetValue(controller);

            PropertyInfo propRunning = controller.GetType().GetProperty("IsGameRunning", BindingFlags.Public | BindingFlags.Instance);
            if (propRunning != null) return (bool)propRunning.GetValue(controller);
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

        if (_gameWasRunningLastFrame && !isRunning)
        {
            Debug.Log($"[AchromaFlowManager] Game round finished for {_currentState}. Moving to next story.");
            AdvanceToNextState();
        }

        _gameWasRunningLastFrame = isRunning;
    }

    private void StartGameWithState(AchromaState state, float duration = 0f)
    {
        MonoBehaviour controller = GetControllerForState(state);
        if (controller == null) return;

        Debug.Log($"[AchromaFlowManager] Starting Game State {state} with controller: {controller.name}" +
                  (duration > 0f ? $" ({duration}s)." : "."));

        if (duration > 0f) SetControllerDuration(controller, duration);

        try
        {
            MethodInfo methodWithBool = controller.GetType().GetMethod("StartGame", new Type[] { typeof(bool) });
            if (methodWithBool != null)
            {
                methodWithBool.Invoke(controller, new object[] { true });
            }
            else
            {
                MethodInfo methodNoParam = controller.GetType().GetMethod("StartGame", Type.EmptyTypes);
                if (methodNoParam != null)
                {
                    methodNoParam.Invoke(controller, null);
                }
                else
                {
                    MethodInfo methodPlay = controller.GetType().GetMethod("PlayGame", Type.EmptyTypes);
                    if (methodPlay != null)
                        methodPlay.Invoke(controller, null);
                    else
                        Debug.LogWarning($"[AchromaFlowManager] Could not find StartGame(bool), StartGame(), or PlayGame() in {controller.name}.");
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
            if (field != null) { field.SetValue(controller, duration); return; }

            PropertyInfo prop = controller.GetType().GetProperty("GameDuration", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) { prop.SetValue(controller, duration); return; }

            FieldInfo durationField = controller.GetType().GetField("duration", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (durationField != null) durationField.SetValue(controller, duration);
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
            if (field != null) { field.SetValue(controller, 0f); return; }

            MethodInfo methodEnd = controller.GetType().GetMethod("EndGame", Type.EmptyTypes);
            if (methodEnd != null) { methodEnd.Invoke(controller, null); return; }

            MethodInfo methodStop = controller.GetType().GetMethod("StopGame", Type.EmptyTypes);
            if (methodStop != null) methodStop.Invoke(controller, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[AchromaFlowManager] Error force ending game on {controller.name}: {e.Message}");
        }
    }

    // ── Cleanup helpers ───────────────────────────────────────────────────────

    private void EnsureAllGamesStopped()
    {
        if (_receiver != null)
        {
            try
            {
                PropertyInfo prop = _receiver.GetType().GetProperty("IsRoundRunning", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && (bool)prop.GetValue(_receiver))
                    ForceStopGameForController(_receiver);
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
                ForceStopGameForController(controller);
        }
        catch {}
    }

    private void ForceStopGameForController(MonoBehaviour controller)
    {
        if (controller == null) return;
        try
        {
            MethodInfo endGame = controller.GetType().GetMethod("EndGame", Type.EmptyTypes);
            if (endGame != null) { endGame.Invoke(controller, null); return; }

            FieldInfo runningField = controller.GetType().GetField("_gameRunning", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (runningField != null) runningField.SetValue(controller, false);

            FieldInfo audioField = controller.GetType().GetField("_countdownAudioActive", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (audioField != null) audioField.SetValue(controller, false);
        }
        catch {}
    }

    // ── Story video helpers ───────────────────────────────────────────────────

    private void SubscribeToStoryVideo(AchromaState state)
    {
        UnsubscribeFromStoryVideo();

        var players = new System.Collections.Generic.List<VideoPlayer>();

        var wallContent  = _wallScreen?.GetContentForState(state);
        var floorContent = _floorScreen?.GetContentForState(state);

        if (wallContent != null)
        {
            var vp = wallContent.GetComponentInChildren<VideoPlayer>();
            if (vp != null) players.Add(vp);
        }
        if (floorContent != null && floorContent != wallContent)
        {
            var vp = floorContent.GetComponentInChildren<VideoPlayer>();
            if (vp != null && !players.Contains(vp)) players.Add(vp);
        }

        _storyVideoPlayers       = players.ToArray();
        _storyVideoFinishedCount = 0;

        foreach (var vp in _storyVideoPlayers)
            vp.loopPointReached += OnStoryVideoFinished;

        if (_storyVideoPlayers.Length > 1)
            Debug.Log($"[AchromaFlowManager] Subscribed to {_storyVideoPlayers.Length} video players for {state}. Transition fires when all finish.");
    }

    private void UnsubscribeFromStoryVideo()
    {
        if (_storyVideoPlayers == null) return;
        foreach (var vp in _storyVideoPlayers)
            if (vp != null) vp.loopPointReached -= OnStoryVideoFinished;
        _storyVideoPlayers       = null;
        _storyVideoFinishedCount = 0;
    }

    private void OnStoryVideoFinished(VideoPlayer vp)
    {
        _storyVideoFinishedCount++;
        if (_storyVideoFinishedCount >= (_storyVideoPlayers?.Length ?? 1))
        {
            UnsubscribeFromStoryVideo();
            AdvanceToNextState();
        }
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private void ClearVideoTextures()
    {
        if (_videoRenderTextures == null) return;
        var prev = RenderTexture.active;
        foreach (var rt in _videoRenderTextures)
        {
            if (rt == null) continue;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
        }
        RenderTexture.active = prev;
    }

    private bool IsStoryState(AchromaState state) =>
        state == AchromaState.Story1 || state == AchromaState.Story2 ||
        state == AchromaState.Story3 || state == AchromaState.Story4;

    private bool IsGameState(AchromaState state) =>
        state == AchromaState.Game1 || state == AchromaState.Game2 || state == AchromaState.Game3;

    private void UpdateDebugUI()
    {
        if (_debugStateText != null)
            _debugStateText.text = $"State: {_currentState}";
    }
}
