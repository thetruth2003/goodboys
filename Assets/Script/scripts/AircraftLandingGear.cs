using UnityEngine;

public class AircraftLandingGear : MonoBehaviour
{
    private const string RuntimeColliderSuffix = "_RuntimeLandingGearCollider";

    [Header("Wheel")]
    [SerializeField] private WheelCollider sourceWheelCollider;
    [SerializeField] private Transform wheelVisual;
    [SerializeField] private float wheelRadius = 0.4f;
    [SerializeField] private float suspensionDistance = 0.1f;
    [SerializeField] private float wheelMass = 2f;
    [SerializeField] private float suspensionSpring = 35000f;
    [SerializeField] private float suspensionDamper = 4500f;
    [SerializeField] private float forwardGrip = 0.65f;
    [SerializeField] private float sidewaysGrip = 1f;

    [Header("Steering")]
    [SerializeField] private bool allowSteering;
    [SerializeField] private float maxSteerAngle = 30f;

    [Header("Ground balance")]
    [SerializeField] private bool enableGroundRollAssist = true;
    [SerializeField] private float rollStrength = 14f;
    [SerializeField] private float rollDamping = 5f;
    [SerializeField] private float maxRollAcceleration = 16f;

    private ModularVehicleController vehicleController;
    private Rigidbody vehicleBody;
    private WheelCollider runtimeWheelCollider;
    private Transform runtimeColliderTransform;
    private Quaternion visualRotationOffset = Quaternion.identity;
    private Vector3 visualPositionOffset;
    private bool visualOffsetCaptured;
    private bool runtimeConfigured;

    public WheelCollider RuntimeWheelCollider => runtimeWheelCollider;

    private void Awake()
    {
        TrySetupForSimulation();
    }

    private void OnEnable()
    {
        TrySetupForSimulation();
    }

    private void FixedUpdate()
    {
        if (PlayerController.BuildModeActive)
        {
            SetAllWheelCollidersEnabled(false);
            return;
        }

        if (!TrySetupForSimulation())
        {
            return;
        }

        runtimeWheelCollider.motorTorque = 0f;
        runtimeWheelCollider.brakeTorque = 0f;
        runtimeWheelCollider.steerAngle = GetSteerAngle();

        SyncWheelVisual();
        ApplySharedGroundRollAssist();
    }

    public bool TrySetupForSimulation()
    {
        if (vehicleController == null)
        {
            vehicleController = GetComponentInParent<ModularVehicleController>(true);
        }

        if (vehicleController == null)
        {
            SetAllWheelCollidersEnabled(false);
            return false;
        }

        if (vehicleBody == null)
        {
            vehicleBody = vehicleController.GetComponent<Rigidbody>();
        }

        if (vehicleBody == null || wheelVisual == null)
        {
            return false;
        }

        EnsureRuntimeWheelCollider();
        if (!runtimeConfigured)
        {
            ConfigureRuntimeWheelCollider();
            runtimeConfigured = true;
        }

        if (sourceWheelCollider != null && sourceWheelCollider != runtimeWheelCollider)
        {
            sourceWheelCollider.enabled = false;
        }

        CaptureVisualRotationOffset();
        return runtimeWheelCollider != null;
    }

    private void EnsureRuntimeWheelCollider()
    {
        if (runtimeWheelCollider != null)
        {
            return;
        }

        if (sourceWheelCollider != null)
        {
            runtimeWheelCollider = sourceWheelCollider;
            runtimeColliderTransform = sourceWheelCollider.transform;
            return;
        }

        GameObject colliderObject = new GameObject($"{name}{RuntimeColliderSuffix}");
        runtimeColliderTransform = colliderObject.transform;
        runtimeColliderTransform.SetParent(vehicleController.transform, true);
        if (runtimeWheelCollider != sourceWheelCollider)
        {
            runtimeColliderTransform.SetPositionAndRotation(wheelVisual.position, transform.rotation);
            runtimeColliderTransform.localScale = Vector3.one;
        }
        runtimeWheelCollider = colliderObject.AddComponent<WheelCollider>();
    }

