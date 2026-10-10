using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(BoxCollider))]
public class VehicleFinishLineSystem : MonoBehaviour
{
    [SerializeField] private bool pauseOnWin = true;
    [SerializeField] private bool showSceneGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0.15f, 0.95f, 0.35f, 0.35f);
    [SerializeField] private string victoryTitle = "Gorev Tamamlandi!";
    [SerializeField] private string victorySubtitle = "Arac bitis cizgisini gecti. Sonraki gorev artik acik.";
    [SerializeField] private bool showActiveBeam = true;
    [SerializeField] private float beamHeight = 55f;
    [SerializeField] private float beamWidth = 1f;
    [SerializeField] private Color beamColor = new Color(1f, 0.92f, 0.3f, 0.92f);
    private static VehicleFinishLineSystem activeVictoryTrigger;

    private BoxCollider triggerCollider;
    private bool hasWon;
    private bool isFinishActive = true;
    private LevelProgressionManager.CompletionResult completionResult;
    private LineRenderer beamRenderer;
    private Material beamMaterial;
    private bool resultCursorCaptured;
    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;

    public static void ResetVictoryState()
    {
        activeVictoryTrigger = null;

        VehicleFinishLineSystem[] triggers = FindObjectsByType<VehicleFinishLineSystem>(FindObjectsSortMode.None);
        foreach (VehicleFinishLineSystem trigger in triggers)
        {
            if (trigger != null)
            {
                trigger.hasWon = false;
                trigger.RestoreCursorAfterResultMenu();
                trigger.RefreshFinishActivationState();
            }
        }
    }

    public static void RefreshObjectiveStates()
    {
        VehicleFinishLineSystem[] triggers = FindObjectsByType<VehicleFinishLineSystem>(FindObjectsSortMode.None);
        foreach (VehicleFinishLineSystem trigger in triggers)
        {
            if (trigger != null)
            {
                trigger.RefreshFinishActivationState();
            }
        }
    }

    private void Awake()
    {
        EnsureTriggerSetup();
        LevelProgressionManager.EnsureActiveLevelForScene(SceneManager.GetActiveScene().name);
        RefreshFinishActivationState();
    }

    private void OnValidate()
    {
        EnsureTriggerSetup();
    }

    private void OnDisable()
    {
        RestoreCursorAfterResultMenu();
        if (activeVictoryTrigger == this)
        {
            activeVictoryTrigger = null;
        }
    }

    private void Update()
    {
        if (PlayerController.BuildModeActive && hasWon)
        {
            hasWon = false;
            RestoreCursorAfterResultMenu();
            if (activeVictoryTrigger == this)
            {
                activeVictoryTrigger = null;
            }
        }

        if (hasWon)
        {
            ReleaseCursorForResultMenu();
        }

        RefreshFinishActivationState();
        UpdateBeam();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryMarkVictory(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryMarkVictory(other);
    }

    private void OnGUI()
    {
        if (activeVictoryTrigger != this || !hasWon)
        {
            return;
        }

        int previousDepth = GUI.depth;
        GUI.depth = -100;

        float panelWidth = Mathf.Min(560f, Screen.width - 40f);
        float panelHeight = 250f;
        Rect panelRect = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth,
            panelHeight);

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.78f);
        GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
        GUI.color = previousColor;

        GUI.Box(panelRect, GUIContent.none);

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * 0.03f, 24f, 36f)),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * 0.016f, 15f, 22f)),
            normal = { textColor = new Color(0.93f, 0.93f, 0.93f) }
        };

        int currentLevel = completionResult.CompletedLevelNumber > 0
            ? completionResult.CompletedLevelNumber
            : LevelProgressionManager.GetActiveLevelNumber();
        string currentLevelName = LevelProgressionManager.GetLevelDisplayName(currentLevel);

        GUI.Label(new Rect(panelRect.x + 20f, panelRect.y + 28f, panelRect.width - 40f, 48f), victoryTitle, titleStyle);
        GUI.Label(
            new Rect(panelRect.x + 24f, panelRect.y + 76f, panelRect.width - 48f, 72f),
            $"{currentLevelName}\n{victorySubtitle}",
            subtitleStyle);

        float buttonWidth = panelRect.width - 80f;
        float buttonHeight = 34f;
        float buttonX = panelRect.x + 40f;
        float firstButtonY = panelRect.y + panelRect.height - 104f;

        int nextLevel = completionResult.HasNextLevel ? completionResult.NextLevelNumber : currentLevel;
        bool canAdvance = completionResult.HasNextLevel &&
                          (!IsMultiplayerClientOnly());

        bool previousEnabled = GUI.enabled;
        GUI.enabled = canAdvance;
        string nextLabel = completionResult.HasNextLevel
            ? $"Sonraki Bolume Gec  ({LevelProgressionManager.GetLevelDisplayName(nextLevel)})"
            : "Tum bolumler tamamlandi";
        if (GUI.Button(new Rect(buttonX, firstButtonY, buttonWidth, buttonHeight), nextLabel))
        {
            LevelProgressionManager.StartLevel(nextLevel, SceneManager.GetActiveScene().name);
        }
        GUI.enabled = true;

        if (GUI.Button(new Rect(buttonX, firstButtonY + 40f, buttonWidth, buttonHeight), "Ana Menuye Don"))
        {
            LevelProgressionManager.ReturnToMainMenu();
        }

        GUI.enabled = previousEnabled;
        GUI.Label(
            new Rect(panelRect.x + 20f, panelRect.y + panelRect.height - 28f, panelRect.width - 40f, 20f),
            canAdvance ? "Istersen R ile ayni gorevi tekrar da deneyebilirsin." : "Sadece host sonraki gorevi acabilir. R ile ayni gorevi yeniden deneyebilirsin.",
            subtitleStyle);
        GUI.depth = previousDepth;
    }

    private void OnDrawGizmos()
    {
        if (!showSceneGizmo)
        {
            return;
        }

        EnsureTriggerSetup();
        if (triggerCollider == null)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(triggerCollider.center, triggerCollider.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.95f);
        Gizmos.DrawWireCube(triggerCollider.center, triggerCollider.size);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private void EnsureTriggerSetup()
    {
        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<BoxCollider>();
        }

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private bool IsWinningTarget(Collider other)
    {
        if (other == null)
        {
            return false;
        }

        if (other.GetComponentInParent<PlayerController>() != null)
        {
            return true;
        }

        Rigidbody attachedBody = other.attachedRigidbody;
        if (attachedBody == null)
        {
            attachedBody = other.GetComponentInParent<Rigidbody>();
        }

        if (attachedBody == null)
        {
            return false;
        }

        return attachedBody.GetComponent<ModularVehicleController>() != null ||
               attachedBody.GetComponentInParent<ModularVehicleController>() != null;
    }

    private void TryMarkVictory(Collider other)
    {
        if (hasWon || !isFinishActive || PlayerController.BuildModeActive || !IsWinningTarget(other))
        {
            return;
        }

        LevelProgressionManager.EnsureActiveLevelForScene(SceneManager.GetActiveScene().name);
        completionResult = LevelProgressionManager.CompleteActiveLevel();
        LevelTimerSystem.NotifyLevelCompleted();
        hasWon = true;
        activeVictoryTrigger = this;
        ReleaseCursorForResultMenu();

        if (pauseOnWin)
        {
            Time.timeScale = 0f;
        }
    }

    private bool IsMultiplayerClientOnly()
    {
        MultiplayerSessionCoordinator coordinator = MultiplayerSessionCoordinator.Instance;
        return coordinator != null && coordinator.IsNetworkSessionActive && !coordinator.IsHostSession;
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

    private void RefreshFinishActivationState()
    {
        LevelCheckpoint[] checkpoints = FindObjectsByType<LevelCheckpoint>(FindObjectsSortMode.None);
        if (checkpoints == null || checkpoints.Length == 0)
        {
            isFinishActive = true;
            return;
        }

        for (int index = 0; index < checkpoints.Length; index++)
        {
            LevelCheckpoint checkpoint = checkpoints[index];
            if (checkpoint != null && !checkpoint.HasTriggered)
            {
                isFinishActive = false;
                return;
            }
        }

        isFinishActive = true;
    }

    private void UpdateBeam()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureBeam();
        if (beamRenderer == null)
        {
            return;
        }

        bool shouldShow = showActiveBeam && isFinishActive && !hasWon;
        beamRenderer.enabled = shouldShow;

        if (!shouldShow || triggerCollider == null)
        {
            return;
        }

        Vector3 start = transform.TransformPoint(triggerCollider.center);
        Vector3 end = start + Vector3.up * Mathf.Max(6f, beamHeight);

        beamRenderer.startWidth = Mathf.Max(0.12f, beamWidth);
        beamRenderer.endWidth = Mathf.Max(0.12f, beamWidth);
        beamRenderer.startColor = beamColor;
        beamRenderer.endColor = new Color(beamColor.r, beamColor.g, beamColor.b, 0.05f);
        beamRenderer.SetPosition(0, start);
        beamRenderer.SetPosition(1, end);
    }

    private void EnsureBeam()
    {
        if (beamRenderer != null)
        {
            return;
        }

        GameObject beamObject = new GameObject("FinishBeam");
        beamObject.transform.SetParent(transform, false);
        beamRenderer = beamObject.AddComponent<LineRenderer>();
        beamRenderer.useWorldSpace = true;
        beamRenderer.positionCount = 2;
        beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beamRenderer.receiveShadows = false;
        beamRenderer.textureMode = LineTextureMode.Stretch;
        beamRenderer.alignment = LineAlignment.View;
        beamRenderer.numCapVertices = 4;

        Shader beamShader = Shader.Find("Sprites/Default");
        if (beamShader != null)
        {
            beamMaterial = new Material(beamShader);
            beamMaterial.color = beamColor;
            beamRenderer.material = beamMaterial;
        }
    }

    private void OnDestroy()
    {
        if (beamMaterial != null)
        {
            Destroy(beamMaterial);
        }
    }
}
