using UnityEngine;

[DisallowMultipleComponent]
public class LevelTimerSystem : MonoBehaviour
{
    private const float BonusDisplayDuration = 1.35f;

    [SerializeField] private bool timerEnabled = true;
    [SerializeField] private float startingTimeSeconds = 30f;
    [SerializeField] private bool startAfterSeatOccupied = true;
    [SerializeField] private bool pauseGameWhenTimeEnds = true;
    [SerializeField] private bool showHud = true;
    [SerializeField] private bool showGameOverMenu = true;
    [SerializeField] private string gameOverTitle = "Sure Bitti!";
    [SerializeField] private string gameOverSubtitle = "Parkuru zamaninda tamamlayamadin.";
    [SerializeField] private Vector2 hudSize = new Vector2(220f, 78f);
    [SerializeField] private Vector2 hudMargin = new Vector2(24f, 24f);

    private static LevelTimerSystem activeTimer;

    private float remainingTime;
    private bool isRunning;
    private bool hasExpired;
    private bool levelCompleted;
    private float recentAddedTime;
    private float recentAddedTimeDisplay;
    private bool resultCursorCaptured;
    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;

    public static LevelTimerSystem ActiveTimer
    {
        get
        {
            if (activeTimer == null)
            {
                activeTimer = FindFirstObjectByType<LevelTimerSystem>();
            }

            return activeTimer;
        }
    }

    public float RemainingTime => remainingTime;

    public bool IsRunning => isRunning;

    public bool HasExpired => hasExpired;

    public static void ResetTimerState()
    {
        activeTimer = null;
        Time.timeScale = 1f;

        LevelTimerSystem[] timers = FindObjectsByType<LevelTimerSystem>(FindObjectsSortMode.None);
        foreach (LevelTimerSystem timer in timers)
        {
            if (timer != null)
            {
                timer.ResetStateInternal();
            }
        }

        LevelCheckpoint[] checkpoints = FindObjectsByType<LevelCheckpoint>(FindObjectsSortMode.None);
        LevelCheckpoint firstActiveCheckpoint = null;
        LevelCheckpoint fallbackCheckpoint = null;

        foreach (LevelCheckpoint checkpoint in checkpoints)
        {
            if (checkpoint == null)
            {
                continue;
            }

            checkpoint.ResetCheckpointState();

            if (fallbackCheckpoint == null)
            {
                fallbackCheckpoint = checkpoint;
            }

            if (firstActiveCheckpoint == null && checkpoint.StartsActive)
            {
                firstActiveCheckpoint = checkpoint;
            }
        }

        if (firstActiveCheckpoint == null)
        {
            firstActiveCheckpoint = fallbackCheckpoint;
        }

        foreach (LevelCheckpoint checkpoint in checkpoints)
        {
            if (checkpoint != null)
            {
                checkpoint.SetCheckpointActive(checkpoint == firstActiveCheckpoint);
            }
        }

        VehicleFinishLineSystem.RefreshObjectiveStates();
    }

    public static void NotifyLevelCompleted()
    {
        LevelTimerSystem timer = ActiveTimer;
        if (timer != null)
        {
            timer.levelCompleted = true;
            timer.isRunning = false;
        }
    }

    private void Awake()
    {
        activeTimer = this;
        LevelProgressionManager.EnsureActiveLevelForScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        ResetStateInternal();
    }

    private void OnEnable()
    {
        activeTimer = this;
    }

    private void OnDisable()
    {
        RestoreCursorAfterResultMenu();
        if (activeTimer == this)
        {
            activeTimer = null;
        }
    }

    private void OnValidate()
    {
        startingTimeSeconds = Mathf.Max(1f, startingTimeSeconds);
        hudSize.x = Mathf.Max(160f, hudSize.x);
        hudSize.y = Mathf.Max(64f, hudSize.y);
    }

