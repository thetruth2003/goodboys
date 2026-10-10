using UnityEngine;

public class pervane : MonoBehaviour
{
    private const KeyCode ActivationKey = KeyCode.F;
    private const float VerticalDirectionDotThreshold = 0.7f;
    private const float MinimumEffectiveValue = 0.0001f;

    [SerializeField, HideInInspector] private bool useWorldDirectionInput = true;
    [SerializeField, HideInInspector] private VehicleControlChannel controlChannel = VehicleControlChannel.LegacyActivationKey;
    [SerializeField, HideInInspector] private float controlActivationThreshold = 0.2f;

    [Header("Basit Guc Sistemi")]
    [Tooltip("Heli pervanesinin temel gucu. Etkin guc = powerUnits * motor carpani. Ornek: 60 guc ve 600 kutle => yuk orani 10.")]
    [Min(1f)]
    [SerializeField] private float powerUnits = 60f;

    [Tooltip("Yuk orani = toplam kutle / etkin guc. Bu degerin altinda veya esitse pervane rahat kaldirir.")]
    [Min(0.1f)]
    [SerializeField] private float fullLiftRatio = 10f;

    [Tooltip("Yuk orani bu deger ile fullLiftRatio arasindaysa pervane sabit tutar. Bu degerin ustunde yavas yavas indirir.")]
    [Min(0.1f)]
    [SerializeField] private float hoverLimitRatio = 11f;

    [Tooltip("Hover limitini gectikten sonra destegin kac oran boyunca yavas yavas azalsin. Buyurse daha gec dusurur.")]
    [Min(0.1f)]
    [SerializeField] private float overloadGraceRatio = 4f;

    [Tooltip("Yatay bakan pervanelerde hedef hiz. Motor carpani bunu arttirir.")]
    [Min(0f)]
    [SerializeField] private float horizontalTargetSpeed = 5f;

    [Tooltip("Yatay bakan pervanelerin itme sertligi. Artarsa ileri veya yana itis daha guclu olur.")]
    [Min(0f)]
    [SerializeField] private float horizontalAcceleration = 9f;

    [Header("Gorsel")]
    [Tooltip("Sadece gorunur donme hizi. Fizik guc hesabina girmez.")]
    public float spinSpeed = 200f;
    [Tooltip("Pervanenin tusa basinca ne kadar hizli donmeye baslayacagi.")]
    public float spinAcceleration = 900f;
    [Tooltip("Tus birakilinca donus gorselinin ne kadar hizli yavaslayacagi.")]
    public float spinDeceleration = 280f;

    [Header("Gecis")]
    [Tooltip("Gercek gucun ne kadar hizli acilacagi. Yuksekse tusa basinca guc daha cabuk dolar.")]
    public float thrustRampUpSpeed = 4.5f;
    [Tooltip("Gercek gucun ne kadar hizli kapanacagi. Yuksekse tusu birakinca guc daha cabuk kesilir.")]
    public float thrustRampDownSpeed = 1.8f;

    [Header("Referanslar")]
    [Tooltip("Donen gorsel obje. Heli scriptinde kuvvet yonu bu objenin up ekseninden okunur.")]
    public GameObject Pervane;

    private Rigidbody rb;
    private ModularVehicleController modularVehicleController;
    private float currentSpinRate;
    private float currentThrustFactor;
    private bool hasCachedWorldDirectionChannel;
    private VehicleControlChannel cachedWorldDirectionChannel;

    public float PowerUnits => powerUnits;

    private void Start()
    {
        ResolveRuntimeReferences();
    }

    private void Update()
    {
        ResolveRuntimeReferences();
        if (Pervane == null || rb == null)
        {
            return;
        }

        float targetSpinRate = ShouldPropellerBeActive() ? spinSpeed : 0f;
        float spinChangeRate = targetSpinRate > currentSpinRate ? spinAcceleration : spinDeceleration;
        currentSpinRate = Mathf.MoveTowards(currentSpinRate, targetSpinRate, spinChangeRate * Time.deltaTime);

        if (currentSpinRate > MinimumEffectiveValue)
        {
            Pervane.transform.Rotate(Vector3.up, currentSpinRate * Time.deltaTime, Space.Self);
        }
    }

