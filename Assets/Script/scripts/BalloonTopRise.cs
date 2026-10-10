using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class BalloonTopRise : MonoBehaviour
{
    public const float DefaultSupportSearchPadding = 0.08f;

    [Header("Rise")]
    [Tooltip("Top simulasyonda yukari ne kadar istekli ciksin.")]
    [Min(0.1f)]
    [SerializeField] private float upwardAcceleration = 8f;

    [Tooltip("Topun cikarken ulasacagi maksimum dikey hiz.")]
    [Min(0.1f)]
    [SerializeField] private float maxRiseSpeed = 4f;

    [Tooltip("Yatay savrulma fazla olursa biraz sakinlestirir.")]
    [Min(0f)]
    [SerializeField] private float horizontalDamping = 1.2f;

    [Header("Rope")]
    [Tooltip("Altindaki kupu ararken X/Z tarafinda bu kadar tolerans kullan.")]
    [Min(0f)]
    [SerializeField] private float supportSearchPadding = DefaultSupportSearchPadding;

    [Tooltip("Top ilk basta bu kadar ekstra ip boslugu ile yukari ciksin.")]
    [Min(0f)]
    [SerializeField] private float slackRiseDistance = 0.9f;

    [Tooltip("Ip limite yaklasinca bu mesafede gerilim hissi artmaya baslar.")]
    [Min(0.01f)]
    [SerializeField] private float ropeTensionBlendDistance = 0.35f;

    [Tooltip("Ip gerildikten sonra tekrar gevsek moda donmesi icin gereken ekstra bosluk.")]
    [Min(0.01f)]
    [SerializeField] private float ropeLatchReleaseSlack = 0.16f;

    [Tooltip("Ip limitini asmissa topu limite geri toplama hizi.")]
    [Min(0.1f)]
    [SerializeField] private float ropeCorrectionSpeed = 24f;

    [Tooltip("Ip gerildiginde topun disari dogru kacis hizini ne kadar kessin.")]
    [Range(0f, 2f)]
    [SerializeField] private float ropeOutwardVelocityDamping = 1.2f;

    [Tooltip("Ipin gorsel egimi. Gevsekken daha belirgin olur.")]
    [Min(0f)]
    [SerializeField] private float ropeSagAmount = 0.18f;

    [Tooltip("Ip kac noktadan cizilsin.")]
    [Range(2, 20)]
    [SerializeField] private int ropeSegmentCount = 10;

    [Header("Lift")]
    [Tooltip("Bu kutleye kadar top alttaki blogu rahat kaldirir.")]
    [Min(1f)]
    [SerializeField] private float fullLiftMassCapacity = 300f;

    [Tooltip("Bu kutleye kadar yukari cekis azalir ama hala destek verir.")]
    [Min(1f)]
    [SerializeField] private float hoverLiftMassCapacity = 360f;

    [Tooltip("Hover limitten sonra destek bu aralikta sifira duser.")]
    [Min(1f)]
    [SerializeField] private float overloadGraceMass = 140f;

    [Tooltip("Hafif yukte blogu ne kadar rahat kaldirsin.")]
    [Min(0.1f)]
    [SerializeField] private float fullLiftAcceleration = 12f;

    [Tooltip("Ipin gorunen kalinligi.")]
    [Min(0.005f)]
    [SerializeField] private float ropeWidth = 0.035f;

    [SerializeField] private Color ropeColor = new Color(0.16f, 0.12f, 0.08f, 1f);

    [Header("Runtime")]
    [Tooltip("Simulasyonda olusan topun kutlesi.")]
    [Min(0.01f)]
    [SerializeField] private float runtimeBalloonMass = 0.02f;

    [Tooltip("Simulasyonda olusan topun boyutu.")]
    [Min(0.1f)]
    [SerializeField] private float runtimeScaleMultiplier = 1f;

    [Tooltip("Simulasyonda yerde duran mesh gizlensin ki cift top gorunmesin.")]
    [SerializeField] private bool hidePlacedMeshDuringSimulation = true;

    private Rigidbody localBody;
    private Rigidbody simulationRootBody;
    private ModularVehicleController vehicleController;
    private Renderer[] buildRenderers = System.Array.Empty<Renderer>();
    private Collider[] buildColliders = System.Array.Empty<Collider>();
    private LineRenderer ropeRenderer;

    private GameObject runtimeBalloonObject;
    private Rigidbody runtimeBalloonBody;
    private Vector3 runtimeSupportAnchorLocal;
    private bool hasRuntimeSupportAnchor;
    private float runtimeRopeStartDistance;
    private float currentRopeTensionFactor;
    private bool ropeLatched;
    private bool runtimeInitialized;

    private void Awake()
    {
        CacheBuildComponents();
        ResolveRuntimeReferences();
        EnsureRopeRenderer();
        UpdateRopeRenderer(false);
    }

    private void OnEnable()
    {
        runtimeInitialized = false;
        ResolveRuntimeReferences();
        EnsureRopeRenderer();
        RestoreBuildVisuals();
        UpdateRopeRenderer(false);
    }

    private void FixedUpdate()
    {
        ResolveRuntimeReferences();

        if (!IsSimulationCloneActive())
        {
            return;
        }

        if (!runtimeInitialized)
        {
            InitializeRuntimeBalloon();
        }

        SimulateRuntimeBalloon();
    }

    private void LateUpdate()
    {
        if (IsSimulationCloneActive())
        {
            UpdateRopeRenderer(hasRuntimeSupportAnchor && runtimeBalloonBody != null);
            return;
        }

        RestoreBuildVisuals();
        UpdateRopeRenderer(false);
    }

    private void OnDisable()
    {
        CleanupRuntimeArtifacts();
        RestoreBuildVisuals();
    }

    private void OnDestroy()
    {
        CleanupRuntimeArtifacts();
    }

    private void ResolveRuntimeReferences()
    {
        if (localBody == null)
        {
            localBody = GetComponent<Rigidbody>();
        }

        if (localBody != null)
        {
            simulationRootBody = null;
            vehicleController = null;
            return;
        }

        if (simulationRootBody == null)
        {
            simulationRootBody = GetComponentInParent<Rigidbody>();
        }

        if (vehicleController == null)
        {
            vehicleController = GetComponentInParent<ModularVehicleController>();
        }
    }

    private bool IsSimulationCloneActive()
    {
        if (PlayerController.BuildModeActive)
        {
            return false;
        }

        return localBody == null &&
               simulationRootBody != null &&
               !simulationRootBody.isKinematic &&
               vehicleController != null &&
               vehicleController.isActiveAndEnabled;
    }

    private void InitializeRuntimeBalloon()
    {
        CleanupRuntimeArtifacts();
        runtimeInitialized = true;
        CacheBuildComponents();

        if (hidePlacedMeshDuringSimulation)
        {
            SetBuildRenderersEnabled(false);
        }

        SetBuildCollidersEnabled(false);
        CreateRuntimeBalloonObject();

        if (TryFindSupportCubeBelow(
                transform,
                transform.parent,
                supportSearchPadding,
                out _,
                out Vector3 anchorWorldPosition))
        {
            CreateRuntimeRopeAnchor(anchorWorldPosition);
            AttachRopeJoint();
        }

        UpdateRopeRenderer(hasRuntimeSupportAnchor && runtimeBalloonBody != null);
    }

    private void CreateRuntimeBalloonObject()
    {
        runtimeBalloonObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        runtimeBalloonObject.name = $"{name}_RuntimeBalloon";
        runtimeBalloonObject.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
        runtimeBalloonObject.transform.localScale = transform.lossyScale * runtimeScaleMultiplier;

        SphereCollider runtimeSphereCollider = runtimeBalloonObject.GetComponent<SphereCollider>();
        if (runtimeSphereCollider != null)
        {
            runtimeSphereCollider.enabled = false;
        }

        CopyBuildVisualMaterial(runtimeBalloonObject.GetComponent<Renderer>());

        runtimeBalloonBody = runtimeBalloonObject.AddComponent<Rigidbody>();
        runtimeBalloonBody.mass = runtimeBalloonMass;
        runtimeBalloonBody.useGravity = false;
        runtimeBalloonBody.linearDamping = 0.35f;
        runtimeBalloonBody.angularDamping = 3f;
        runtimeBalloonBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        runtimeBalloonBody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void CreateRuntimeRopeAnchor(Vector3 anchorWorldPosition)
    {
        if (simulationRootBody == null)
        {
            hasRuntimeSupportAnchor = false;
            runtimeSupportAnchorLocal = Vector3.zero;
            runtimeRopeStartDistance = 0f;
            return;
        }

        runtimeSupportAnchorLocal = simulationRootBody.transform.InverseTransformPoint(anchorWorldPosition);
        runtimeRopeStartDistance = Vector3.Distance(transform.position, anchorWorldPosition);
        hasRuntimeSupportAnchor = true;
    }

    private void AttachRopeJoint()
    {
        currentRopeTensionFactor = 0f;
        ropeLatched = false;
    }

    private void SimulateRuntimeBalloon()
    {
        if (runtimeBalloonBody == null)
        {
            return;
        }

        Vector3 velocity = runtimeBalloonBody.linearVelocity;
        float horizontalLerp = Mathf.Clamp01(horizontalDamping * Time.fixedDeltaTime);
        velocity.x = Mathf.Lerp(velocity.x, 0f, horizontalLerp);
        velocity.z = Mathf.Lerp(velocity.z, 0f, horizontalLerp);

        if (velocity.y > maxRiseSpeed)
        {
            velocity.y = maxRiseSpeed;
        }

        runtimeBalloonBody.linearVelocity = velocity;

        if (runtimeBalloonBody.linearVelocity.y < maxRiseSpeed)
        {
            runtimeBalloonBody.AddForce(Vector3.up * upwardAcceleration, ForceMode.Acceleration);
        }

        ApplyRopeLengthLimit(Time.fixedDeltaTime);
        ApplyLiftForceToSupport();
    }

    private void ApplyRopeLengthLimit(float deltaTime)
    {
        if (!hasRuntimeSupportAnchor || runtimeBalloonBody == null)
        {
            currentRopeTensionFactor = 0f;
            ropeLatched = false;
            return;
        }

        Vector3 anchorPosition = GetSupportAnchorWorldPosition();
        Vector3 anchorToBalloon = runtimeBalloonBody.position - anchorPosition;
        float distance = anchorToBalloon.magnitude;
        float ropeLimit = GetRopeLimitDistance();

        if (distance <= 0.0001f)
        {
            currentRopeTensionFactor = 0f;
            ropeLatched = false;
            return;
        }

        float latchDistance = Mathf.Max(0.01f, ropeLimit - ropeTensionBlendDistance);
        float releaseDistance = ropeLimit - ropeLatchReleaseSlack;

        if (ropeLatched)
        {
            if (distance < releaseDistance)
            {
                ropeLatched = false;
            }
        }
        else if (distance >= latchDistance)
        {
            ropeLatched = true;
        }

        if (!ropeLatched)
        {
            currentRopeTensionFactor = Mathf.InverseLerp(latchDistance, ropeLimit, distance);
            return;
        }

        currentRopeTensionFactor = 1f;

        Vector3 ropeDirection = anchorToBalloon / distance;
        Vector3 limitedPosition = anchorPosition + ropeDirection * ropeLimit;
        float correctionFactor = 1f - Mathf.Exp(-ropeCorrectionSpeed * deltaTime);
        Vector3 correctedPosition = Vector3.Lerp(runtimeBalloonBody.position, limitedPosition, correctionFactor);
        runtimeBalloonBody.MovePosition(correctedPosition);

        Vector3 velocity = runtimeBalloonBody.linearVelocity;
        float radialSpeed = Vector3.Dot(velocity, ropeDirection);
        velocity -= ropeDirection * radialSpeed;

        if (radialSpeed > 0f)
        {
            velocity -= ropeDirection * radialSpeed * Mathf.Max(0f, ropeOutwardVelocityDamping - 1f);
        }

        runtimeBalloonBody.linearVelocity = velocity;
    }

    private void ApplyLiftForceToSupport()
    {
        if (simulationRootBody == null || !hasRuntimeSupportAnchor)
        {
            return;
        }

        if (currentRopeTensionFactor <= 0.001f)
        {
            return;
        }

        float liftAcceleration = CalculateLiftAcceleration(simulationRootBody.mass);
        if (liftAcceleration <= 0f)
        {
            return;
        }

        simulationRootBody.AddForceAtPosition(
            Vector3.up * (liftAcceleration * currentRopeTensionFactor),
            GetSupportAnchorWorldPosition(),
            ForceMode.Acceleration);
    }

    private float CalculateLiftAcceleration(float totalMass)
    {
        float gravityMagnitude = Physics.gravity.magnitude;
        if (gravityMagnitude <= 0.001f)
        {
            return 0f;
        }

        int supportBalloonCount = GetSupportBalloonCount();
        if (supportBalloonCount <= 0)
        {
            return 0f;
        }

        float sharedMass = totalMass / supportBalloonCount;
        float totalSupportAcceleration;

        if (sharedMass <= fullLiftMassCapacity)
        {
            totalSupportAcceleration = fullLiftAcceleration;
        }
        else if (sharedMass <= hoverLiftMassCapacity)
        {
            float t = Mathf.InverseLerp(fullLiftMassCapacity, hoverLiftMassCapacity, sharedMass);
            totalSupportAcceleration = Mathf.Lerp(fullLiftAcceleration, gravityMagnitude, t);
        }
        else
        {
            float overloadT = Mathf.InverseLerp(
                hoverLiftMassCapacity,
                hoverLiftMassCapacity + overloadGraceMass,
                sharedMass);
            totalSupportAcceleration = Mathf.Lerp(gravityMagnitude, 0f, overloadT);
        }

        return totalSupportAcceleration / supportBalloonCount;
    }

    private void EnsureRopeRenderer()
    {
        if (ropeRenderer != null)
        {
            return;
        }

        Transform existingRope = transform.Find("BalloonTopRope");
        if (existingRope != null)
        {
            ropeRenderer = existingRope.GetComponent<LineRenderer>();
        }

        if (ropeRenderer == null)
        {
            GameObject ropeObject = new GameObject("BalloonTopRope");
            ropeObject.transform.SetParent(transform, false);
            ropeRenderer = ropeObject.AddComponent<LineRenderer>();
        }

        ropeRenderer.shadowCastingMode = ShadowCastingMode.Off;
        ropeRenderer.receiveShadows = false;
        ropeRenderer.textureMode = LineTextureMode.Stretch;
        ropeRenderer.alignment = LineAlignment.View;
        ropeRenderer.useWorldSpace = true;
        ropeRenderer.widthMultiplier = ropeWidth;
        ropeRenderer.numCapVertices = 2;
        ropeRenderer.positionCount = 2;

        if (ropeRenderer.sharedMaterial == null)
        {
            Shader ropeShader = Shader.Find("Sprites/Default");
            if (ropeShader != null)
            {
                ropeRenderer.sharedMaterial = new Material(ropeShader);
            }
        }

        if (ropeRenderer.sharedMaterial != null)
        {
            ropeRenderer.sharedMaterial.color = ropeColor;
        }

        ropeRenderer.startColor = ropeColor;
        ropeRenderer.endColor = ropeColor;
    }

    private void UpdateRopeRenderer(bool enabled)
    {
        EnsureRopeRenderer();
        if (ropeRenderer == null)
        {
            return;
        }

        ropeRenderer.enabled = enabled;
        if (!enabled || !hasRuntimeSupportAnchor || runtimeBalloonBody == null)
        {
            return;
        }

        Vector3 ropeStart = GetSupportAnchorWorldPosition();
        Vector3 ropeEnd = runtimeBalloonBody.position;
        int segments = Mathf.Max(2, ropeSegmentCount);
        ropeRenderer.positionCount = segments;

        float sag = ropeSagAmount * (1f - currentRopeTensionFactor);

        for (int i = 0; i < segments; i++)
        {
            float t = segments == 1 ? 0f : i / (float)(segments - 1);
            Vector3 point = Vector3.Lerp(ropeStart, ropeEnd, t);
            float sagOffset = Mathf.Sin(t * Mathf.PI) * sag;
            point += Vector3.down * sagOffset;
            ropeRenderer.SetPosition(i, point);
        }
    }

    private void CacheBuildComponents()
    {
        if (buildRenderers.Length == 0)
        {
            buildRenderers = GetComponentsInChildren<Renderer>(true);
        }

        if (buildColliders.Length == 0)
        {
            buildColliders = GetComponentsInChildren<Collider>(true);
        }
    }

    private void CopyBuildVisualMaterial(Renderer runtimeRenderer)
    {
        if (runtimeRenderer == null || buildRenderers.Length == 0)
        {
            return;
        }

        Renderer sourceRenderer = buildRenderers[0];
        if (sourceRenderer != null && sourceRenderer.sharedMaterial != null)
        {
            runtimeRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        }
    }

    private void SetBuildRenderersEnabled(bool enabled)
    {
        foreach (Renderer renderer in buildRenderers)
        {
            if (renderer == null || renderer == ropeRenderer)
            {
                continue;
            }

            renderer.enabled = enabled;
        }
    }

    private void SetBuildCollidersEnabled(bool enabled)
    {
        foreach (Collider collider in buildColliders)
        {
            if (collider == null || collider.isTrigger)
            {
                continue;
            }

            collider.enabled = enabled;
        }
    }

    private void RestoreBuildVisuals()
    {
        SetBuildRenderersEnabled(true);
        SetBuildCollidersEnabled(true);
    }

    private void CleanupRuntimeArtifacts()
    {
        if (runtimeBalloonObject != null)
        {
            Destroy(runtimeBalloonObject);
            runtimeBalloonObject = null;
            runtimeBalloonBody = null;
        }

        hasRuntimeSupportAnchor = false;
        runtimeSupportAnchorLocal = Vector3.zero;
        runtimeRopeStartDistance = 0f;
        currentRopeTensionFactor = 0f;
        ropeLatched = false;
        runtimeInitialized = false;
        UpdateRopeRenderer(false);
    }

    private Vector3 GetSupportAnchorWorldPosition()
    {
        if (simulationRootBody == null)
        {
            return transform.position;
        }

        return simulationRootBody.transform.TransformPoint(runtimeSupportAnchorLocal);
    }

    private float GetRopeLimitDistance()
    {
        return Mathf.Max(0.25f, runtimeRopeStartDistance + slackRiseDistance);
    }

    private int GetSupportBalloonCount()
    {
        if (simulationRootBody == null)
        {
            return 0;
        }

        BalloonTopRise[] balloons = simulationRootBody.GetComponentsInChildren<BalloonTopRise>(true);
        int count = 0;

        foreach (BalloonTopRise balloon in balloons)
        {
            if (balloon == null || !balloon.hasRuntimeSupportAnchor || balloon.runtimeBalloonBody == null)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public static bool TryFindSupportCubeBelow(
        Transform balloonTransform,
        Transform searchRoot,
        float horizontalPadding,
        out Transform supportCube,
        out Vector3 anchorWorldPosition)
    {
        if (searchRoot == null)
        {
            supportCube = null;
            anchorWorldPosition = Vector3.zero;
            return false;
        }

        return TryFindSupportCubeBelow(
            balloonTransform,
            searchRoot.GetComponentsInChildren<Transform>(true),
            horizontalPadding,
            out supportCube,
            out anchorWorldPosition);
    }

    public static bool TryFindSupportCubeBelow(
        Transform balloonTransform,
        IEnumerable<Transform> candidateRoots,
        float horizontalPadding,
        out Transform supportCube,
        out Vector3 anchorWorldPosition)
    {
        supportCube = null;
        anchorWorldPosition = Vector3.zero;

        if (balloonTransform == null || candidateRoots == null)
        {
            return false;
        }

        Vector3 balloonPosition = balloonTransform.position;
        float bestTopY = float.NegativeInfinity;

        foreach (Transform candidate in candidateRoots)
        {
            if (candidate == null ||
                candidate == balloonTransform ||
                candidate.IsChildOf(balloonTransform) ||
                !candidate.CompareTag("Cube"))
            {
                continue;
            }

            if (!TryGetWorldBounds(candidate.gameObject, out Bounds bounds))
            {
                continue;
            }

            if (balloonPosition.x < bounds.min.x - horizontalPadding ||
                balloonPosition.x > bounds.max.x + horizontalPadding ||
                balloonPosition.z < bounds.min.z - horizontalPadding ||
                balloonPosition.z > bounds.max.z + horizontalPadding)
            {
                continue;
            }

            if (bounds.max.y > balloonPosition.y + 0.01f)
            {
                continue;
            }

            if (bounds.max.y <= bestTopY)
            {
                continue;
            }

            bestTopY = bounds.max.y;
            supportCube = candidate;
            anchorWorldPosition = new Vector3(
                Mathf.Clamp(balloonPosition.x, bounds.min.x, bounds.max.x),
                bounds.max.y,
                Mathf.Clamp(balloonPosition.z, bounds.min.z, bounds.max.z));
        }

        return supportCube != null;
    }

    private static bool TryGetWorldBounds(GameObject rootObject, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

}
