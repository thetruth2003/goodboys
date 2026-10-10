using UnityEngine;

public class FrontWheele : MonoBehaviour
{
    private const string RuntimeWheelColliderSuffix = "_RuntimeWheelCollider";

    private bool runtimeConfigured;
    private Quaternion visualPoseOffset = Quaternion.identity;
    private Vector3 visualPositionOffsetLocal;
    private bool visualPoseOffsetCaptured;
    private Transform runtimeWheelColliderTransform;
    private Transform sourceWheelColliderTransform;
    private bool visualRootResolved;
    private bool wheelDirectionsResolved;
    private float automaticMotorDirection = 1f;
    private float automaticSteeringDirection = 1f;
    private float tractionTorqueScale = 1f;

    public bool preserveYRotation;

    [SerializeField] private float motorForce;
    [SerializeField] private float breakForce;
    [SerializeField] private float maxSteerAngle;
    [SerializeField] private WheelCollider WheelCollider;
    [SerializeField] private Transform WheelTransform;
    [SerializeField] private bool allowDrive = true;
    [SerializeField] private bool allowSteering = true;
    [SerializeField] private bool autoCorrectMotorDirection = true;
    [SerializeField] private bool autoCorrectSteeringDirection = true;
    [SerializeField] private bool invertMotorDirection;
    [SerializeField] private bool invertSteeringDirection;
    [SerializeField] private bool autoFlipVisualBySide = true;
    [SerializeField] private float rightSideVisualYawOffset;
    [SerializeField] private float leftSideVisualYawOffset = 180f;
    [SerializeField] private float idleBrakeForce = 250f;
    [SerializeField] private bool autoTuneWheelCollider = true;
    [SerializeField] private float visualRadiusMultiplier = 0.95f;
    [SerializeField] private float tunedWheelMass = 2f;
    [SerializeField] private float tunedSuspensionDistance = 0.3f;
    [SerializeField] private float tunedWheelDampingRate = 0.25f;
    [SerializeField] private float tunedForwardStiffness = 1f;
    [SerializeField] private float tunedSidewaysStiffness = 1f;
    [SerializeField] private float driveInputSign = 1f;
    [SerializeField] private float minimumIdleBrakeForce = 120f;
    [SerializeField] private float minimumWheelDampingRate = 0.25f;
    [SerializeField] private float minimumForwardStiffness = 1f;
    [SerializeField] private float minimumSidewaysStiffness = 1f;
    [SerializeField] private float tractionControlSlipStart = 0.25f;
    [SerializeField] private float tractionControlSlipMax = 0.75f;
    [SerializeField] [Range(0.05f, 1f)] private float tractionControlTorqueFactor = 0.2f;

    private void Awake()
    {
        AutoSetupForSimulation();
    }

    private void FixedUpdate()
    {
        if (!IsSimulationWheel())
        {
            return;
        }

        ResolveRuntimeReferences();
        SyncVisual();
    }

    private void OnEnable()
    {
        AutoSetupForSimulation();
    }

    public void AutoSetupForSimulation()
    {
        ResolveRuntimeReferences();

        if (!IsSimulationWheel())
        {
            SetWheelColliderEnabled(false);
            return;
        }

        if (!runtimeConfigured)
        {
            ConfigureWheelColliders();
            runtimeConfigured = true;
        }

        ResolveAutomaticWheelDirections();
        CaptureVisualPoseOffset();
        SetWheelColliderEnabled(true);
    }

    public WheelCollider RuntimeWheelCollider => WheelCollider;

    public Transform RuntimeWheelTransform => WheelTransform;

    public float MotorForce => motorForce;

    public float BrakeForce => breakForce;

    public float MaxSteerAngle => maxSteerAngle;

    public float IdleBrakeForce => idleBrakeForce;

    public float GetMotorDirectionSign()
    {
        return GetMotorDirectionMultiplier();
    }

    public float GetSteeringDirectionSign()
    {
        return GetSteeringDirectionMultiplier();
    }