    private void FixedUpdate()
    {
        ResolveRuntimeReferences();
        if (Pervane == null || rb == null)
        {
            return;
        }

        float targetThrustFactor = ShouldPropellerBeActive() ? 1f : 0f;
        float thrustChangeRate = targetThrustFactor > currentThrustFactor ? thrustRampUpSpeed : thrustRampDownSpeed;
        currentThrustFactor = Mathf.MoveTowards(currentThrustFactor, targetThrustFactor, thrustChangeRate * Time.fixedDeltaTime);

        if (rb.isKinematic || currentThrustFactor <= MinimumEffectiveValue)
        {
            return;
        }

        Rigidbody controlledBody = GetControlledBody();
        if (controlledBody == null || controlledBody.isKinematic)
        {
            return;
        }

        Vector3 thrustDirection = GetThrustDirection();
        float motorMultiplier = GetConnectedPowerMultiplier();
        float effectivePower = GetEffectivePower(motorMultiplier);
        float loadRatio = GetLoadRatio(controlledBody, effectivePower);

        if (IsVerticalDirection())
        {
            float supportAcceleration = CalculateVerticalSupportAcceleration(thrustDirection, loadRatio);
            if (supportAcceleration <= MinimumEffectiveValue)
            {
                return;
            }

            controlledBody.AddForceAtPosition(
                thrustDirection * supportAcceleration * currentThrustFactor,
                Pervane.transform.position,
                ForceMode.Acceleration);
            return;
        }

        float movementFactor = CalculateHorizontalMovementFactor(loadRatio);
        if (movementFactor <= MinimumEffectiveValue)
        {
            return;
        }

        float targetSpeed = horizontalTargetSpeed * motorMultiplier * movementFactor;
        float currentSpeedAlongDirection = Vector3.Dot(controlledBody.GetPointVelocity(Pervane.transform.position), thrustDirection);
        float speedError = targetSpeed - currentSpeedAlongDirection;
        if (speedError <= MinimumEffectiveValue)
        {
            return;
        }

        float commandedAcceleration = Mathf.Min(
            horizontalAcceleration * motorMultiplier * movementFactor,
            speedError * horizontalAcceleration) * currentThrustFactor;

        if (commandedAcceleration <= MinimumEffectiveValue)
        {
            return;
        }

        controlledBody.AddForceAtPosition(
            thrustDirection * commandedAcceleration,
            Pervane.transform.position,
            ForceMode.Acceleration);
    }

    private void ResolveRuntimeReferences()
    {
        if (rb == null)
        {
            rb = GetComponentInParent<Rigidbody>();
        }

        if (modularVehicleController == null)
        {
            modularVehicleController = GetComponentInParent<ModularVehicleController>();
        }

        UpdateWorldDirectionControlCache();
    }

    private bool ShouldPropellerBeActive()
    {
        if (rb == null || rb.isKinematic || !CanAcceptControlInput())
        {
            return false;
        }

        if (modularVehicleController == null)
        {
            modularVehicleController = GetComponentInParent<ModularVehicleController>();
        }

        if (modularVehicleController != null && !modularVehicleController.IsPropellerSystemEnabled)
        {
            return false;
        }

        if (useWorldDirectionInput)
        {
            if (IsVerticalDirection())
            {
                return Input.GetKey(ActivationKey);
            }

            if (modularVehicleController == null)
            {
                modularVehicleController = GetComponentInParent<ModularVehicleController>();
            }

            if (modularVehicleController == null)
            {
                return false;
            }

            VehicleControlChannel worldDirectionChannel = hasCachedWorldDirectionChannel
                ? cachedWorldDirectionChannel
                : ResolveWorldDirectionControlChannel();

            return modularVehicleController.GetControlChannelValue(worldDirectionChannel) > controlActivationThreshold;
        }

        if (controlChannel == VehicleControlChannel.LegacyActivationKey)
        {
            return Input.GetKey(ActivationKey);
        }

        if (modularVehicleController == null)
        {
            modularVehicleController = GetComponentInParent<ModularVehicleController>();
        }

        return modularVehicleController != null &&
               modularVehicleController.GetControlChannelValue(controlChannel) > controlActivationThreshold;
    }

