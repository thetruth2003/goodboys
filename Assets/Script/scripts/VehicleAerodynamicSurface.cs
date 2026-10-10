using UnityEngine;

public enum VehicleAerodynamicSurfaceType
{
    Wing,
    Tail
}

[DisallowMultipleComponent]
public sealed class VehicleAerodynamicSurface : MonoBehaviour
{
    private const float MinimumSpeedSquared = 2.25f;

    [Header("Yuzey Turu")]
    [SerializeField] private VehicleAerodynamicSurfaceType surfaceType = VehicleAerodynamicSurfaceType.Wing;

    [Header("Ortak Ayarlar")]
    [Tooltip("Colliderdan hesaplanan yuzey alanina uygulanan carpan.")]
    [Min(0.05f)]
    [SerializeField] private float areaMultiplier = 1f;

    [Tooltip("Kuvvetin collider merkezine gore yerel konum farki.")]
    [SerializeField] private Vector3 localForcePointOffset;

    [Tooltip("Parcanin kupe bakan baglanti yonu. Kanat prefabinda +X, kuyruk prefabinda -Z. Sari gizmo bu yonu gosterir.")]
    [SerializeField] private Vector3 localAttachmentDirection = Vector3.right;

    [Tooltip("Tek bir yuzeyin araca verebilecegi en yuksek ivme. Yanlis yerlesimde fizik patlamasini sinirlar.")]
    [Min(1f)]
    [SerializeField] private float maxSurfaceAcceleration = 45f;

    [Header("Kanat")]
    [Tooltip("Kanat duz giderken uretilen temel kaldirma.")]
    [SerializeField] private float baseLiftCoefficient = 0.22f;

    [Tooltip("Hucum acisi arttikca kaldirmanin ne kadar hizli arttigi.")]
    [Min(0f)]
    [SerializeField] private float liftSlope = 3.2f;

    [Range(5f, 60f)]
    [SerializeField] private float stallAngle = 25f;

    [Range(0f, 1f)]
    [SerializeField] private float stalledLiftRatio = 0.2f;

    [Min(0f)]
    [SerializeField] private float wingForceScale = 60f;

    [Min(0f)]
    [SerializeField] private float profileDragCoefficient = 0.035f;

    [Min(0f)]
    [SerializeField] private float inducedDragCoefficient = 0.09f;

    [Min(0f)]
    [SerializeField] private float broadsideDragCoefficient = 0.9f;

    [Min(0f)]
    [SerializeField] private float spanwiseDragCoefficient = 0.32f;

    [Tooltip("Kanat veya kuyruk hava akimina ters bakarken olusan ek frenleme.")]
    [Min(0f)]
    [SerializeField] private float reverseDragCoefficient = 0.9f;

    [Tooltip("Yuksekten birakilan kanadin dusus havasini ne kadar ileri suzulmeye cevirecegi.")]
    [Min(0f)]
    [SerializeField] private float descentGlideCoefficient = 0.12f;

    [Header("Kuyruk")]
    [Tooltip("Kuyrugun yukari-asagi hava akisina karsi direnci.")]
    [Min(0f)]
    [SerializeField] private float tailPitchStability = 1f;

    [Tooltip("Kuyrugun yanal hava akisina karsi direnci.")]
    [Min(0f)]
    [SerializeField] private float tailYawStability = 1.15f;

    [Min(0f)]
    [SerializeField] private float tailForceScale = 18f;

    [Min(0f)]
    [SerializeField] private float tailDragCoefficient = 0.08f;

    [Header("Pilot Kontrolu")]
    [Tooltip("Bu hizin altinda kanat ve kuyruk kontrol torku uretilmez.")]
    [Min(0f)]
    [SerializeField] private float minimumControlSpeed = 2f;

    [Tooltip("Bu hizda kontrol yuzeyi tam guce ulasir.")]
    [Min(0.1f)]
    [SerializeField] private float fullControlSpeed = 9f;

    [Tooltip("W/S ile kanadin burun yukari-asagi cevirme gucu.")]
    [Min(0f)]
    [SerializeField] private float wingPitchControlAcceleration = 10f;

