using UnityEngine;

[DefaultExecutionOrder(500)]
public class VehicleDriveLeverHUD : MonoBehaviour
{
    [SerializeField] private KeyCode forwardModeKey = KeyCode.Alpha1;
    [SerializeField] private KeyCode reverseModeKey = KeyCode.Alpha2;
    [SerializeField] private KeyCode togglePropellerSystemKey = KeyCode.Alpha3;
    [SerializeField] private KeyCode toggleWingControlKey = KeyCode.Alpha4;
    [SerializeField] private KeyCode toggleTailControlKey = KeyCode.Alpha5;
    [SerializeField] private KeyCode toggleModeKey = KeyCode.X;
    [SerializeField] private Vector2 panelSize = new Vector2(340f, 580f);
    [SerializeField] private Vector2 screenMargin = new Vector2(24f, 24f);

    private ModularVehicleController targetVehicle;

    private static VehicleDriveLeverHUD activeHud;
    private GUIStyle wrappedDetailLabelStyle;

    private void Awake()
    {
        targetVehicle = GetComponent<ModularVehicleController>();
    }

    private void OnEnable()
    {
        activeHud = this;
    }

    private void OnDisable()
    {
        if (activeHud == this)
        {
            activeHud = null;
        }
    }

    private void Update()
    {
        if (targetVehicle == null)
        {
            return;
        }

        if (targetVehicle.IsDriveControlAvailable)
        {
            bool shouldBecomeActiveHud = activeHud == null ||
                                         activeHud == this ||
                                         activeHud.targetVehicle == null ||
                                         (targetVehicle.HasSeatModules && !activeHud.targetVehicle.HasSeatModules);

            if (shouldBecomeActiveHud)
            {
                activeHud = this;
            }
        }
        else if (activeHud == this)
        {
            activeHud = null;
            return;
        }

        if (activeHud != this)
        {
            return;
        }

        if (Input.GetKeyDown(forwardModeKey))
        {
            targetVehicle.SetForwardDriveMode();
        }
        else if (Input.GetKeyDown(reverseModeKey))
        {
            targetVehicle.SetReverseDriveMode();
        }
        else if (Input.GetKeyDown(togglePropellerSystemKey))
        {
            targetVehicle.TogglePropellerSystem();
        }
        else if (Input.GetKeyDown(toggleWingControlKey))
        {
            targetVehicle.ToggleWingControl();
        }
        else if (Input.GetKeyDown(toggleTailControlKey))
        {
            targetVehicle.ToggleTailControl();
        }
        else if (Input.GetKeyDown(toggleModeKey))
        {
            targetVehicle.ToggleDriveMode();
        }
    }