    public void SetAutomaticDirectionReference(Vector3 referenceForward, Vector3 referenceUp)
    {
        if (WheelCollider == null)
        {
            return;
        }

        Vector3 wheelForward = Vector3.ProjectOnPlane(WheelCollider.transform.forward, referenceUp);
        Vector3 targetForward = Vector3.ProjectOnPlane(referenceForward, referenceUp);
        if (wheelForward.sqrMagnitude < 0.001f || targetForward.sqrMagnitude < 0.001f)
        {
            return;
        }

        float alignment = Vector3.Dot(wheelForward.normalized, targetForward.normalized);
        if (Mathf.Abs(alignment) >= 0.5f)
        {
            automaticMotorDirection = alignment < 0f ? 1f : -1f;
        }

        automaticSteeringDirection = Vector3.Dot(WheelCollider.transform.up, referenceUp) < 0f
            ? -1f
            : 1f;

        wheelDirectionsResolved = true;
    }

    public void SetWheelColliderEnabled(bool enabled)
    {
        if (WheelCollider != null)
        {
            WheelCollider.enabled = enabled;
        }
    }

    public void ApplyDriveTorque(float driveInput)
    {
        if (WheelCollider == null || !WheelCollider.enabled)
        {
            return;
        }

        float motorInput = allowDrive ? driveInput * GetDriveInputSign() : 0f;
        motorInput = ApplyTractionControl(motorInput);
        WheelCollider.motorTorque = motorInput * motorForce * GetMotorDirectionMultiplier();
    }

    public void ApplyBrakeTorque(bool braking, float driveInput)
    {
        if (WheelCollider == null || !WheelCollider.enabled)
        {
            return;
        }

        float motorInput = allowDrive ? driveInput * GetDriveInputSign() : 0f;
        bool shouldHoldWheelStill = Mathf.Abs(motorInput) < 0.05f;
        float passiveBrakeForce = shouldHoldWheelStill ? Mathf.Max(idleBrakeForce, minimumIdleBrakeForce) : 0f;
        float brakeTorque = braking ? breakForce : passiveBrakeForce;
        WheelCollider.brakeTorque = brakeTorque;
    }

    public void ApplySteering(float steeringInput)
    {
        if (WheelCollider == null || !WheelCollider.enabled)
        {
            return;
        }

        float steerAngle = allowSteering ? maxSteerAngle * steeringInput : 0f;
        WheelCollider.steerAngle = steerAngle * GetSteeringDirectionMultiplier();
    }

    public void SyncVisual()
    {
        UpdateWheelVisual();
    }

    public bool CanDrive => allowDrive;

    public bool CanSteer => allowSteering;

    public void SetSteeringEnabled(bool enabled)
    {
        allowSteering = enabled;
    }

    public Vector3 GetBuildForwardDirection()
    {
        return transform.forward * GetMotorDirectionMultiplier();
    }

    public float GetEstimatedWorldRadius()
    {
        if (WheelCollider != null)
        {
            return WheelCollider.radius * GetWheelColliderScaleCompensation(WheelCollider.transform);
        }

        if (WheelTransform != null)
        {
            Renderer renderer = WheelTransform.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                return Mathf.Max(renderer.bounds.extents.y, renderer.bounds.extents.z);
            }
        }