    private void ConfigureRuntimeWheelCollider()
    {
        if (runtimeWheelCollider == null)
        {
            return;
        }

        runtimeColliderTransform.SetPositionAndRotation(wheelVisual.position, transform.rotation);
        runtimeColliderTransform.localScale = Vector3.one;

        float scale = GetLargestWorldScale(transform);
        runtimeWheelCollider.radius = Mathf.Max(0.05f, wheelRadius * scale);
        runtimeWheelCollider.center = Vector3.zero;
        runtimeWheelCollider.mass = Mathf.Max(0.1f, wheelMass);
        runtimeWheelCollider.suspensionDistance = Mathf.Max(0.01f, suspensionDistance * scale);
        // This is an unpowered caster/landing wheel. A low damping value lets the
        // propeller or gravity roll it instead of making it feel hand-braked.
        runtimeWheelCollider.wheelDampingRate = 0.05f;
        runtimeWheelCollider.forceAppPointDistance = 0f;

        JointSpring spring = runtimeWheelCollider.suspensionSpring;
        spring.targetPosition = 0.5f;
        float supportedMass = GetSupportedMassPerLandingGear();
        float restCompression = Mathf.Max(0.01f, runtimeWheelCollider.suspensionDistance * spring.targetPosition);
        float loadBearingSpring = supportedMass * Physics.gravity.magnitude / restCompression;
        spring.spring = Mathf.Max(100f, suspensionSpring, loadBearingSpring);

        float criticalDamping = 2f * Mathf.Sqrt(spring.spring * supportedMass);
        spring.damper = Mathf.Max(10f, suspensionDamper, criticalDamping * 0.65f);
        runtimeWheelCollider.suspensionSpring = spring;

        WheelFrictionCurve forward = runtimeWheelCollider.forwardFriction;
        forward.extremumSlip = 0.4f;
        forward.extremumValue = 1f;
        forward.asymptoteSlip = 0.8f;
        forward.asymptoteValue = 0.5f;
        forward.stiffness = Mathf.Max(0.05f, forwardGrip);
        runtimeWheelCollider.forwardFriction = forward;

        WheelFrictionCurve sideways = runtimeWheelCollider.sidewaysFriction;
        sideways.extremumSlip = 0.2f;
        sideways.extremumValue = 1f;
        sideways.asymptoteSlip = 0.5f;
        sideways.asymptoteValue = 0.75f;
        sideways.stiffness = Mathf.Max(0.05f, sidewaysGrip);
        runtimeWheelCollider.sidewaysFriction = sideways;

        runtimeWheelCollider.motorTorque = 0f;
        runtimeWheelCollider.brakeTorque = 0f;
        runtimeWheelCollider.steerAngle = GetSteerAngle();
        runtimeWheelCollider.enabled = true;
    }

    private void CaptureVisualRotationOffset()
    {
        if (visualOffsetCaptured || runtimeWheelCollider == null || wheelVisual == null)
        {
            return;
        }

        runtimeWheelCollider.GetWorldPose(out Vector3 wheelPosition, out Quaternion wheelRotation);
        visualRotationOffset = Quaternion.Inverse(wheelRotation) * wheelVisual.rotation;
        visualPositionOffset = runtimeColliderTransform.InverseTransformVector(wheelVisual.position - wheelPosition);
        visualOffsetCaptured = true;
    }

    private void SyncWheelVisual()
    {
        if (runtimeWheelCollider == null || wheelVisual == null)
        {
            return;
        }

        runtimeWheelCollider.GetWorldPose(out Vector3 position, out Quaternion rotation);
        wheelVisual.SetPositionAndRotation(
            position + runtimeColliderTransform.TransformVector(visualPositionOffset),
            rotation * visualRotationOffset);
    }