    private bool CanAcceptControlInput()
    {
        if (modularVehicleController == null)
        {
            modularVehicleController = GetComponentInParent<ModularVehicleController>();
        }

        return modularVehicleController == null || modularVehicleController.IsDriveControlAvailable;
    }

    private void UpdateWorldDirectionControlCache()
    {
        if (!useWorldDirectionInput)
        {
            hasCachedWorldDirectionChannel = false;
            return;
        }

        if (PlayerController.BuildModeActive)
        {
            hasCachedWorldDirectionChannel = false;
            return;
        }

        if (hasCachedWorldDirectionChannel || Pervane == null)
        {
            return;
        }

        cachedWorldDirectionChannel = ResolveWorldDirectionControlChannel();
        hasCachedWorldDirectionChannel = true;
    }

    private VehicleControlChannel ResolveWorldDirectionControlChannel()
    {
        Vector3 horizontalDirection = Vector3.ProjectOnPlane(GetThrustDirection(), Vector3.up);
        if (horizontalDirection.sqrMagnitude <= MinimumEffectiveValue)
        {
            return GetFallbackControlChannel();
        }

        horizontalDirection.Normalize();

        if (Mathf.Abs(horizontalDirection.x) >= Mathf.Abs(horizontalDirection.z))
        {
            return horizontalDirection.x >= 0f
                ? VehicleControlChannel.SteeringRight
                : VehicleControlChannel.SteeringLeft;
        }

        return horizontalDirection.z >= 0f
            ? VehicleControlChannel.ThrottleForward
            : VehicleControlChannel.ThrottleReverse;
    }

    private VehicleControlChannel GetFallbackControlChannel()
    {
        return controlChannel == VehicleControlChannel.LegacyActivationKey
            ? VehicleControlChannel.ThrottleForward
            : controlChannel;
    }

    private bool IsVerticalDirection()
    {
        return Mathf.Abs(Vector3.Dot(GetThrustDirection(), Vector3.up)) >= VerticalDirectionDotThreshold;
    }

    private Vector3 GetThrustDirection()
    {
        return (Pervane != null ? Pervane.transform.up : transform.up).normalized;
    }

    private Rigidbody GetControlledBody()
    {
        Rigidbody primaryBody = BuildVehicleUtility.GetPrimaryConnectedBody(rb);
        return primaryBody != null ? primaryBody : rb;
    }

    private float GetConnectedPowerMultiplier()
    {
        return BuildVehicleUtility.GetConnectedPowerMultiplier(rb);
    }

    private float GetEffectivePower(float motorMultiplier)
    {
        return Mathf.Max(1f, powerUnits * Mathf.Max(1f, motorMultiplier));
    }

    private float GetLoadRatio(Rigidbody controlledBody, float effectivePower)
    {
        if (controlledBody == null)
        {
            return float.PositiveInfinity;
        }

        return controlledBody.mass / Mathf.Max(1f, effectivePower);
    }

    private float CalculateVerticalSupportAcceleration(Vector3 thrustDirection, float loadRatio)
    {
        float gravityCompensation = Mathf.Max(0f, -Vector3.Dot(Physics.gravity, thrustDirection));
        if (gravityCompensation <= MinimumEffectiveValue)
        {
            return 0f;
        }

        if (loadRatio <= fullLiftRatio)
        {
            float surplusFactor = Mathf.Clamp01((fullLiftRatio - loadRatio) / Mathf.Max(fullLiftRatio, 0.01f));
            return gravityCompensation * (1f + surplusFactor);
        }

        if (loadRatio <= hoverLimitRatio)
        {
            return gravityCompensation;
        }

        float overloadFactor = Mathf.Clamp01(1f - ((loadRatio - hoverLimitRatio) / Mathf.Max(overloadGraceRatio, 0.01f)));
        return gravityCompensation * overloadFactor;
    }

    private float CalculateHorizontalMovementFactor(float loadRatio)
    {
        if (loadRatio <= fullLiftRatio)
        {
            float surplusFactor = Mathf.Clamp01((fullLiftRatio - loadRatio) / Mathf.Max(fullLiftRatio, 0.01f));
            return 1f + surplusFactor * 0.75f;
        }

        if (loadRatio >= hoverLimitRatio)
        {
            return 0f;
        }

        return Mathf.InverseLerp(hoverLimitRatio, fullLiftRatio, loadRatio);
    }
}
