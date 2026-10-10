using UnityEngine;

public class WheelBuildDirectionIndicator : MonoBehaviour
{
    [SerializeField] private Color arrowColor = new Color(0.2f, 1f, 0.35f, 0.95f);
    [SerializeField] private float arrowHeight = 0.55f;
    [SerializeField] private float arrowLength = 0.7f;
    [SerializeField] private float arrowHeadLength = 0.2f;
    [SerializeField] private float arrowHeadWidth = 0.16f;
    [SerializeField] private float arrowWidth = 0.04f;

    private LineRenderer lineRenderer;
    private FrontWheele frontWheel;
    private BackWheele backWheel;
    private Rigidbody wheelBody;

    private static Material sharedArrowMaterial;

    private void Awake()
    {
        frontWheel = GetComponent<FrontWheele>();
        backWheel = GetComponent<BackWheele>();
        ResolveRuntimeReferences();
        EnsureRenderer();
    }

    private void LateUpdate()
    {
        ResolveRuntimeReferences();
        bool shouldShow = PlayerController.BuildModeActive && gameObject.activeInHierarchy;
        if (lineRenderer == null)
        {
            EnsureRenderer();
        }

        if (lineRenderer == null)
        {
            return;
        }

        lineRenderer.enabled = shouldShow;
        if (!shouldShow)
        {
            return;
        }

        UpdateArrow();
    }

    private void EnsureRenderer()
    {
        if (lineRenderer != null)
        {
            return;
        }

        GameObject arrowObject = new GameObject("WheelDirectionArrow");
        arrowObject.transform.SetParent(transform, false);

        lineRenderer = arrowObject.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = false;
        lineRenderer.positionCount = 5;
        lineRenderer.startWidth = arrowWidth;
        lineRenderer.endWidth = arrowWidth;
        lineRenderer.numCapVertices = 2;
        lineRenderer.numCornerVertices = 2;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.material = GetSharedArrowMaterial();
        lineRenderer.startColor = arrowColor;
        lineRenderer.endColor = arrowColor;
        lineRenderer.sortingOrder = 50;
    }

    private void UpdateArrow()
    {
        Vector3 direction = GetBuildForwardDirection();
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.forward;
        }

        Vector3 localDirection = transform.InverseTransformDirection(Vector3.ProjectOnPlane(direction.normalized, Vector3.up));
        if (localDirection.sqrMagnitude < 0.0001f)
        {
            localDirection = Vector3.forward;
        }

        localDirection.Normalize();

        Vector3 localRight = Vector3.Cross(Vector3.up, localDirection).normalized;
        Vector3 basePoint = Vector3.up * arrowHeight;
        Vector3 tipPoint = basePoint + localDirection * arrowLength;
        Vector3 headBase = tipPoint - localDirection * arrowHeadLength;
        Vector3 leftHead = headBase + localRight * arrowHeadWidth;
        Vector3 rightHead = headBase - localRight * arrowHeadWidth;

        lineRenderer.SetPosition(0, basePoint);
        lineRenderer.SetPosition(1, tipPoint);
        lineRenderer.SetPosition(2, leftHead);
        lineRenderer.SetPosition(3, tipPoint);
        lineRenderer.SetPosition(4, rightHead);
    }

    private Vector3 GetBuildForwardDirection()
    {
        if (frontWheel != null)
        {
            return frontWheel.GetBuildForwardDirection();
        }

        if (backWheel != null)
        {
            return backWheel.GetBuildForwardDirection();
        }

        return transform.forward;
    }

    private void ResolveRuntimeReferences()
    {
        if (wheelBody == null)
        {
            wheelBody = GetComponentInParent<Rigidbody>();
        }
    }

    private static Material GetSharedArrowMaterial()
    {
        if (sharedArrowMaterial != null)
        {
            return sharedArrowMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        sharedArrowMaterial = new Material(shader);
        sharedArrowMaterial.name = "WheelDirectionArrowMaterial";
        return sharedArrowMaterial;
    }
}