    private void Update()
    {
        if (recentAddedTimeDisplay > 0f)
        {
            recentAddedTimeDisplay = Mathf.Max(0f, recentAddedTimeDisplay - Time.unscaledDeltaTime);
        }

        if (!timerEnabled || levelCompleted)
        {
            return;
        }

        if (hasExpired)
        {
            ReleaseCursorForResultMenu();
            return;
        }

        if (!isRunning)
        {
            if (!startAfterSeatOccupied || HasAnyOccupiedSeat())
            {
                isRunning = true;
            }

            return;
        }

        remainingTime = Mathf.Max(0f, remainingTime - Time.deltaTime);
        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            hasExpired = true;
            isRunning = false;
            ReleaseCursorForResultMenu();

            if (pauseGameWhenTimeEnds)
            {
                Time.timeScale = 0f;
            }
        }
    }

    private void OnGUI()
    {
        if (!timerEnabled)
        {
            return;
        }

        if (showHud)
        {
            DrawTimerHud();
        }

        if (hasExpired && showGameOverMenu)
        {
            DrawGameOverMenu();
        }
    }

    private void DrawTimerHud()
    {
        Rect panelRect = new Rect(
            (Screen.width - hudSize.x) * 0.5f,
            Mathf.Max(0f, hudMargin.y),
            hudSize.x,
            hudSize.y);

        GUIStyle panelStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 12
        };

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };
        headerStyle.normal.textColor = Color.white;

        GUIStyle valueStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 28,
            fontStyle = FontStyle.Bold
        };

        Color timerColor = Color.white;
        if (hasExpired)
        {
            timerColor = new Color(1f, 0.45f, 0.45f);
        }
        else if (remainingTime <= 5f)
        {
            timerColor = new Color(1f, 0.82f, 0.3f);
        }

        valueStyle.normal.textColor = timerColor;

        GUIStyle infoStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerCenter,
            fontSize = 12
        };
        infoStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

        GUI.Box(panelRect, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 6f, panelRect.width, 18f), "Checkpoint Suresi", headerStyle);
        GUI.Label(new Rect(panelRect.x, panelRect.y + 18f, panelRect.width, 34f), Mathf.CeilToInt(remainingTime).ToString(), valueStyle);

        string infoText = string.Empty;
        if (hasExpired)
        {
            infoText = "Sure bitti";
        }
        else if (!isRunning)
        {
            infoText = startAfterSeatOccupied ? "Koltuga oturunca baslar" : "Hazir";
        }

        if (!string.IsNullOrEmpty(infoText))
        {
            GUI.Label(new Rect(panelRect.x + 8f, panelRect.y + panelRect.height - 24f, panelRect.width - 16f, 18f), infoText, infoStyle);
        }

        if (recentAddedTimeDisplay > 0f && recentAddedTime > 0f)
        {
            GUIStyle bonusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            bonusStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);

            GUI.Label(
                new Rect(panelRect.x, panelRect.y + panelRect.height + 4f, panelRect.width, 20f),
                $"+{Mathf.CeilToInt(recentAddedTime)} sn",
                bonusStyle);
        }
    }

    private void DrawGameOverMenu()
    {
        int previousDepth = GUI.depth;
        GUI.depth = -100;

        float panelWidth = Mathf.Min(520f, Screen.width - 40f);
        float panelHeight = 270f;
        Rect panelRect = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth,
            panelHeight);

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
        GUI.color = previousColor;
        GUI.Box(panelRect, GUIContent.none);

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * 0.03f, 25f, 38f)),
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.45f, 0.4f) }
        };

        GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * 0.015f, 14f, 20f)),
            normal = { textColor = new Color(0.92f, 0.92f, 0.92f) }
        };

        int activeLevel = LevelProgressionManager.GetActiveLevelNumber();
        string levelName = LevelProgressionManager.GetLevelDisplayName(activeLevel);
        GUI.Label(new Rect(panelRect.x + 20f, panelRect.y + 22f, panelRect.width - 40f, 50f), gameOverTitle, titleStyle);
        GUI.Label(
            new Rect(panelRect.x + 30f, panelRect.y + 72f, panelRect.width - 60f, 58f),
            $"{levelName}\n{gameOverSubtitle}",
            subtitleStyle);

        float buttonWidth = panelRect.width - 80f;
        float buttonHeight = 42f;
        float buttonX = panelRect.x + 40f;
        float firstButtonY = panelRect.y + 142f;
        bool canRestart = !IsMultiplayerClientOnly();

        bool previousEnabled = GUI.enabled;
        GUI.enabled = canRestart;
        string restartLabel = canRestart ? "Tekrar Baslat" : "Yalnizca Host Tekrar Baslatabilir";
        if (GUI.Button(new Rect(buttonX, firstButtonY, buttonWidth, buttonHeight), restartLabel))
        {
            LevelProgressionManager.RestartActiveLevel();
        }

        GUI.enabled = true;
        if (GUI.Button(new Rect(buttonX, firstButtonY + 52f, buttonWidth, buttonHeight), "Ana Menuye Don"))
        {
            LevelProgressionManager.ReturnToMainMenu();
        }

        GUI.enabled = previousEnabled;
        GUI.depth = previousDepth;
    }

    public bool TryAddTime(float extraSeconds)
    {
        if (!timerEnabled || hasExpired || levelCompleted)
        {
            return false;
        }

        if (extraSeconds <= 0f)
        {
            return false;
        }

        remainingTime += extraSeconds;
        recentAddedTime = extraSeconds;
        recentAddedTimeDisplay = BonusDisplayDuration;
        return true;
    }

    private void ResetStateInternal()
    {
        RestoreCursorAfterResultMenu();
        remainingTime = Mathf.Max(1f, startingTimeSeconds);
        isRunning = false;
        hasExpired = false;
        levelCompleted = false;
        recentAddedTime = 0f;
        recentAddedTimeDisplay = 0f;
    }

    private void ReleaseCursorForResultMenu()
    {
        if (!resultCursorCaptured)
        {
            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            resultCursorCaptured = true;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreCursorAfterResultMenu()
    {
        if (!resultCursorCaptured)
        {
            return;
        }

        Cursor.lockState = previousCursorLockMode;
        Cursor.visible = previousCursorVisible;
        resultCursorCaptured = false;
    }

    private bool IsMultiplayerClientOnly()
    {
        MultiplayerSessionCoordinator coordinator = MultiplayerSessionCoordinator.Instance;
        return coordinator != null && coordinator.IsNetworkSessionActive && !coordinator.IsHostSession;
    }

    private bool HasAnyOccupiedSeat()
    {
        VehicleSeat[] seats = FindObjectsByType<VehicleSeat>(FindObjectsSortMode.None);
        foreach (VehicleSeat seat in seats)
        {
            if (seat != null && seat.IsOccupied)
            {
                return true;
            }
        }

        return false;
    }
}