        return 0.35f;
    }

    private void UpdateWheelVisual()
    {
        if (WheelCollider == null || WheelTransform == null || !WheelCollider.enabled)
        {
            return;
        }

        WheelCollider.GetWorldPose(out Vector3 posePosition, out Quaternion poseRotation);

        Quaternion visualRotation = poseRotation * visualPoseOffset;
        if (preserveYRotation)
        {
            Vector3 euler = visualRotation.eulerAngles;
            euler.y = WheelTransform.rotation.eulerAngles.y;
            visualRotation = Quaternion.Euler(euler);
        }

        Vector3 axleOffset = Vector3.right * visualPositionOffsetLocal.x;
        Vector3 fixedOffset = new Vector3(0f, visualPositionOffsetLocal.y, visualPositionOffsetLocal.z);
        Vector3 worldPositionOffset = poseRotation * axleOffset +
                                      WheelCollider.transform.TransformVector(fixedOffset);
        WheelTransform.position = posePosition + worldPositionOffset;
        WheelTransform.rotation = visualRotation;
    }

    private float GetMotorDirectionMultiplier()
    {
        float manualDirection = invertMotorDirection ? -1f : 1f;
        return manualDirection * (autoCorrectMotorDirection ? automaticMotorDirection : 1f);
    }

    private float GetSteeringDirectionMultiplier()
    {
        float manualDirection = invertSteeringDirection ? -1f : 1f;
        return manualDirection * (autoCorrectSteeringDirection ? automaticSteeringDirection : 1f);
    }

    private void ResolveRuntimeReferences()
    {
        ResolveWheelVisualRoot();

        bool isSimulationWheel = IsSimulationWheel();
        if (isSimulationWheel)
        {
            EnsureDetachedSimulationWheelCollider();
        }
        else if (WheelCollider == null)
        {
            WheelCollider = GetComponentInChildren<WheelCollider>(true);
        }
    }

    private void ConfigureWheelColliders()
    {
        if (!autoTuneWheelCollider)
        {
            return;
        }

        ConfigureWheelCollider(WheelCollider, WheelTransform);
    }

    private void ConfigureWheelCollider(WheelCollider wheelCollider, Transform visualTransform)
    {
        if (wheelCollider == null)
        {
            return;
        }

        AlignWheelColliderTransform(wheelCollider, visualTransform);

        if (visualTransform != null)
        {
            Renderer visualRenderer = visualTransform.GetComponentInChildren<Renderer>();
            if (visualRenderer != null)
            {
                float worldRadius = Mathf.Max(
                    visualRenderer.bounds.extents.y,
                    visualRenderer.bounds.extents.z) * visualRadiusMultiplier;
                float scaleCompensation = GetWheelColliderScaleCompensation(wheelCollider.transform);
                float fittedRadius = worldRadius / scaleCompensation;

                if (fittedRadius > 0.05f)
                {
                    wheelCollider.radius = fittedRadius;
                }
            }
        }

        wheelCollider.mass = tunedWheelMass;
        wheelCollider.suspensionDistance = tunedSuspensionDistance / GetWheelColliderScaleCompensation(wheelCollider.transform);
        wheelCollider.center = new Vector3(0f, wheelCollider.suspensionDistance * 0.5f, 0f);
        wheelCollider.wheelDampingRate = Mathf.Max(tunedWheelDampingRate, minimumWheelDampingRate);
        wheelCollider.forceAppPointDistance = 0f;
        wheelCollider.suspensionExpansionLimited = true;
        wheelCollider.ConfigureVehicleSubsteps(8f, 6, 10);

        JointSpring suspensionSpring = wheelCollider.suspensionSpring;
        suspensionSpring.spring = 2000f;
        suspensionSpring.damper = 450f;
        suspensionSpring.targetPosition = 0.5f;
        wheelCollider.suspensionSpring = suspensionSpring;

        WheelFrictionCurve forwardFriction = wheelCollider.forwardFriction;
        forwardFriction.extremumSlip = 0.4f;
        forwardFriction.extremumValue = 1f;
        forwardFriction.asymptoteSlip = 0.8f;
        forwardFriction.asymptoteValue = 0.5f;
        forwardFriction.stiffness = Mathf.Max(tunedForwardStiffness, minimumForwardStiffness);
        wheelCollider.forwardFriction = forwardFriction;

        WheelFrictionCurve sidewaysFriction = wheelCollider.sidewaysFriction;
        sidewaysFriction.extremumSlip = 0.2f;
        sidewaysFriction.extremumValue = 1f;
        sidewaysFriction.asymptoteSlip = 0.5f;
        sidewaysFriction.asymptoteValue = 0.75f;
        sidewaysFriction.stiffness = Mathf.Max(tunedSidewaysStiffness, minimumSidewaysStiffness);
        wheelCollider.sidewaysFriction = sidewaysFriction;
    }

    private float ApplyTractionControl(float motorInput)
    {
        if (WheelCollider == null)
        {
            return motorInput;
        }

        float targetTorqueScale = 1f;
        if (!Mathf.Approximately(motorInput, 0f) &&
            WheelCollider.GetGroundHit(out WheelHit hit))
        {
            float slipStart = Mathf.Max(0.01f, tractionControlSlipStart);
            float slipMax = Mathf.Max(slipStart + 0.01f, tractionControlSlipMax);
            float slipAmount = Mathf.Abs(hit.forwardSlip);

            if (slipAmount > slipStart)
            {
                float reduction = Mathf.InverseLerp(slipStart, slipMax, slipAmount);
                targetTorqueScale = Mathf.Lerp(1f, tractionControlTorqueFactor, reduction);
            }
        }

        tractionTorqueScale = Mathf.MoveTowards(
            tractionTorqueScale,
            targetTorqueScale,
            6f * Time.fixedDeltaTime);
        return motorInput * tractionTorqueScale;
    }

    private float GetDriveInputSign()
    {
        return Mathf.Approximately(driveInputSign, 0f) ? 1f : Mathf.Sign(driveInputSign);
    }

    private void ResolveAutomaticWheelDirections()
    {
        if (wheelDirectionsResolved || WheelCollider == null)
        {
            return;
        }

        ModularVehicleController vehicleController = GetComponentInParent<ModularVehicleController>(true);
        if (vehicleController == null)
        {
            return;
        }

        SetAutomaticDirectionReference(
            vehicleController.GetFlightReferenceForward(),
            vehicleController.transform.up);
    }

    private void CaptureVisualPoseOffset()
    {
        if (visualPoseOffsetCaptured)
        {
            return;
        }

        if (WheelCollider == null || WheelTransform == null)
        {
            visualPoseOffset = Quaternion.identity;
            visualPositionOffsetLocal = Vector3.zero;
            return;
        }

        WheelCollider.GetWorldPose(out Vector3 posePosition, out _);
        visualPositionOffsetLocal = WheelCollider.transform.InverseTransformVector(
            WheelTransform.position - posePosition);
        visualPoseOffset = Quaternion.Inverse(WheelCollider.transform.rotation) * WheelTransform.rotation;
        visualPoseOffsetCaptured = true;
    }

    private bool IsSimulationWheel()
    {
        return GetComponentInParent<ModularVehicleController>(true) != null;
    }

    private float GetWheelColliderScaleCompensation(Transform wheelColliderTransform)
    {
        Vector3 lossyScale = wheelColliderTransform.lossyScale;
        float largestScale = Mathf.Max(
            Mathf.Abs(lossyScale.x),
            Mathf.Abs(lossyScale.y),
            Mathf.Abs(lossyScale.z));

        return Mathf.Max(0.01f, largestScale);
    }

    private Transform FindBestWheelVisualTransform()
    {
        foreach (Transform child in transform)
        {
            if (IsSharedWheelVisualContainer(child) &&
                child.GetComponentInChildren<Renderer>(true) != null)
            {
                return child;
            }
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Renderer bestRenderer = null;
        float bestScore = float.MinValue;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer.transform == transform)
            {
                continue;
            }

            float score = renderer.bounds.size.sqrMagnitude;
            if (score > bestScore)
            {
                bestScore = score;
                bestRenderer = renderer;
            }
        }

        return bestRenderer != null ? PromoteToSharedVisualRoot(bestRenderer.transform) : transform;
    }

    private void ResolveWheelVisualRoot()
    {
        if (visualRootResolved && WheelTransform != null)
        {
            return;
        }

        Transform visualCandidate = WheelTransform != null
            ? WheelTransform
            : FindBestWheelVisualTransform();
        WheelTransform = PromoteToSharedVisualRoot(visualCandidate);
        visualRootResolved = WheelTransform != null;
    }

    private Transform PromoteToSharedVisualRoot(Transform visualCandidate)
    {
        if (visualCandidate == null)
        {
            return null;
        }

        if (IsSharedWheelVisualContainer(visualCandidate))
        {
            return visualCandidate;
        }

        Transform candidate = visualCandidate;
        while (candidate.parent != null && candidate.parent != transform)
        {
            if (IsSharedWheelVisualContainer(candidate.parent))
            {
                return candidate.parent;
            }

            candidate = candidate.parent;
        }

        return visualCandidate;
    }

    private static bool IsSharedWheelVisualContainer(Transform candidate)
    {
        return candidate != null &&
               (string.Equals(candidate.name, "wheels", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.name, "meshes", System.StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureDetachedSimulationWheelCollider()
    {
        ModularVehicleController vehicleController = GetComponentInParent<ModularVehicleController>(true);
        if (vehicleController == null)
        {
            return;
        }

        CacheSourceWheelColliderTransform();

        bool createdRuntimeCollider = runtimeWheelColliderTransform == null;
        if (createdRuntimeCollider)
        {
            GameObject runtimeColliderObject = new GameObject($"{gameObject.name}{RuntimeWheelColliderSuffix}");
            runtimeWheelColliderTransform = runtimeColliderObject.transform;
            runtimeWheelColliderTransform.SetParent(vehicleController.transform, true);
        }

        if (createdRuntimeCollider)
        {
            Transform referenceTransform = sourceWheelColliderTransform != null
                ? sourceWheelColliderTransform
                : (WheelTransform != null ? WheelTransform : transform);
            runtimeWheelColliderTransform.SetPositionAndRotation(referenceTransform.position, referenceTransform.rotation);
        }

        runtimeWheelColliderTransform.localScale = Vector3.one;

        WheelCollider runtimeWheelCollider = runtimeWheelColliderTransform.GetComponent<WheelCollider>();
        if (runtimeWheelCollider == null)
        {
            runtimeWheelCollider = runtimeWheelColliderTransform.gameObject.AddComponent<WheelCollider>();
        }

        DisableNestedWheelColliders(runtimeWheelCollider);
        DisableWheelVisualColliders();
        WheelCollider = runtimeWheelCollider;
    }

    private void CacheSourceWheelColliderTransform()
    {
        if (sourceWheelColliderTransform != null)
        {
            return;
        }

        if (WheelCollider != null && WheelCollider.transform != runtimeWheelColliderTransform)
        {
            sourceWheelColliderTransform = WheelCollider.transform;
            return;
        }

        WheelCollider[] nestedWheelColliders = GetComponentsInChildren<WheelCollider>(true);
        foreach (WheelCollider nestedWheelCollider in nestedWheelColliders)
        {
            if (nestedWheelCollider != null && nestedWheelCollider.transform != runtimeWheelColliderTransform)
            {
                sourceWheelColliderTransform = nestedWheelCollider.transform;
                return;
            }
        }
    }

    private void DisableNestedWheelColliders(WheelCollider activeWheelCollider)
    {
        WheelCollider[] nestedWheelColliders = GetComponentsInChildren<WheelCollider>(true);
        foreach (WheelCollider nestedWheelCollider in nestedWheelColliders)
        {
            if (nestedWheelCollider != null && nestedWheelCollider != activeWheelCollider)
            {
                nestedWheelCollider.enabled = false;
            }
        }
    }

    private void DisableWheelVisualColliders()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider colliderComponent in colliders)
        {
            if (colliderComponent == null || colliderComponent is WheelCollider)
            {
                continue;
            }

            colliderComponent.enabled = false;
        }
    }

    private void AlignWheelColliderTransform(WheelCollider wheelCollider, Transform visualTransform)
    {
        if (wheelCollider == null || visualTransform == null)
        {
            return;
        }

        if (runtimeWheelColliderTransform != null && wheelCollider.transform == runtimeWheelColliderTransform)
        {
            Transform referenceTransform = sourceWheelColliderTransform != null
                ? sourceWheelColliderTransform
                : visualTransform;
            wheelCollider.transform.SetPositionAndRotation(referenceTransform.position, referenceTransform.rotation);
            wheelCollider.transform.localScale = Vector3.one;
        }
    }
}