    private void ApplySharedGroundRollAssist()
    {
        if (!enableGroundRollAssist || vehicleBody == null || vehicleController == null)
        {
            return;
        }

        AircraftLandingGear[] gears = vehicleController.GetComponentsInChildren<AircraftLandingGear>(true);
        AircraftLandingGear assistLeader = null;
        int assistGearCount = 0;
        foreach (AircraftLandingGear gear in gears)
        {
            if (gear != null && gear.enableGroundRollAssist)
            {
                assistGearCount++;
                if (assistLeader == null)
                {
                    assistLeader = gear;
                }
            }
        }

        if (assistLeader != this || assistGearCount < 2)
        {
            return;
        }

        bool anyGrounded = false;
        foreach (AircraftLandingGear gear in gears)
        {
            if (gear != null &&
                gear.enableGroundRollAssist &&
                gear.runtimeWheelCollider != null &&
                gear.runtimeWheelCollider.isGrounded)
            {
                anyGrounded = true;
                break;
            }
        }

        if (!anyGrounded)
        {
            return;
        }

        Vector3 rollAxis = FindLandingGearAxis(gears);
        Vector3 currentUp = Vector3.ProjectOnPlane(vehicleController.transform.up, rollAxis);
        Vector3 targetUp = Vector3.ProjectOnPlane(Vector3.up, rollAxis);
        if (currentUp.sqrMagnitude < 0.0001f || targetUp.sqrMagnitude < 0.0001f)
        {
            return;
        }

        currentUp.Normalize();
        targetUp.Normalize();
        float rollAngle = Vector3.SignedAngle(currentUp, targetUp, rollAxis) * Mathf.Deg2Rad;
        float rollSpeed = Vector3.Dot(vehicleBody.angularVelocity, rollAxis);
        float acceleration = rollAngle * rollStrength - rollSpeed * rollDamping;
        acceleration = Mathf.Clamp(acceleration, -maxRollAcceleration, maxRollAcceleration);
        vehicleBody.AddTorque(rollAxis * acceleration, ForceMode.Acceleration);
    }

    private Vector3 FindLandingGearAxis(AircraftLandingGear[] gears)
    {
        float bestDistanceSqr = 0f;
        Vector3 bestAxis = vehicleController.transform.forward;

        for (int i = 0; i < gears.Length; i++)
        {
            if (gears[i] == null || !gears[i].enableGroundRollAssist)
            {
                continue;
            }

            for (int j = i + 1; j < gears.Length; j++)
            {
                if (gears[j] == null || !gears[j].enableGroundRollAssist)
                {
                    continue;
                }

                Vector3 difference = gears[j].transform.position - gears[i].transform.position;
                float distanceSqr = difference.sqrMagnitude;
                if (distanceSqr > bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestAxis = difference.normalized;
                }
            }
        }

        if (bestAxis.sqrMagnitude < 0.0001f)
        {
            bestAxis = transform.forward;
        }

        return bestAxis.normalized;
    }

    private float GetSupportedMassPerLandingGear()
    {
        if (vehicleBody == null || vehicleController == null)
        {
            return Mathf.Max(1f, wheelMass);
        }

        int landingGearCount = 0;
        AircraftLandingGear[] gears = vehicleController.GetComponentsInChildren<AircraftLandingGear>(true);
        foreach (AircraftLandingGear gear in gears)
        {
            if (gear != null && gear.isActiveAndEnabled)
            {
                landingGearCount++;
            }
        }

        return vehicleBody.mass / Mathf.Max(1, landingGearCount);
    }

    private float GetSteerAngle()
    {
        if (!allowSteering || vehicleController == null)
        {
            return 0f;
        }

        float direction = Vector3.Dot(runtimeColliderTransform.forward, vehicleController.transform.forward) < 0f
            ? -1f
            : 1f;
        return vehicleController.CurrentSteeringInput * maxSteerAngle * direction;
    }

    private void SetAllWheelCollidersEnabled(bool enabled)
    {
        if (sourceWheelCollider != null)
        {
            sourceWheelCollider.enabled = enabled;
        }

        if (runtimeWheelCollider != null && runtimeWheelCollider != sourceWheelCollider)
        {
            runtimeWheelCollider.enabled = enabled;
        }
    }

    private static float GetLargestWorldScale(Transform target)
    {
        Vector3 scale = target.lossyScale;
        return Mathf.Max(0.01f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

#if UNITY_EDITOR
    public void ConfigurePrefab(
        WheelCollider wheelCollider,
        Transform visual,
        float radius,
        float suspension,
        float mass,
        bool steering,
        bool groundRollAssist)
    {
        sourceWheelCollider = wheelCollider;
        wheelVisual = visual;
        wheelRadius = radius;
        suspensionDistance = suspension;
        wheelMass = mass;
        allowSteering = steering;
        enableGroundRollAssist = groundRollAssist;
    }
#endif
}
