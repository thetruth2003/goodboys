using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class LevelCheckpoint : MonoBehaviour
{
    [SerializeField] private bool addsTimeToTimer = true;
    [SerializeField] private float timeBonusSeconds = 10f;
    [SerializeField] private bool startsActive = false;
    [SerializeField] private LevelCheckpoint nextCheckpoint;
    [SerializeField] private bool showSceneGizmo = true;
    [SerializeField] private Color activeGizmoColor = new Color(0.2f, 1f, 0.55f, 0.35f);
    [SerializeField] private Color inactiveGizmoColor = new Color(0.35f, 0.45f, 0.55f, 0.18f);
    [SerializeField] private Color completedGizmoColor = new Color(1f, 0.7f, 0.2f, 0.28f);
    [SerializeField] private bool showActiveBeam = true;
    [SerializeField] private float beamHeight = 45f;
    [SerializeField] private float beamWidth = 0.8f;
    [SerializeField] private Color beamColor = new Color(0.25f, 1f, 0.65f, 0.9f);

    private BoxCollider triggerCollider;
    private bool isActiveCheckpoint;
    private bool hasTriggered;
    private LineRenderer beamRenderer;
    private Material beamMaterial;

    public float TimeBonusSeconds => Mathf.Max(0f, timeBonusSeconds);

    public bool AddsTimeToTimer => addsTimeToTimer;

    public bool StartsActive => startsActive;

    public bool IsActiveCheckpoint => isActiveCheckpoint;

    public bool HasTriggered => hasTriggered;

    private void Awake()
    {
        EnsureTriggerSetup();
    }

    private void OnValidate()
    {
        EnsureTriggerSetup();
        timeBonusSeconds = Mathf.Max(0f, timeBonusSeconds);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryActivate(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryActivate(other);
    }

    private void Update()
    {
        UpdateBeam();
    }

    public void ResetCheckpointState()
    {
        hasTriggered = false;
        isActiveCheckpoint = false;
        RefreshTriggerState();
    }

    public void SetCheckpointActive(bool active)
    {
        if (hasTriggered)
        {
            isActiveCheckpoint = false;
        }
        else
        {
            isActiveCheckpoint = active;
        }

        RefreshTriggerState();
    }

    public void MarkTriggered()
    {
        hasTriggered = true;
        isActiveCheckpoint = false;
        RefreshTriggerState();
    }

    public void ActivateNextCheckpoint()
    {
        if (nextCheckpoint != null)
        {
            nextCheckpoint.SetCheckpointActive(true);
        }

        VehicleFinishLineSystem.RefreshObjectiveStates();
    }

    private void TryActivate(Collider other)
    {
        if (PlayerController.BuildModeActive || !isActiveCheckpoint || hasTriggered || other == null || !IsCheckpointTarget(other))
        {
            return;
        }

        LevelTimerSystem timer = LevelTimerSystem.ActiveTimer;
        if (AddsTimeToTimer && timer != null)
        {
            timer.TryAddTime(TimeBonusSeconds);
        }

        MarkTriggered();
        ActivateNextCheckpoint();
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

        RefreshTriggerState();
    }

    private void RefreshTriggerState()
    {
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private bool IsCheckpointTarget(Collider other)
    {
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

        bool shouldShow = showActiveBeam && isActiveCheckpoint && !hasTriggered;
        beamRenderer.enabled = shouldShow;

        if (!shouldShow || triggerCollider == null)
        {
            return;
        }

        Vector3 start = transform.TransformPoint(triggerCollider.center);
        Vector3 end = start + Vector3.up * Mathf.Max(5f, beamHeight);

        beamRenderer.startWidth = Mathf.Max(0.1f, beamWidth);
        beamRenderer.endWidth = Mathf.Max(0.1f, beamWidth);
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

        GameObject beamObject = new GameObject("CheckpointBeam");
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

        Color gizmoColor = inactiveGizmoColor;
        if (hasTriggered)
        {
            gizmoColor = completedGizmoColor;
        }
        else if (isActiveCheckpoint)
        {
            gizmoColor = activeGizmoColor;
        }

        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(triggerCollider.center, triggerCollider.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.95f);
        Gizmos.DrawWireCube(triggerCollider.center, triggerCollider.size);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