    private void OnGUI()
    {
        if (activeHud != this ||
            targetVehicle == null ||
            PlayerController.BuildModeActive ||
            !targetVehicle.IsDriveControlAvailable)
        {
            return;
        }

        EnsureStyles();

        float safeWidth = Mathf.Max(1f, Screen.width - (screenMargin.x * 2f));
        float safeHeight = Mathf.Max(1f, Screen.height - (screenMargin.y * 2f));
        float scale = Mathf.Min(1f, safeWidth / panelSize.x, safeHeight / panelSize.y);
        float scaledScreenWidth = Screen.width / Mathf.Max(scale, 0.0001f);
        Matrix4x4 previousMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        Rect panelRect = new Rect(
            scaledScreenWidth - panelSize.x - screenMargin.x,
            screenMargin.y,
            panelSize.x,
            panelSize.y);

        Rect leverTrackRect = new Rect(panelRect.x + 40f, panelRect.y + 34f, 40f, 126f);
        bool isForward = targetVehicle.CurrentDriveMode == ModularVehicleController.DriveMode.ForwardOnly;
        Rect leverHandleRect = isForward
            ? new Rect(leverTrackRect.x + 4f, leverTrackRect.y + 8f, leverTrackRect.width - 8f, 34f)
            : new Rect(leverTrackRect.x + 4f, leverTrackRect.yMax - 42f, leverTrackRect.width - 8f, 34f);

        GUI.Box(panelRect, "Vites");
        GUI.Label(new Rect(panelRect.x + 18f, panelRect.y + 12f, 84f, 20f), "W");
        GUI.Label(new Rect(panelRect.x + 18f, panelRect.y + 160f, 84f, 20f), "S");
        GUI.Box(leverTrackRect, GUIContent.none);
        GUI.Box(leverHandleRect, GUIContent.none);

        Rect forwardButtonRect = new Rect(panelRect.x + 10f, panelRect.y + 184f, panelRect.width - 20f, 28f);
        Rect reverseButtonRect = new Rect(panelRect.x + 10f, panelRect.y + 216f, panelRect.width - 20f, 28f);

        if (GUI.Button(forwardButtonRect, "1 Ileri"))
        {
            targetVehicle.SetForwardDriveMode();
        }

        if (GUI.Button(reverseButtonRect, "2 Geri"))
        {
            targetVehicle.SetReverseDriveMode();
        }

        string propellerStatus = targetVehicle.IsPropellerSystemEnabled ? "Acik" : "Kapali";
        Rect propellerButtonRect = new Rect(panelRect.x + 10f, panelRect.y + 248f, panelRect.width - 20f, 28f);
        if (GUI.Button(propellerButtonRect, $"3 Pervane: {propellerStatus}"))
        {
            targetVehicle.TogglePropellerSystem();
        }

        string wingStatus = targetVehicle.IsWingControlEnabled ? "Acik" : "Kapali";
        Rect wingButtonRect = new Rect(panelRect.x + 10f, panelRect.y + 280f, panelRect.width - 20f, 28f);
        if (GUI.Button(wingButtonRect, $"4 Kanat x{targetVehicle.WingSurfaceCount}: {wingStatus}  [Num 8/2, 4/6]"))
        {
            targetVehicle.ToggleWingControl();
        }

        string tailStatus = targetVehicle.IsTailControlEnabled ? "Acik" : "Kapali";
        Rect tailButtonRect = new Rect(panelRect.x + 10f, panelRect.y + 312f, panelRect.width - 20f, 28f);
        if (GUI.Button(tailButtonRect, $"5 Kuyruk x{targetVehicle.TailSurfaceCount}: {tailStatus}  [Num 7/9]"))
        {
            targetVehicle.ToggleTailControl();
        }

        Rect powerRect = new Rect(panelRect.x + 10f, panelRect.y + 348f, panelRect.width - 20f, 118f);
        GUI.Box(powerRect, "Guc");

        GUI.Label(
            new Rect(powerRect.x + 8f, powerRect.y + 22f, powerRect.width - 16f, 18f),
            $"Kutle: {targetVehicle.SimulationMass:0}");

        GUI.Label(
            new Rect(powerRect.x + 8f, powerRect.y + 40f, powerRect.width - 16f, 18f),
            $"Motor Carpani: x{targetVehicle.MotorPowerMultiplier:0.##}");

        DrawPowerRow(
            new Rect(powerRect.x + 8f, powerRect.y + 58f, powerRect.width - 16f, 18f),
            "Teker",
            targetVehicle.GroundWheelCount,
            targetVehicle.WheelDisplayPowerLevel);

        DrawDetailedPowerRow(
            new Rect(powerRect.x + 8f, powerRect.y + 76f, powerRect.width - 16f, 18f),
            "Heli",
            targetVehicle.HelicopterPropellerCount,
            targetVehicle.HelicopterPropellerDisplayPowerLevel,
            targetVehicle.HelicopterSingleLoadRatio,
            targetVehicle.HelicopterTotalLoadRatio);

        DrawDetailedPowerRow(
            new Rect(powerRect.x + 8f, powerRect.y + 98f, powerRect.width - 16f, 18f),
            "Ucak",
            targetVehicle.PlanePropellerCount,
            targetVehicle.PlanePropellerDisplayPowerLevel,
            targetVehicle.PlaneSingleLoadRatio,
            targetVehicle.PlaneTotalLoadRatio);

        Rect detailRect = new Rect(panelRect.x + 10f, panelRect.y + 472f, panelRect.width - 20f, 94f);
        GUI.Box(detailRect, "Detay");
        GUI.Label(
            new Rect(detailRect.x + 8f, detailRect.y + 20f, detailRect.width - 16f, 16f),
            $"Motor W/M/S: {targetVehicle.WeakMotorCount}/{targetVehicle.MediumMotorCount}/{targetVehicle.StrongMotorCount}");
        GUI.Label(
            new Rect(detailRect.x + 8f, detailRect.y + 36f, detailRect.width - 16f, 16f),
            $"Koltuk: {targetVehicle.OccupiedSeatCount}/{targetVehicle.SeatCount}  |  Pervane: {propellerStatus}");
        GUI.Label(
            new Rect(detailRect.x + 8f, detailRect.y + 54f, detailRect.width - 16f, 16f),
            $"Kanat: {wingStatus}  |  Kuyruk: {tailStatus}");
        GUI.Label(
            new Rect(detailRect.x + 8f, detailRect.y + 70f, detailRect.width - 16f, 22f),
            "Oran: tek / toplam pervane",
            wrappedDetailLabelStyle);

        GUI.matrix = previousMatrix;
    }

    private void EnsureStyles()
    {
        if (wrappedDetailLabelStyle != null)
        {
            return;
        }

        wrappedDetailLabelStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true
        };
    }

    private void DrawPowerRow(Rect rowRect, string label, int count, int power)
    {
        string countText = count > 0 ? "x" + count : "-";
        GUI.Label(rowRect, label + " " + countText + ": " + power);
    }

    private void DrawDetailedPowerRow(Rect rowRect, string label, int count, int power, float singleRatio, float totalRatio)
    {
        if (count <= 0)
        {
            GUI.Label(rowRect, label + " -");
            return;
        }

        GUI.Label(
            rowRect,
            $"{label} x{count}: {power} | Oran tek/top: {singleRatio:0.##} / {totalRatio:0.##}");
    }
}