    [Tooltip("Tam pitch komutunda hedeflenen en yuksek burun donus hizi.")]
    [Range(10f, 120f)]
    [SerializeField] private float maximumPitchRateDegrees = 55f;

    [Tooltip("Pitch donus hizinin hedefe ne kadar yumusak yaklasacagi.")]
    [Min(0.1f)]
    [SerializeField] private float pitchRateResponse = 4.25f;

    [Tooltip("Cok yuksek hizda kalacak en dusuk pitch hassasiyeti.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float highSpeedPitchAuthority = 0.58f;

    [Tooltip("Stall bolgesinde kalacak en dusuk pitch hassasiyeti.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float stalledPitchAuthority = 0.3f;

    [Tooltip("A/D ile kanadin araci yatirma gucu.")]
    [Min(0f)]
    [SerializeField] private float wingRollControlAcceleration = 16f;

    [Tooltip("A/D ile kuyrugun araci dogrudan saga-sola cevirme gucu.")]
    [Min(0f)]
    [SerializeField] private float tailYawControlAcceleration = 7f;

    private Rigidbody controlledBody;
    private ModularVehicleController vehicleController;
    private BoxCollider surfaceCollider;

    public VehicleAerodynamicSurfaceType SurfaceType => surfaceType;

    private void Awake()
    {
        surfaceCollider = GetComponent<BoxCollider>();
        ResolveControlledBody();
    }

    private void OnEnable()
    {
        ResolveControlledBody();
    }

    private void FixedUpdate()
    {
        ResolveControlledBody();
        if (controlledBody == null || controlledBody.isKinematic || PlayerController.BuildModeActive)
        {
            return;
        }

        Vector3 forcePoint = GetForcePoint();
        Vector3 pointVelocity = controlledBody.GetPointVelocity(forcePoint);
        if (pointVelocity.sqrMagnitude < MinimumSpeedSquared)
        {
            return;
        }

        Vector3 aerodynamicForce = surfaceType == VehicleAerodynamicSurfaceType.Wing
            ? CalculateWingForce(pointVelocity)
            : CalculateTailForce(pointVelocity);

        float maxForce = controlledBody.mass * maxSurfaceAcceleration;
        aerodynamicForce = Vector3.ClampMagnitude(aerodynamicForce, maxForce);
        controlledBody.AddForceAtPosition(aerodynamicForce, forcePoint, ForceMode.Force);
        ApplyPilotControl(pointVelocity, forcePoint);
    }

    private Vector3 CalculateWingForce(Vector3 pointVelocity)
    {
        Vector3 chord = GetResolvedChordDirection();
        Vector3 normal = transform.up.normalized;
        Vector3 span = transform.right.normalized;

        float signedChordSpeed = Vector3.Dot(pointVelocity, chord);
        float forwardChordSpeed = Mathf.Max(0f, signedChordSpeed);
        float normalSpeed = Vector3.Dot(pointVelocity, normal);
        float spanSpeed = Vector3.Dot(pointVelocity, span);
        float angleOfAttack = Mathf.Atan2(-normalSpeed, Mathf.Max(0.1f, forwardChordSpeed));
        float liftCoefficient = ApplyStall(baseLiftCoefficient + liftSlope * angleOfAttack, angleOfAttack);

        float area = GetHorizontalArea();
        Vector3 liftDirection = Vector3.ProjectOnPlane(normal, pointVelocity);
        Vector3 lift = Vector3.zero;

        if (forwardChordSpeed > 0.1f && liftDirection.sqrMagnitude > 0.0001f)
        {
            liftDirection.Normalize();
            if (Vector3.Dot(liftDirection, normal) < 0f)
            {
                liftDirection = -liftDirection;
            }

            float liftPressure = CalculateDynamicPressure(forwardChordSpeed);
            lift = liftDirection * (liftPressure * area * liftCoefficient * wingForceScale);
        }

        float chordDragCoefficient = profileDragCoefficient +
                                     inducedDragCoefficient * liftCoefficient * liftCoefficient;
        if (signedChordSpeed < 0f)
        {
            chordDragCoefficient += reverseDragCoefficient;
        }

        Vector3 chordDrag = CalculateAxisDrag(chord, signedChordSpeed, area, chordDragCoefficient, wingForceScale);
        Vector3 normalDrag = CalculateAxisDrag(normal, normalSpeed, area, broadsideDragCoefficient, wingForceScale);
        Vector3 spanDrag = CalculateAxisDrag(span, spanSpeed, area, spanwiseDragCoefficient, wingForceScale);
        float descentSpeed = Mathf.Max(0f, -normalSpeed);
        Vector3 descentGlide = chord *
                               (CalculateDynamicPressure(descentSpeed) * area * descentGlideCoefficient * wingForceScale);

        return lift + chordDrag + normalDrag + spanDrag + descentGlide;
    }

    private Vector3 CalculateTailForce(Vector3 pointVelocity)
    {
        Vector3 chord = GetResolvedChordDirection();
        Vector3 normal = transform.up.normalized;
        Vector3 side = transform.right.normalized;
        float chordSpeed = Vector3.Dot(pointVelocity, chord);
        float normalSpeed = Vector3.Dot(pointVelocity, normal);
        float sideSpeed = Vector3.Dot(pointVelocity, side);
        float chordDragCoefficient = tailDragCoefficient + (chordSpeed < 0f ? reverseDragCoefficient : 0f);

        Vector3 pitchForce = CalculateAxisDrag(
            normal,
            normalSpeed,
            GetHorizontalArea(),
            tailPitchStability,
            tailForceScale);
        Vector3 yawForce = CalculateAxisDrag(
            side,
            sideSpeed,
            GetVerticalArea(),
            tailYawStability,
            tailForceScale);
        Vector3 chordDrag = CalculateAxisDrag(
            chord,
            chordSpeed,
            GetReferenceArea(),
            chordDragCoefficient,
            tailForceScale);

        return pitchForce + yawForce + chordDrag;
    }

    private static float CalculateDynamicPressure(float speed)
    {
        return 0.5f * 1.225f * speed * speed;
    }

    private static Vector3 CalculateAxisDrag(
        Vector3 axis,
        float speedAlongAxis,
        float area,
        float dragCoefficient,
        float forceScale)
    {
        if (Mathf.Abs(speedAlongAxis) < 0.01f || dragCoefficient <= 0f)
        {
            return Vector3.zero;
        }

        float dragMagnitude = CalculateDynamicPressure(Mathf.Abs(speedAlongAxis)) * area * dragCoefficient * forceScale;
        return -axis * Mathf.Sign(speedAlongAxis) * dragMagnitude;
    }

    private float ApplyStall(float liftCoefficient, float angleOfAttack)
    {
        float absoluteAngle = Mathf.Abs(angleOfAttack * Mathf.Rad2Deg);
        if (absoluteAngle <= stallAngle)
        {
            return liftCoefficient;
        }

        float stallProgress = Mathf.InverseLerp(stallAngle, 90f, Mathf.Min(90f, absoluteAngle));
        float postStallLift = Mathf.Sign(liftCoefficient) * Mathf.Abs(liftCoefficient) *
                              stalledLiftRatio * Mathf.Cos(stallProgress * Mathf.PI * 0.5f);
        return Mathf.Lerp(liftCoefficient, postStallLift, Mathf.SmoothStep(0f, 1f, stallProgress));
    }

    private void ResolveControlledBody()
    {
        Rigidbody parentBody = GetComponentInParent<Rigidbody>();
        if (parentBody != null)
        {
            controlledBody = parentBody;
        }

        if (vehicleController == null)
        {
            vehicleController = GetComponentInParent<ModularVehicleController>(true);
        }
    }

    private void ApplyPilotControl(Vector3 pointVelocity, Vector3 forcePoint)
    {
        if (vehicleController == null || controlledBody == null)
        {
            return;
        }

        float forwardAirSpeed = Mathf.Max(0f, Vector3.Dot(pointVelocity, GetResolvedChordDirection()));
        float speedAuthority = Mathf.InverseLerp(
            minimumControlSpeed,
            Mathf.Max(minimumControlSpeed + 0.1f, fullControlSpeed),
            forwardAirSpeed);
        if (speedAuthority <= 0f)
        {
            return;
        }

        if (surfaceType == VehicleAerodynamicSurfaceType.Wing && vehicleController.IsWingControlEnabled)
        {
            float areaAuthority = Mathf.Clamp(GetHorizontalArea() / 3f, 0.25f, 2f);
            float pitchInput = vehicleController.CurrentFlightPitchInput;
            float rollInput = vehicleController.CurrentWingRollInput;
            float mountingResponse = GetMountingResponse();
            Vector3 bodyRight = controlledBody.transform.right;
            Vector3 bodyForward = controlledBody.transform.forward;
            Vector3 pitchAxis = -bodyRight * mountingResponse;
            float pitchAcceleration = CalculateOrganicPitchAcceleration(
                pointVelocity,
                pitchAxis,
                pitchInput,
                speedAuthority);
            float wingCount = Mathf.Max(1, vehicleController.WingSurfaceCount);
            Vector3 controlAcceleration =
                pitchAxis * (pitchAcceleration / wingCount) +
                -bodyForward * (rollInput * wingRollControlAcceleration * mountingResponse);

            controlledBody.AddTorque(
                controlAcceleration * areaAuthority,
                ForceMode.Acceleration);
            return;
        }

        if (surfaceType == VehicleAerodynamicSurfaceType.Tail && vehicleController.IsTailControlEnabled)
        {
            float areaAuthority = Mathf.Clamp(GetReferenceArea(), 0.25f, 2f);
            float yawInput = vehicleController.CurrentTailYawInput;
            float mountingResponse = GetMountingResponse();

            // Kuyruk merkez kutle hizasinda yanal kuvvet uygular. Arkadaysa normal yaw,
            // onde veya ters takiliysa geometrinin gerektirdigi ters/garip tepki olusur.
            Vector3 bodyUp = controlledBody.transform.up;
            Vector3 centerOffset = forcePoint - controlledBody.worldCenterOfMass;
            Vector3 yawForcePoint = forcePoint - bodyUp * Vector3.Dot(centerOffset, bodyUp);
            Vector3 yawAcceleration = -controlledBody.transform.right *
                                      (yawInput * tailYawControlAcceleration * speedAuthority *
                                       areaAuthority * mountingResponse);
            controlledBody.AddForceAtPosition(yawAcceleration, yawForcePoint, ForceMode.Acceleration);
        }
    }

    private float CalculateOrganicPitchAcceleration(
        Vector3 pointVelocity,
        Vector3 pitchAxis,
        float pitchInput,
        float speedAuthority)
    {
        Vector3 chord = GetResolvedChordDirection();
        Vector3 normal = transform.up.normalized;
        float forwardAirSpeed = Mathf.Max(0f, Vector3.Dot(pointVelocity, chord));
        float normalAirSpeed = Vector3.Dot(pointVelocity, normal);
        float angleOfAttack = Mathf.Abs(Mathf.Atan2(-normalAirSpeed, Mathf.Max(0.1f, forwardAirSpeed)) * Mathf.Rad2Deg);

        float highSpeedFade = Mathf.InverseLerp(
            fullControlSpeed * 1.5f,
            fullControlSpeed * 4f,
            forwardAirSpeed);
        float speedRateFactor = Mathf.Lerp(1f, highSpeedPitchAuthority, highSpeedFade);

        float stallFade = Mathf.InverseLerp(stallAngle * 0.75f, 90f, Mathf.Min(90f, angleOfAttack));
        float stallRateFactor = Mathf.Lerp(1f, stalledPitchAuthority, Mathf.SmoothStep(0f, 1f, stallFade));

        float targetPitchRate = pitchInput *
                                maximumPitchRateDegrees *
                                Mathf.Deg2Rad *
                                speedAuthority *
                                speedRateFactor *
                                stallRateFactor;
        float currentPitchRate = Vector3.Dot(controlledBody.angularVelocity, pitchAxis.normalized);
        float rateError = targetPitchRate - currentPitchRate;
        return Mathf.Clamp(
            rateError * pitchRateResponse,
            -wingPitchControlAcceleration,
            wingPitchControlAcceleration);
    }

    private float GetMountingResponse()
    {
        Vector3 attachmentDirection = localAttachmentDirection.sqrMagnitude > 0.0001f
            ? transform.TransformDirection(localAttachmentDirection.normalized)
            : transform.right;
        Vector3 extensionDirection = -attachmentDirection;
        Vector3 fromCenterToMount = transform.position - controlledBody.worldCenterOfMass;

        float attachmentAlignment = fromCenterToMount.sqrMagnitude > 0.0001f
            ? Vector3.Dot(extensionDirection, fromCenterToMount.normalized)
            : 1f;
        float attachmentPolarity = Mathf.Abs(attachmentAlignment) < 0.1f
            ? 1f
            : Mathf.Sign(attachmentAlignment);

        float upAlignment = Vector3.Dot(transform.up, controlledBody.transform.up);
        if (Mathf.Abs(upAlignment) < 0.1f)
        {
            return 0f;
        }

        return attachmentPolarity * upAlignment;
    }

    private Vector3 GetResolvedChordDirection()
    {
        Vector3 chord = transform.forward.normalized;
        if (vehicleController == null)
        {
            return chord;
        }

        Vector3 referenceForward = vehicleController.GetFlightReferenceForward();
        if (referenceForward.sqrMagnitude < 0.0001f)
        {
            return chord;
        }

        referenceForward.Normalize();
        return Vector3.Dot(chord, referenceForward) < 0f ? -chord : chord;
    }

    private Vector3 GetForcePoint()
    {
        if (surfaceCollider == null)
        {
            surfaceCollider = GetComponent<BoxCollider>();
        }

        Vector3 localCenter = surfaceCollider != null ? surfaceCollider.center : Vector3.zero;
        return transform.TransformPoint(localCenter + localForcePointOffset);
    }

    private float GetHorizontalArea()
    {
        Vector3 size = GetWorldColliderSize();
        return Mathf.Max(0.05f, size.x * size.z * areaMultiplier);
    }

    private float GetVerticalArea()
    {
        Vector3 size = GetWorldColliderSize();
        return Mathf.Max(0.05f, size.y * size.z * areaMultiplier);
    }

    private float GetReferenceArea()
    {
        return Mathf.Max(GetHorizontalArea(), GetVerticalArea());
    }

    private Vector3 GetWorldColliderSize()
    {
        if (surfaceCollider == null)
        {
            surfaceCollider = GetComponent<BoxCollider>();
        }

        Vector3 localSize = surfaceCollider != null ? surfaceCollider.size : Vector3.one;
        Vector3 scale = transform.lossyScale;
        return new Vector3(
            Mathf.Abs(localSize.x * scale.x),
            Mathf.Abs(localSize.y * scale.y),
            Mathf.Abs(localSize.z * scale.z));
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 point = GetForcePoint();
        float length = Mathf.Max(0.35f, Mathf.Sqrt(GetReferenceArea()) * 0.35f);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(point, point + transform.up * length);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(point, point + transform.forward * length);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(point, point + transform.right * length);
        Gizmos.DrawWireSphere(point, 0.06f);

        Vector3 attachmentDirection = localAttachmentDirection.sqrMagnitude > 0.0001f
            ? transform.TransformDirection(localAttachmentDirection.normalized)
            : transform.right;
        Vector3 attachmentMarker = transform.position + attachmentDirection * length * 0.7f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, attachmentMarker);
        Gizmos.DrawWireCube(attachmentMarker, Vector3.one * length * 0.14f);

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.Label(attachmentMarker, "KUP BAGLANTI");
#endif
    }
}
