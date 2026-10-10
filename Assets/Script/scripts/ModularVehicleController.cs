using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(-500)]
public class ModularVehicleController : MonoBehaviour
{
    private const string AutoChassisFrontColliderName = "_AutoChassisFrontCollider";
    private const string AutoChassisRearColliderName = "_AutoChassisRearCollider";
    private const string AutoChassisMeshColliderName = "_AutoChassisMesh";

    public enum DriveMode
    {
        ForwardOnly,
        ReverseOnly
    }

    private struct AxleSpan
    {
        public bool hasValue;
        public Bounds bounds;
        public float averageZ;
        public int count;
    }

    [System.Serializable]
    private struct WheelModuleBinding
    {
        [Tooltip("Kaydedilen tekerin sahnedeki adi.")]
        public string wheelName;
        [Tooltip("Teker modulu olarak bulunan ana obje veya root transform.")]
        public Transform wheelRoot;
        [Tooltip("Bu modula ait fiziksel WheelCollider referansi.")]
        public WheelCollider wheelCollider;
        [Tooltip("Bu modula ait gorunen teker mesh'i veya modeli.")]
        public Transform wheelMesh;
    }

    [Tooltip("Aciksa simulasyon root'una otomatik bir ana govde collider'i ekler veya gunceller.")]
    [SerializeField] private bool autoCreateChassisCollider = false;
    [Tooltip("Mumkunse govde icin BoxCollider yerine child objede convex MeshCollider olusturur. Unity fizik kurali geregi dynamic Rigidbody'de convex kullanilir.")]
    [SerializeField] private bool preferGeneratedMeshCollider = false;
    [Tooltip("Otomatik uretilen govde collision mesh'ini sahnede yari saydam olarak gosterir. Sadece gorsel kontrol icindir, fizigi degistirmez.")]
    [SerializeField] private bool showGeneratedChassisMesh = false;
    [Tooltip("Otomatik uretilen yari saydam govde mesh'inin rengi ve saydamligi.")]
    [SerializeField] private Color generatedChassisMeshColor = new Color(1f, 1f, 1f, 0.08f);
    [Tooltip("Yerde giden araclarda alt parcalarin colliderlarini kapatir; ana govde collider'i kullansin diye.")]
    [SerializeField] private bool disableChildCollidersForGroundVehicle = false;
    [Tooltip("Otomatik olusan ana govde collider'ina eklenecek ekstra bosluk payi.")]
    [SerializeField] private Vector3 chassisPadding = new Vector3(0.15f, 0.05f, 0.15f);
    [Tooltip("Teker acikligina gore govde collider'i ne kadar yana/genise yayilsin.")]
    [SerializeField] private float chassisWheelWidthMultiplier = 1.15f;
    [Tooltip("Govde collider'i tekerin en altindan ne kadar yukarida baslasin.")]
    [SerializeField] private float minimumChassisGroundClearance = 0.24f;
    [Tooltip("Otomatik olusan govde collider'inin hedef yuksekligi.")]
    [SerializeField] private float targetChassisColliderHeight = 0.82f;
    [Tooltip("Govde collider'i ne olursa olsun bu yukseklikten daha ince olmasin.")]
    [SerializeField] private float minimumChassisColliderHeight = 0.78f;
    [Tooltip("On ve arka govde collider'lari birbirine hafif girsin ki arada bosluk kalmasin.")]
    [SerializeField] private float chassisSegmentOverlap = 0.08f;
    [Tooltip("Pervane modullerinde birakilan kucuk ek collider'in boyutu.")]
    [SerializeField] private Vector3 propellerModuleColliderSize = new Vector3(0.55f, 0.55f, 0.55f);
    [Tooltip("Pervane sistemi baslangicta acik mi kapali mi olsun. 3 tusu ve HUD ile degisir.")]
    [SerializeField] private bool propellerSystemEnabled = false;
    [Tooltip("Kanat kontrolu baslangicta acik mi kapali mi olsun. 4 tusu ve HUD ile degisir.")]
    [SerializeField] private bool wingControlEnabled = false;
    [Tooltip("Kuyruk kontrolu baslangicta acik mi kapali mi olsun. 5 tusu ve HUD ile degisir.")]
    [SerializeField] private bool tailControlEnabled = false;
    [Header("Ucus Test Kontrolleri")]
    [Tooltip("Aciksa pervane/teker tuslarindan bagimsiz Numpad ucus kontrollerini kullanir. Online oyuncu atamalari geldiginde bu test katmani kapatilabilir.")]
    [SerializeField] private bool useDedicatedFlightTestKeys = true;
    [SerializeField] private KeyCode wingPitchUpKey = KeyCode.Keypad8;
    [SerializeField] private KeyCode wingPitchDownKey = KeyCode.Keypad2;
    [SerializeField] private KeyCode wingRollLeftKey = KeyCode.Keypad4;
    [SerializeField] private KeyCode wingRollRightKey = KeyCode.Keypad6;
    [SerializeField] private KeyCode tailYawLeftKey = KeyCode.Keypad7;
    [SerializeField] private KeyCode tailYawRightKey = KeyCode.Keypad9;
    [Tooltip("Pitch kumandasinin sifirdan tam degere kac birim/saniye ile ulasacagi.")]
    [Min(0.1f)]
    [SerializeField] private float flightPitchInputRiseSpeed = 2.8f;
    [Tooltip("Pitch tusu birakilinca kumandanin merkeze donus hizi.")]
    [Min(0.1f)]
    [SerializeField] private float flightPitchInputReturnSpeed = 3.5f;
    [Tooltip("Surus modu. ForwardOnly ise sadece ileri, ReverseOnly ise sadece geri gaz calisir.")]
    [SerializeField] private DriveMode driveMode = DriveMode.ForwardOnly;
    [Tooltip("Tum teker torkunun ana carpani. Aracin genel hizlanma gucunu en cok bu etkiler.")]
    [SerializeField] private float globalDriveTorqueMultiplier = 4.5f;
    [Tooltip("Motor carpani x1 iken, yere basan cekis tekerlerinin sagladigi ek ivme.")]
    [Min(0f)]
    [SerializeField] private float groundDriveAccelerationPerPower = 2f;
    [Tooltip("Cok sayida motor takildiginda zemin ivmesinin cikabilecegi ust sinir.")]
    [Min(0f)]
    [SerializeField] private float maximumGroundDriveAcceleration = 18f;
    [Tooltip("Ek zemin ivmesinin on teker direksiyon acisini ne kadar takip edecegi.")]
    [Range(0f, 1f)]
    [SerializeField] private float groundDriveSteeringInfluence = 0.7f;
    [Tooltip("Gaz altinda on teker temasi kesilirse onu tekrar yere indiren ivme.")]
    [Min(0f)]
    [SerializeField] private float steeringWheelContactRecoveryAcceleration = 3.5f;
    [Tooltip("On tekerlerin ne kadar motor torku alacagini belirler. 0 yaparsan onler cekmez.")]
    [SerializeField] private float frontDriveMultiplier = 1f;
    [Tooltip("Arka tekerlerin ne kadar motor torku alacagini belirler. Arka ceker hissi icin bunu yuksek tutarsin.")]
    [SerializeField] private float rearDriveMultiplier = 1f;
    [Tooltip("Hiz arttikca araci yere basan ekstra kuvvet. Yuksekse ziplama azalir ama fazla olursa hantallastirir.")]
    [SerializeField] private float downforcePerSpeed = 0f;
    [Tooltip("Kutle merkezini Y ekseninde asagi/yukari kaydirir. Daha negatif yaparsan arac daha zor devrilir.")]
    [SerializeField] private float centerOfMassYOffset = -0.45f;
    [Tooltip("Bu hizdan sonra direksiyon hassasiyetini azaltmaya baslar.")]
    [SerializeField] private float steeringFadeStartSpeed = 14f;
    [Tooltip("Bu hizda direksiyon azaltma etkisi maksimuma ulasir.")]
    [SerializeField] private float steeringFadeEndSpeed = 32f;
    [Tooltip("Yuksek hizda kalan direksiyon carpani. Dusukse arac hizliyken daha az kirar.")]
    [SerializeField] private float highSpeedSteeringMultiplier = 0.7f;
    [Header("Global Wheel Grip")]
    [Tooltip("Forward friction egirisinde tam tutusun geldigini slip noktasi. Kuculdukce daha cabuk kayma hissi gelir.")]
    [SerializeField] private float forwardExtremumSlip = 0.4f;
    [Tooltip("Forward friction'da extremum noktasindaki tutus kuvveti.")]
    [SerializeField] private float forwardExtremumValue = 1f;
    [Tooltip("Forward friction egirisinde tutusun dusmeye basladigi slip noktasi.")]
    [SerializeField] private float forwardAsymptoteSlip = 0.8f;
    [Tooltip("Forward friction'da kaymaya basladiktan sonraki tutus seviyesi.")]
    [SerializeField] private float forwardAsymptoteValue = 0.5f;
    [Tooltip("On tekerlerin ileri-geri tutus sertligi. Arttikca patinaj zorlasir.")]
    [SerializeField] private float frontForwardStiffness = 1.5f;
    [Tooltip("Arka tekerlerin ileri-geri tutus sertligi. Arttikca patinaj zorlasir.")]
    [SerializeField] private float rearForwardStiffness = 1.5f;
    [Tooltip("Sideways friction egirisinde tam tutusun geldigi slip noktasi.")]
    [SerializeField] private float sidewaysExtremumSlip = 0.3f;
    [Tooltip("Sideways friction'da extremum noktasindaki tutus kuvveti.")]
    [SerializeField] private float sidewaysExtremumValue = 1f;
    [Tooltip("Sideways friction egirisinde tutusun dusmeye basladigi slip noktasi.")]
    [SerializeField] private float sidewaysAsymptoteSlip = 0.8f;
    [Tooltip("Sideways friction'da kaymaya basladiktan sonraki tutus seviyesi.")]
    [SerializeField] private float sidewaysAsymptoteValue = 0.82f;
    [Tooltip("On tekerlerin saga-sola tutus sertligi. Dusurursen on taraf daha kolay kayar.")]
    [SerializeField] private float frontSidewaysStiffness = 2.5f;
    [Tooltip("Arka tekerlerin saga-sola tutus sertligi. Dusurursen arka taraf daha kolay kayar.")]
    [SerializeField] private float rearSidewaysStiffness = 2.15f;
    [Tooltip("Wheel damping'in inebilecegi minimum deger.")]
    [SerializeField] private float minimumWheelDampingRate = 0.25f;
    [Tooltip("Kuvvetin teker merkezine gore uygulandigi nokta. Fazla oynarsan garip devrilmeler olabilir.")]
    [SerializeField] private float forceAppPointDistance = 0.25f;
    [Tooltip("Suspension hesaplari icin referans kutle. Aracin kutlesi buna gore oranlanir.")]
    [SerializeField] private float suspensionReferenceMass = 100f;
    [Tooltip("Referans kutlede kullanilacak suspension yay kuvveti.")]
    [SerializeField] private float suspensionSpringAtReferenceMass = 2000f;
    [Tooltip("Referans kutlede kullanilacak suspension damper kuvveti.")]
    [SerializeField] private float suspensionDamperAtReferenceMass = 450f;
    [Tooltip("Spring kuvvetine eklenecek genel carpan.")]
    [SerializeField] private float suspensionSpringMultiplier = 1f;
    [Tooltip("Damper kuvvetine eklenecek genel carpan.")]
    [SerializeField] private float suspensionDamperMultiplier = 1f;
    [Tooltip("Arac agirligini tasirken kullanilacak suspension guvenlik payi.")]
    [Min(1f)]
    [SerializeField] private float suspensionLoadSafetyMultiplier = 1.25f;
    [Tooltip("Yayin kritik sonumune gore damper orani. 1 tam kritik sonumdur.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float suspensionDampingRatio = 0.65f;
    [Header("Ground Stability")]
    [Tooltip("Keskin kaldirim temasinda fizik motorunun uygulayabilecegi en yuksek ayrisma hizi.")]
    [Min(0.5f)]
    [SerializeField] private float maximumGroundDepenetrationVelocity = 4f;
    [Tooltip("Duz giderken yanal kaymayi sonumlemeye basladigi hiz.")]
    [Min(0f)]
    [SerializeField] private float straightLineStabilityStartSpeed = 7f;
    [Tooltip("Duz giderken yana dogru hizi azaltan ivme carpani.")]
    [Min(0f)]
    [SerializeField] private float straightLineLateralDamping = 2.2f;
    [Tooltip("Direksiyon bosken kendi kendine saga-sola donmeyi azaltir.")]
    [Min(0f)]
    [SerializeField] private float straightLineYawDamping = 2.8f;
    [Tooltip("Tekerler yerdeyken govdeyi yol normaline yaklastiran tork.")]
    [Min(0f)]
    [SerializeField] private float groundedUprightStrength = 8f;
    [Tooltip("Yerdeki ani yatma ve takla hizini sonumleyen tork.")]
    [Min(0f)]
    [SerializeField] private float groundedTiltDamping = 3f;
    [Tooltip("Kaldirim darbesinden sonra izin verilen en yuksek dikey ayrilma hizi.")]
    [Min(0.5f)]
    [SerializeField] private float maximumGroundedUpwardSpeed = 4f;
    [Tooltip("Teker temasi yeni kesildiginde izin verilen en yuksek takla hizi.")]
    [Min(0.5f)]
    [SerializeField] private float maximumGroundedTiltSpeed = 2.8f;
    [Tooltip("Kaldirimda temas bir kare kesilse bile darbe sinirlamasinin surdugu sure.")]
    [Min(0f)]
    [SerializeField] private float groundContactGraceDuration = 0.12f;
    [Tooltip("Runtime'da bulunan on teker kayitlari. Genelde otomatik dolar, elle kurcalama.")]
    [SerializeField] private WheelModuleBinding[] registeredFrontWheels = System.Array.Empty<WheelModuleBinding>();
    [Tooltip("Runtime'da bulunan arka teker kayitlari. Genelde otomatik dolar, elle kurcalama.")]
    [SerializeField] private WheelModuleBinding[] registeredBackWheels = System.Array.Empty<WheelModuleBinding>();

    private FrontWheele[] frontWheels = System.Array.Empty<FrontWheele>();
    private BackWheele[] backWheels = System.Array.Empty<BackWheele>();
    private pervane[] helicopterPropellers = System.Array.Empty<pervane>();
    private Plane[] planePropellers = System.Array.Empty<Plane>();
    private VehicleAerodynamicSurface[] aerodynamicSurfaces = System.Array.Empty<VehicleAerodynamicSurface>();
    private VehicleSeat[] vehicleSeats = System.Array.Empty<VehicleSeat>();
    private PowerModule[] powerModules = System.Array.Empty<PowerModule>();
    private Rigidbody rootBody;
    private Vector3 baseCenterOfMass;
    private bool centerOfMassCached;
    private float connectedPowerMultiplier = 1f;
    private Vector3 groundDriveForwardLocal = Vector3.forward;
    private Mesh generatedChassisCollisionMesh;
    private Material generatedChassisPreviewMaterial;

    private float steeringInput;
    private float driveInput;
    private float flightPitchInput;
    private float wingRollInput;
    private float tailYawInput;
    private bool isBraking;
    private float groundContactGraceRemaining;
    private Vector3 lastGroundSupportNormal = Vector3.up;

    private void Awake()
    {
        rootBody = GetComponent<Rigidbody>();
        AssembleForSimulation();
    }

    private void OnEnable()
    {
        if (rootBody == null)
        {
            rootBody = GetComponent<Rigidbody>();
        }

        AssembleForSimulation();
        EnsureDriveLeverHud();
    }

    private void OnDestroy()
    {
        ReleaseGeneratedChassisCollisionMesh();
    }

    private void FixedUpdate()
    {
        if (rootBody == null)
        {
            rootBody = GetComponent<Rigidbody>();
        }

        if (!HasFiniteRigidBodyState(rootBody))
        {
            SanitizeRigidBodyState(rootBody);
            return;
        }

        bool canAcceptInput = CanAcceptDirectInput();
        float rawSteeringInput = canAcceptInput ? GetRawSteeringInput() : 0f;
        float rawThrottleInput = canAcceptInput ? GetRawThrottleInput() : 0f;

        float targetFlightPitchInput;
        if (canAcceptInput && useDedicatedFlightTestKeys)
        {
            targetFlightPitchInput = ReadDigitalAxis(wingPitchDownKey, wingPitchUpKey);
            wingRollInput = ReadDigitalAxis(wingRollLeftKey, wingRollRightKey);
            tailYawInput = ReadDigitalAxis(tailYawLeftKey, tailYawRightKey);
        }
        else
        {
            targetFlightPitchInput = rawThrottleInput;
            wingRollInput = rawSteeringInput;
            tailYawInput = rawSteeringInput;
        }

        float pitchInputChangeSpeed = Mathf.Abs(targetFlightPitchInput) > Mathf.Abs(flightPitchInput)
            ? flightPitchInputRiseSpeed
            : flightPitchInputReturnSpeed;
        flightPitchInput = Mathf.MoveTowards(
            flightPitchInput,
            targetFlightPitchInput,
            pitchInputChangeSpeed * Time.fixedDeltaTime);

        steeringInput = GetSteeringInput(rawSteeringInput);
        driveInput = GetDriveInputForCurrentMode(rawThrottleInput);
        isBraking = canAcceptInput && IsBrakePressed();

        ApplyInputs();
        ApplyGroundDriveAssist();
        ApplyStabilityForces();
    }

    public DriveMode CurrentDriveMode => driveMode;

    public float CurrentPowerMultiplier => connectedPowerMultiplier;

    public int CurrentDisplayPowerLevel => Mathf.Max(10, Mathf.RoundToInt(connectedPowerMultiplier * 10f));

    public float SimulationMass => rootBody != null ? rootBody.mass : 0f;

    public float MotorPowerMultiplier => connectedPowerMultiplier;

    public float CurrentSteeringInput => steeringInput;

    public float CurrentFlightPitchInput => flightPitchInput;

    public float CurrentFlightTurnInput => wingRollInput;

    public float CurrentWingRollInput => wingRollInput;

    public float CurrentTailYawInput => tailYawInput;

    public Vector3 GetFlightReferenceForward()
    {
        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null && seat.IsDriverSeat)
            {
                return seat.GetSeatWorldRotation() * Vector3.forward;
            }
        }

        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null)
            {
                return seat.GetSeatWorldRotation() * Vector3.forward;
            }
        }

        return transform.forward;
    }

    public bool IsPropellerSystemEnabled => propellerSystemEnabled;

    public bool IsWingControlEnabled => wingControlEnabled;

    public bool IsTailControlEnabled => tailControlEnabled;

    public int GroundWheelCount => GetGroundWheelCount();

    public int HelicopterPropellerCount => CountComponents(helicopterPropellers);

    public int PlanePropellerCount => CountComponents(planePropellers);

    public int WingSurfaceCount => CountAerodynamicSurfaces(VehicleAerodynamicSurfaceType.Wing);

    public int TailSurfaceCount => CountAerodynamicSurfaces(VehicleAerodynamicSurfaceType.Tail);

    public int WheelDisplayPowerLevel => GroundWheelCount > 0 ? CurrentDisplayPowerLevel : 0;

    public int HelicopterPropellerDisplayPowerLevel => HelicopterPropellerCount > 0 ? CurrentDisplayPowerLevel : 0;

    public int PlanePropellerDisplayPowerLevel => PlanePropellerCount > 0 ? CurrentDisplayPowerLevel : 0;

    public int SeatCount => CountComponents(vehicleSeats);

    public int OccupiedSeatCount => CountOccupiedSeats();

    public bool HasSeatModules => SeatCount > 0;

    public bool IsDriveControlAvailable => CanAcceptDirectInput();

    public bool SupportsSeatlessControl => SeatCount == 0;

    public int WeakMotorCount => CountPowerModules(PowerModule.PowerTier.Weak);

    public int MediumMotorCount => CountPowerModules(PowerModule.PowerTier.Medium);

    public int StrongMotorCount => CountPowerModules(PowerModule.PowerTier.Strong);

    public float HelicopterSingleEffectivePower => GetAveragePropellerBasePower(helicopterPropellers) * Mathf.Max(1f, connectedPowerMultiplier);

    public float HelicopterTotalEffectivePower => GetTotalPropellerBasePower(helicopterPropellers) * Mathf.Max(1f, connectedPowerMultiplier);

    public float PlaneSingleEffectivePower => GetAveragePropellerBasePower(planePropellers) * Mathf.Max(1f, connectedPowerMultiplier);

    public float PlaneTotalEffectivePower => GetTotalPropellerBasePower(planePropellers) * Mathf.Max(1f, connectedPowerMultiplier);

    public float HelicopterSingleLoadRatio => CalculateDisplayLoadRatio(SimulationMass, HelicopterSingleEffectivePower);

    public float HelicopterTotalLoadRatio => CalculateDisplayLoadRatio(SimulationMass, HelicopterTotalEffectivePower);

    public float PlaneSingleLoadRatio => CalculateDisplayLoadRatio(SimulationMass, PlaneSingleEffectivePower);

    public float PlaneTotalLoadRatio => CalculateDisplayLoadRatio(SimulationMass, PlaneTotalEffectivePower);

    public void SetForwardDriveMode()
    {
        driveMode = DriveMode.ForwardOnly;
    }

    public void SetReverseDriveMode()
    {
        driveMode = DriveMode.ReverseOnly;
    }

    public void SetDriveMode(bool leverUp)
    {
        driveMode = leverUp ? DriveMode.ForwardOnly : DriveMode.ReverseOnly;
    }

    public void ToggleDriveMode()
    {
        driveMode = driveMode == DriveMode.ForwardOnly
            ? DriveMode.ReverseOnly
            : DriveMode.ForwardOnly;
    }

    public void SetPropellerSystemEnabled(bool enabled)
    {
        propellerSystemEnabled = enabled;
    }

    public void TogglePropellerSystem()
    {
        propellerSystemEnabled = !propellerSystemEnabled;
    }

    public void SetWingControlEnabled(bool enabled)
    {
        wingControlEnabled = enabled;
    }

    public void ToggleWingControl()
    {
        wingControlEnabled = !wingControlEnabled;
    }

    public void SetTailControlEnabled(bool enabled)
    {
        tailControlEnabled = enabled;
    }

    public void ToggleTailControl()
    {
        tailControlEnabled = !tailControlEnabled;
    }

    [ContextMenu("Refresh Wheel Registry")]
    public void RefreshWheelModules()
    {
        frontWheels = GetComponentsInChildren<FrontWheele>(true);
        backWheels = GetComponentsInChildren<BackWheele>(true);
        helicopterPropellers = GetComponentsInChildren<pervane>(true);
        planePropellers = GetComponentsInChildren<Plane>(true);
        aerodynamicSurfaces = GetComponentsInChildren<VehicleAerodynamicSurface>(true);
        vehicleSeats = GetComponentsInChildren<VehicleSeat>(true);
        powerModules = GetComponentsInChildren<PowerModule>(true);
        registeredFrontWheels = BuildFrontWheelRegistry();
        registeredBackWheels = BuildBackWheelRegistry();
        ResolveGroundWheelDirections();
    }

    public void AssembleForSimulation()
    {
        RefreshWheelModules();
        AutoSetupWheelModules();
        RefreshConnectedPowerMultiplier();
        ApplyGlobalWheelGrip();
        ApplyCenterOfMassOffset();

        // The vehicle uses its child part colliders as one compound body.  Do not
        // let a stale generated chassis collider overlap that physical shape.
        SetAutoChassisSegmentCollidersEnabled(false);
        SetAutoChassisMeshColliderEnabled(false);

        if (GetGroundWheelCount() >= 2)
        {
            rootBody.maxDepenetrationVelocity = Mathf.Min(
                rootBody.maxDepenetrationVelocity,
                maximumGroundDepenetrationVelocity);
            NormalizePropellerModuleColliders();

            if (disableChildCollidersForGroundVehicle)
            {
                DisableChildColliders();
            }

            if (autoCreateChassisCollider)
            {
                CreateOrUpdateChassisCollider();
            }
        }
    }

    private void ApplyInputs()
    {
        foreach (FrontWheele frontWheel in frontWheels)
        {
            if (frontWheel != null && frontWheel.enabled)
            {
                ApplyFrontWheel(frontWheel);
            }
        }

        foreach (BackWheele backWheel in backWheels)
        {
            if (backWheel != null && backWheel.enabled)
            {
                ApplyBackWheel(backWheel);
            }
        }
    }

    private float GetDriveInputForCurrentMode(float rawInput)
    {
        if (driveMode == DriveMode.ForwardOnly)
        {
            return Mathf.Max(rawInput, 0f);
        }

        return Mathf.Min(rawInput, 0f);
    }

    private void AutoSetupWheelModules()
    {
        foreach (FrontWheele frontWheel in frontWheels)
        {
            if (frontWheel != null)
            {
                frontWheel.SetSteeringEnabled(true);
                frontWheel.AutoSetupForSimulation();
            }
        }

        foreach (BackWheele backWheel in backWheels)
        {
            if (backWheel != null)
            {
                backWheel.SetSteeringEnabled(false);
                backWheel.AutoSetupForSimulation();
            }
        }
    }

    private int GetGroundWheelCount()
    {
        return frontWheels.Length + backWheels.Length;
    }

    private int CountComponents<T>(T[] components) where T : Component
    {
        if (components == null)
        {
            return 0;
        }

        int count = 0;
        foreach (T component in components)
        {
            if (component != null)
            {
                count++;
            }
        }

        return count;
    }

    private int CountAerodynamicSurfaces(VehicleAerodynamicSurfaceType surfaceType)
    {
        int count = 0;
        foreach (VehicleAerodynamicSurface surface in aerodynamicSurfaces)
        {
            if (surface != null && surface.SurfaceType == surfaceType)
            {
                count++;
            }
        }

        return count;
    }

    private int CountOccupiedSeats()
    {
        int count = 0;

        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null && seat.IsOccupied)
            {
                count++;
            }
        }

        return count;
    }

    private int CountPowerModules(PowerModule.PowerTier tier)
    {
        int count = 0;

        foreach (PowerModule module in powerModules)
        {
            if (module != null && module.Tier == tier)
            {
                count++;
            }
        }

        return count;
    }

    private float CalculateDisplayLoadRatio(float mass, float effectivePower)
    {
        if (mass <= 0f || effectivePower <= 0f)
        {
            return 0f;
        }

        return mass / Mathf.Max(1f, effectivePower);
    }

    private float GetAveragePropellerBasePower(pervane[] propellers)
    {
        float totalPower = 0f;
        int count = 0;

        foreach (pervane propeller in propellers)
        {
            if (propeller == null)
            {
                continue;
            }

            totalPower += Mathf.Max(0f, propeller.PowerUnits);
            count++;
        }

        return count > 0 ? totalPower / count : 0f;
    }

    private float GetAveragePropellerBasePower(Plane[] propellers)
    {
        float totalPower = 0f;
        int count = 0;

        foreach (Plane propeller in propellers)
        {
            if (propeller == null)
            {
                continue;
            }

            totalPower += Mathf.Max(0f, propeller.PowerUnits);
            count++;
        }

        return count > 0 ? totalPower / count : 0f;
    }

    private float GetTotalPropellerBasePower(pervane[] propellers)
    {
        float totalPower = 0f;

        foreach (pervane propeller in propellers)
        {
            if (propeller != null)
            {
                totalPower += Mathf.Max(0f, propeller.PowerUnits);
            }
        }

        return totalPower;
    }

    private float GetTotalPropellerBasePower(Plane[] propellers)
    {
        float totalPower = 0f;

        foreach (Plane propeller in propellers)
        {
            if (propeller != null)
            {
                totalPower += Mathf.Max(0f, propeller.PowerUnits);
            }
        }

        return totalPower;
    }

    private void EnsureDriveLeverHud()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (GetComponent<VehicleDriveLeverHUD>() == null)
        {
            gameObject.AddComponent<VehicleDriveLeverHUD>();
        }
    }

    private bool CanAcceptDirectInput()
    {
        return GetFirstOccupiedControlSeat() != null || GetSeatlessControlPlayer() != null;
    }

    public bool CanBeControlledWithoutSeatBy(PlayerController player)
    {
        if (player == null || !SupportsSeatlessControl)
        {
            return false;
        }

        return GetSeatlessControlPlayer() == player;
    }

    [ContextMenu("Apply Global Wheel Grip")]
    public void ApplyGlobalWheelGrip()
    {
        int supportingWheelCount = CountActiveRuntimeWheelColliders();

        foreach (FrontWheele frontWheel in frontWheels)
        {
            if (frontWheel == null)
            {
                continue;
            }

            ApplyGripProfile(frontWheel.RuntimeWheelCollider, true, supportingWheelCount);
        }

        foreach (BackWheele backWheel in backWheels)
        {
            if (backWheel == null)
            {
                continue;
            }

            ApplyGripProfile(backWheel.RuntimeWheelCollider, false, supportingWheelCount);
        }
    }

    private void ApplyGroundDriveAssist()
    {
        if (rootBody == null || rootBody.isKinematic || Mathf.Abs(driveInput) < 0.001f)
        {
            return;
        }

        int drivenWheelCount = 0;
        int groundedWheelCount = 0;
        int steeringWheelCount = 0;
        int groundedSteeringWheelCount = 0;
        int groundedRearDriveWheelCount = 0;
        float groundedSteerAngleSum = 0f;
        Vector3 steeringWheelPositionSum = Vector3.zero;
        Vector3 combinedGroundNormal = Vector3.zero;

        foreach (FrontWheele frontWheel in frontWheels)
        {
            if (frontWheel == null || !frontWheel.enabled || !frontWheel.CanDrive)
            {
                continue;
            }

            WheelCollider wheelCollider = frontWheel.RuntimeWheelCollider;
            if (wheelCollider == null || !wheelCollider.enabled)
            {
                continue;
            }

            steeringWheelCount++;
            steeringWheelPositionSum += wheelCollider.transform.position;
            if (AccumulateGroundedDriveWheel(
                wheelCollider,
                ref drivenWheelCount,
                ref groundedWheelCount,
                ref combinedGroundNormal))
            {
                groundedSteeringWheelCount++;
                groundedSteerAngleSum += wheelCollider.steerAngle;
            }
        }

        foreach (BackWheele backWheel in backWheels)
        {
            if (backWheel == null || !backWheel.enabled || !backWheel.CanDrive)
            {
                continue;
            }

            if (AccumulateGroundedDriveWheel(
                backWheel.RuntimeWheelCollider,
                ref drivenWheelCount,
                ref groundedWheelCount,
                ref combinedGroundNormal))
            {
                groundedRearDriveWheelCount++;
            }
        }

        if (drivenWheelCount == 0 || groundedWheelCount == 0)
        {
            return;
        }

        Vector3 supportNormal = combinedGroundNormal.sqrMagnitude > 0.001f
            ? combinedGroundNormal.normalized
            : GetGravityUp();
        Vector3 driveForward = Vector3.ProjectOnPlane(
            transform.TransformDirection(groundDriveForwardLocal),
            supportNormal);

        if (driveForward.sqrMagnitude < 0.001f)
        {
            return;
        }

        if (groundedSteeringWheelCount > 0 && Mathf.Abs(steeringInput) > 0.001f)
        {
            float averageSteerAngle = groundedSteerAngleSum / groundedSteeringWheelCount;
            driveForward = Quaternion.AngleAxis(
                averageSteerAngle * groundDriveSteeringInfluence,
                supportNormal) * driveForward;
        }

        float groundedFraction = groundedWheelCount / (float)drivenWheelCount;
        float poweredAcceleration = groundDriveAccelerationPerPower * Mathf.Max(1f, connectedPowerMultiplier);
        float acceleration = Mathf.Min(maximumGroundDriveAcceleration, poweredAcceleration) * groundedFraction;
        rootBody.AddForce(
            driveForward.normalized * driveInput * acceleration,
            ForceMode.Acceleration);

        ApplySteeringWheelContactRecovery(
            steeringWheelCount,
            groundedSteeringWheelCount,
            groundedRearDriveWheelCount,
            steeringWheelPositionSum);
    }

    private void ApplySteeringWheelContactRecovery(
        int steeringWheelCount,
        int groundedSteeringWheelCount,
        int groundedRearDriveWheelCount,
        Vector3 steeringWheelPositionSum)
    {
        if (steeringWheelCount == 0 ||
            groundedRearDriveWheelCount == 0 ||
            groundedSteeringWheelCount >= steeringWheelCount ||
            steeringWheelContactRecoveryAcceleration <= 0f)
        {
            return;
        }

        float missingContactFraction = 1f - groundedSteeringWheelCount / (float)steeringWheelCount;
        float recoveryAcceleration = steeringWheelContactRecoveryAcceleration *
                                     missingContactFraction *
                                     Mathf.Clamp01(Mathf.Abs(driveInput));
        Vector3 averageSteeringWheelPosition = steeringWheelPositionSum / steeringWheelCount;
        rootBody.AddForceAtPosition(
            -transform.up * rootBody.mass * recoveryAcceleration,
            averageSteeringWheelPosition,
            ForceMode.Force);
    }

    private static bool AccumulateGroundedDriveWheel(
        WheelCollider wheelCollider,
        ref int drivenWheelCount,
        ref int groundedWheelCount,
        ref Vector3 combinedGroundNormal)
    {
        if (wheelCollider == null || !wheelCollider.enabled)
        {
            return false;
        }

        drivenWheelCount++;
        if (wheelCollider.GetGroundHit(out WheelHit hit))
        {
            groundedWheelCount++;
            Vector3 gravityUp = GetGravityUp();
            if (Vector3.Dot(hit.normal, gravityUp) >= 0.25f)
            {
                combinedGroundNormal += hit.normal;
            }

            return true;
        }

        return false;
    }

    private int CountActiveRuntimeWheelColliders()
    {
        int count = 0;

        foreach (FrontWheele frontWheel in frontWheels)
        {
            if (frontWheel != null && frontWheel.RuntimeWheelCollider != null &&
                frontWheel.RuntimeWheelCollider.enabled)
            {
                count++;
            }
        }

        foreach (BackWheele backWheel in backWheels)
        {
            if (backWheel != null && backWheel.RuntimeWheelCollider != null &&
                backWheel.RuntimeWheelCollider.enabled)
            {
                count++;
            }
        }

        return Mathf.Max(1, count);
    }

    private WheelModuleBinding[] BuildFrontWheelRegistry()
    {
        WheelModuleBinding[] registry = new WheelModuleBinding[frontWheels.Length];

        for (int i = 0; i < frontWheels.Length; i++)
        {
            FrontWheele frontWheel = frontWheels[i];
            if (frontWheel == null)
            {
                continue;
            }

            frontWheel.AutoSetupForSimulation();
            registry[i] = CreateBinding(
                frontWheel.name,
                frontWheel.transform,
                frontWheel.RuntimeWheelCollider,
                frontWheel.RuntimeWheelTransform);
        }

        return registry;
    }

    private WheelModuleBinding[] BuildBackWheelRegistry()
    {
        WheelModuleBinding[] registry = new WheelModuleBinding[backWheels.Length];

        for (int i = 0; i < backWheels.Length; i++)
        {
            BackWheele backWheel = backWheels[i];
            if (backWheel == null)
            {
                continue;
            }

            backWheel.AutoSetupForSimulation();
            registry[i] = CreateBinding(
                backWheel.name,
                backWheel.transform,
                backWheel.RuntimeWheelCollider,
                backWheel.RuntimeWheelTransform);
        }

        return registry;
    }

    private void ResolveGroundWheelDirections()
    {
        Vector3 vehicleUp = transform.up;
        Vector3 driveForward = CalculateGroundDriveReferenceForward(vehicleUp);
        if (driveForward.sqrMagnitude < 0.001f)
        {
            return;
        }

        groundDriveForwardLocal = transform.InverseTransformDirection(driveForward.normalized).normalized;

        foreach (FrontWheele frontWheel in frontWheels)
        {
            if (frontWheel != null)
            {
                frontWheel.SetAutomaticDirectionReference(driveForward, vehicleUp);
            }
        }

        foreach (BackWheele backWheel in backWheels)
        {
            if (backWheel != null)
            {
                backWheel.SetAutomaticDirectionReference(driveForward, vehicleUp);
            }
        }
    }

    private Vector3 CalculateGroundDriveReferenceForward(Vector3 vehicleUp)
    {
        // The build grid's forward direction is world +Z. This picks each wheel's
        // torque sign once; after that the wheels still turn with the vehicle.
        Vector3 preferredForward = Vector3.ProjectOnPlane(Vector3.forward, vehicleUp);
        if (preferredForward.sqrMagnitude > 0.001f)
        {
            preferredForward.Normalize();
        }

        Vector3 seedAxis = Vector3.zero;
        float bestPreferredAlignment = -1f;
        SelectGroundDriveAxisSeed(
            registeredFrontWheels,
            vehicleUp,
            preferredForward,
            ref seedAxis,
            ref bestPreferredAlignment);
        SelectGroundDriveAxisSeed(
            registeredBackWheels,
            vehicleUp,
            preferredForward,
            ref seedAxis,
            ref bestPreferredAlignment);

        if (seedAxis.sqrMagnitude < 0.001f)
        {
            return preferredForward;
        }

        if (preferredForward.sqrMagnitude > 0.001f)
        {
            float preferredDot = Vector3.Dot(seedAxis, preferredForward);
            if (Mathf.Abs(preferredDot) >= 0.05f && preferredDot < 0f)
            {
                seedAxis = -seedAxis;
            }
        }

        Vector3 combinedAxis = Vector3.zero;
        AccumulateGroundDriveAxis(registeredFrontWheels, vehicleUp, seedAxis, ref combinedAxis);
        AccumulateGroundDriveAxis(registeredBackWheels, vehicleUp, seedAxis, ref combinedAxis);
        return combinedAxis.sqrMagnitude > 0.001f ? combinedAxis.normalized : seedAxis;
    }

    private static void SelectGroundDriveAxisSeed(
        WheelModuleBinding[] bindings,
        Vector3 vehicleUp,
        Vector3 preferredForward,
        ref Vector3 seedAxis,
        ref float bestPreferredAlignment)
    {
        foreach (WheelModuleBinding binding in bindings)
        {
            if (binding.wheelCollider == null)
            {
                continue;
            }

            Vector3 candidate = Vector3.ProjectOnPlane(binding.wheelCollider.transform.forward, vehicleUp);
            if (candidate.sqrMagnitude < 0.001f)
            {
                continue;
            }

            candidate.Normalize();
            float preferredAlignment = preferredForward.sqrMagnitude > 0.001f
                ? Mathf.Abs(Vector3.Dot(candidate, preferredForward))
                : 0f;
            if (seedAxis.sqrMagnitude < 0.001f || preferredAlignment > bestPreferredAlignment)
            {
                seedAxis = candidate;
                bestPreferredAlignment = preferredAlignment;
            }
        }
    }

    private static void AccumulateGroundDriveAxis(
        WheelModuleBinding[] bindings,
        Vector3 vehicleUp,
        Vector3 seedAxis,
        ref Vector3 combinedAxis)
    {
        foreach (WheelModuleBinding binding in bindings)
        {
            if (binding.wheelCollider == null)
            {
                continue;
            }

            Vector3 candidate = Vector3.ProjectOnPlane(binding.wheelCollider.transform.forward, vehicleUp);
            if (candidate.sqrMagnitude < 0.001f)
            {
                continue;
            }

            candidate.Normalize();
            float alignment = Vector3.Dot(candidate, seedAxis);
            if (Mathf.Abs(alignment) < 0.5f)
            {
                continue;
            }

            combinedAxis += (alignment < 0f ? -candidate : candidate) * Mathf.Abs(alignment);
        }
    }

    private WheelModuleBinding CreateBinding(
        string wheelName,
        Transform wheelRoot,
        WheelCollider wheelCollider,
        Transform wheelMesh)
    {
        return new WheelModuleBinding
        {
            wheelName = wheelName,
            wheelRoot = wheelRoot,
            wheelCollider = wheelCollider,
            wheelMesh = wheelMesh
        };
    }

    private void DisableChildColliders()
    {
        Collider[] childColliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider childCollider in childColliders)
        {
            if (childCollider == null || childCollider.transform == transform)
            {
                continue;
            }

            if (childCollider is WheelCollider)
            {
                continue;
            }

            if (ShouldKeepChildColliderEnabled(childCollider))
            {
                childCollider.enabled = true;
                continue;
            }

            childCollider.enabled = false;
        }
    }

    private void CreateOrUpdateChassisCollider()
    {
        if (preferGeneratedMeshCollider && TryCreateGeneratedChassisMeshCollider())
        {
            DisableLegacyRootChassisCollider();
            SetAutoChassisSegmentCollidersEnabled(false);
            return;
        }

        if (!TryCalculateChassisBounds(out Bounds localBounds))
        {
            return;
        }

        LiftChassisBoundsAboveWheelBottom(ref localBounds);
        FlattenChassisHeight(ref localBounds);
        if (TryCreateSegmentedChassisColliders(localBounds))
        {
            DisableLegacyRootChassisCollider();
            SetAutoChassisMeshColliderEnabled(false);
            return;
        }

        ExpandBoundsForWheelSpan(ref localBounds);
        SetAutoChassisSegmentCollidersEnabled(false);
        SetAutoChassisMeshColliderEnabled(false);

        BoxCollider chassisCollider = GetComponent<BoxCollider>();
        if (chassisCollider == null)
        {
            chassisCollider = gameObject.AddComponent<BoxCollider>();
        }

        chassisCollider.center = localBounds.center;
        chassisCollider.size = GetPositiveColliderSize(localBounds.size + chassisPadding);
        chassisCollider.enabled = true;
    }

    private bool TryCalculateChassisBounds(out Bounds localBounds)
    {
        if (TryCalculateCoreChassisBounds(out localBounds))
        {
            return true;
        }

        return TryCalculateGenericChassisBounds(out localBounds);
    }

    private bool TryCalculateCoreChassisBounds(out Bounds localBounds)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        localBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !ShouldUseCoreChassisBoundsTarget(renderer.transform))
            {
                continue;
            }

            EncapsulateWorldBounds(renderer.bounds, ref localBounds, ref hasBounds);
        }

        if (!hasBounds)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider childCollider in colliders)
            {
                if (childCollider == null || childCollider is WheelCollider || !ShouldUseCoreChassisBoundsTarget(childCollider.transform))
                {
                    continue;
                }

                EncapsulateWorldBounds(childCollider.bounds, ref localBounds, ref hasBounds);
            }
        }

        return hasBounds;
    }

    private bool TryCalculateGenericChassisBounds(out Bounds localBounds)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        localBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || ShouldIgnoreChassisBoundsTarget(renderer.transform))
            {
                continue;
            }

            EncapsulateWorldBounds(renderer.bounds, ref localBounds, ref hasBounds);
        }

        if (!hasBounds)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider childCollider in colliders)
            {
                if (childCollider == null || childCollider is WheelCollider || ShouldIgnoreChassisBoundsTarget(childCollider.transform))
                {
                    continue;
                }

                EncapsulateWorldBounds(childCollider.bounds, ref localBounds, ref hasBounds);
            }
        }

        return hasBounds;
    }

    private void ApplyFrontWheel(FrontWheele frontWheel)
    {
        frontWheel.ApplyDriveTorque(driveInput * frontDriveMultiplier * globalDriveTorqueMultiplier * connectedPowerMultiplier);
        frontWheel.ApplyBrakeTorque(isBraking, driveInput);
        frontWheel.ApplySteering(steeringInput);
        frontWheel.SyncVisual();
    }

    private void ApplyBackWheel(BackWheele backWheel)
    {
        backWheel.ApplyDriveTorque(driveInput * rearDriveMultiplier * globalDriveTorqueMultiplier * connectedPowerMultiplier);
        backWheel.ApplyBrakeTorque(isBraking, driveInput);
        backWheel.ApplySteering(steeringInput);
        backWheel.SyncVisual();
    }

    private void RefreshConnectedPowerMultiplier()
    {
        if (rootBody == null)
        {
            rootBody = GetComponent<Rigidbody>();
        }

        if (rootBody != null)
        {
            connectedPowerMultiplier = BuildVehicleUtility.GetPowerMultiplier(new[] { rootBody });
        }
        else
        {
            connectedPowerMultiplier = 1f;
        }
    }

    private float GetSteeringInput(float rawSteering)
    {
        float speed = rootBody != null ? rootBody.linearVelocity.magnitude : 0f;
        float speedFactor = Mathf.InverseLerp(steeringFadeStartSpeed, steeringFadeEndSpeed, speed);
        float steeringMultiplier = Mathf.Lerp(1f, highSpeedSteeringMultiplier, speedFactor);
        return rawSteering * steeringMultiplier;
    }

    private static float ReadDigitalAxis(KeyCode negativeKey, KeyCode positiveKey)
    {
        float value = 0f;
        if (Input.GetKey(negativeKey))
        {
            value -= 1f;
        }

        if (Input.GetKey(positiveKey))
        {
            value += 1f;
        }

        return value;
    }

    public float GetControlChannelValue(VehicleControlChannel channel)
    {
        float rawSteering = GetRawSteeringInput();
        float rawThrottle = GetRawThrottleInput();

        switch (channel)
        {
            case VehicleControlChannel.ThrottleForward:
                return Mathf.Max(rawThrottle, 0f);
            case VehicleControlChannel.ThrottleReverse:
                return Mathf.Max(-rawThrottle, 0f);
            case VehicleControlChannel.SteeringLeft:
                return Mathf.Max(-rawSteering, 0f);
            case VehicleControlChannel.SteeringRight:
                return Mathf.Max(rawSteering, 0f);
            default:
                return 0f;
        }
    }

    private float GetRawSteeringInput()
    {
        VehicleSeat steeringSeat = GetFirstOccupiedSeatThatCanControlSteering();
        if (steeringSeat?.Occupant != null)
        {
            return steeringSeat.Occupant.GetVehicleSteeringInput();
        }

        PlayerController seatlessControlPlayer = GetSeatlessControlPlayer();
        if (seatlessControlPlayer != null)
        {
            return seatlessControlPlayer.GetVehicleSteeringInput();
        }

        if (!HasExplicitControlSeats())
        {
            VehicleSeat fallbackSeat = GetFirstOccupiedSeat();
            if (fallbackSeat?.Occupant != null)
            {
                return fallbackSeat.Occupant.GetVehicleSteeringInput();
            }
        }

        return 0f;
    }

    private float GetRawThrottleInput()
    {
        VehicleSeat throttleSeat = GetFirstOccupiedSeatThatCanControlThrottle();
        if (throttleSeat?.Occupant != null)
        {
            return throttleSeat.Occupant.GetVehicleThrottleInput();
        }

        PlayerController seatlessControlPlayer = GetSeatlessControlPlayer();
        if (seatlessControlPlayer != null)
        {
            return seatlessControlPlayer.GetVehicleThrottleInput();
        }

        if (!HasExplicitControlSeats())
        {
            VehicleSeat fallbackSeat = GetFirstOccupiedSeat();
            if (fallbackSeat?.Occupant != null)
            {
                return fallbackSeat.Occupant.GetVehicleThrottleInput();
            }
        }

        return 0f;
    }

    private bool IsBrakePressed()
    {
        VehicleSeat throttleSeat = GetFirstOccupiedSeatThatCanControlThrottle();
        if (throttleSeat?.Occupant != null)
        {
            return throttleSeat.Occupant.IsVehicleBrakePressed();
        }

        PlayerController seatlessControlPlayer = GetSeatlessControlPlayer();
        if (seatlessControlPlayer != null)
        {
            return seatlessControlPlayer.IsVehicleBrakePressed();
        }

        if (!HasExplicitControlSeats())
        {
            VehicleSeat fallbackSeat = GetFirstOccupiedSeat();
            return fallbackSeat?.Occupant != null && fallbackSeat.Occupant.IsVehicleBrakePressed();
        }

        return false;
    }

    private VehicleSeat GetFirstOccupiedControlSeat()
    {
        VehicleSeat preferredSeat = GetFirstOccupiedSeatThatCanControlSteering();
        if (preferredSeat != null)
        {
            return preferredSeat;
        }

        preferredSeat = GetFirstOccupiedSeatThatCanControlThrottle();
        if (preferredSeat != null)
        {
            return preferredSeat;
        }

        if (HasExplicitControlSeats())
        {
            return null;
        }

        return GetFirstOccupiedSeat();
    }

    private PlayerController GetSeatlessControlPlayer()
    {
        if (!SupportsSeatlessControl)
        {
            return null;
        }

        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        PlayerController bestPlayer = null;
        float bestDistanceSqr = float.MaxValue;

        foreach (PlayerController player in players)
        {
            if (player == null || !player.CanProvideSeatlessVehicleInput)
            {
                continue;
            }

            if (!IsPreferredSeatlessControllerFor(player))
            {
                continue;
            }

            float distanceSqr = (player.transform.position - transform.position).sqrMagnitude;
            if (bestPlayer == null || distanceSqr < bestDistanceSqr)
            {
                bestPlayer = player;
                bestDistanceSqr = distanceSqr;
            }
        }

        return bestPlayer;
    }

    private bool IsPreferredSeatlessControllerFor(PlayerController player)
    {
        if (player == null)
        {
            return false;
        }

        ModularVehicleController[] vehicleControllers = FindObjectsByType<ModularVehicleController>(FindObjectsSortMode.None);
        ModularVehicleController bestController = null;
        float bestDistanceSqr = float.MaxValue;

        foreach (ModularVehicleController vehicleController in vehicleControllers)
        {
            if (vehicleController == null || !vehicleController.isActiveAndEnabled || !vehicleController.SupportsSeatlessControl)
            {
                continue;
            }

            float distanceSqr = (vehicleController.transform.position - player.transform.position).sqrMagnitude;
            if (bestController == null ||
                distanceSqr < bestDistanceSqr ||
                (Mathf.Approximately(distanceSqr, bestDistanceSqr) && vehicleController.GetEntityId() < bestController.GetEntityId()))
            {
                bestController = vehicleController;
                bestDistanceSqr = distanceSqr;
            }
        }

        return bestController == this;
    }

    private VehicleSeat GetFirstOccupiedSeatThatCanControlSteering()
    {
        if (!HasExplicitControlSeats())
        {
            return GetOccupiedSeatByIndex(0, true);
        }

        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null && seat.IsOccupied && seat.CanControlSteering)
            {
                return seat;
            }
        }

        return null;
    }

    private VehicleSeat GetFirstOccupiedSeatThatCanControlThrottle()
    {
        if (!HasExplicitControlSeats())
        {
            VehicleSeat secondOccupiedSeat = GetOccupiedSeatByIndex(1, true);
            return secondOccupiedSeat ?? GetOccupiedSeatByIndex(0, true);
        }

        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null && seat.IsOccupied && seat.CanControlThrottle)
            {
                return seat;
            }
        }

        return null;
    }

    private VehicleSeat GetFirstOccupiedSeat()
    {
        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null && seat.IsOccupied)
            {
                return seat;
            }
        }

        return null;
    }

    private bool HasExplicitControlSeats()
    {
        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat != null && seat.IsSpecializedControlSeat)
            {
                return true;
            }
        }

        return false;
    }

    private VehicleSeat GetOccupiedSeatByIndex(int index, bool preferDriverSeats)
    {
        if (index < 0)
        {
            return null;
        }

        if (preferDriverSeats)
        {
            VehicleSeat preferredDriverSeat = GetOccupiedDriverSeatByIndex(index);
            if (preferredDriverSeat != null)
            {
                return preferredDriverSeat;
            }
        }

        int currentIndex = 0;
        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat == null || !seat.IsOccupied)
            {
                continue;
            }

            if (currentIndex == index)
            {
                return seat;
            }

            currentIndex++;
        }

        return null;
    }

    private VehicleSeat GetOccupiedDriverSeatByIndex(int index)
    {
        int currentIndex = 0;
        foreach (VehicleSeat seat in vehicleSeats)
        {
            if (seat == null || !seat.IsOccupied || !seat.IsDriverSeat)
            {
                continue;
            }

            if (currentIndex == index)
            {
                return seat;
            }

            currentIndex++;
        }

        return null;
    }

    private void ApplyStabilityForces()
    {
        if (rootBody == null || rootBody.isKinematic ||
            !HasFiniteVector(rootBody.linearVelocity) ||
            !HasFiniteVector(rootBody.angularVelocity) ||
            !HasFiniteVector(transform.up))
        {
            return;
        }

        bool isGrounded = TryGetGroundSupport(
            out int activeWheelCount,
            out int groundedWheelCount,
            out Vector3 supportNormal);

        if (isGrounded)
        {
            groundContactGraceRemaining = groundContactGraceDuration;
            lastGroundSupportNormal = supportNormal;
        }
        else
        {
            groundContactGraceRemaining = Mathf.Max(
                0f,
                groundContactGraceRemaining - Time.fixedDeltaTime);
            if (groundContactGraceRemaining <= 0f)
            {
                return;
            }

            supportNormal = lastGroundSupportNormal;
        }

        float speed = rootBody.linearVelocity.magnitude;
        if (!float.IsFinite(speed))
        {
            return;
        }

        float groundedFraction = isGrounded
            ? groundedWheelCount / (float)Mathf.Max(1, activeWheelCount)
            : Mathf.Clamp01(groundContactGraceRemaining / Mathf.Max(0.001f, groundContactGraceDuration)) * 0.5f;

        ApplyGroundUprightStability(supportNormal, groundedFraction, speed);
        ApplyStraightLineStability(supportNormal, groundedFraction, speed);
        ClampGroundImpactMotion(supportNormal, speed);

        if (isGrounded && downforcePerSpeed > 0f && speed >= 0.1f)
        {
            rootBody.AddForce(-supportNormal * speed * downforcePerSpeed, ForceMode.Force);
        }
    }

    private bool TryGetGroundSupport(
        out int activeWheelCount,
        out int groundedWheelCount,
        out Vector3 supportNormal)
    {
        activeWheelCount = 0;
        groundedWheelCount = 0;
        Vector3 combinedNormal = Vector3.zero;
        Vector3 gravityUp = GetGravityUp();

        AccumulateGroundSupport(
            registeredFrontWheels,
            gravityUp,
            ref activeWheelCount,
            ref groundedWheelCount,
            ref combinedNormal);
        AccumulateGroundSupport(
            registeredBackWheels,
            gravityUp,
            ref activeWheelCount,
            ref groundedWheelCount,
            ref combinedNormal);

        supportNormal = combinedNormal.sqrMagnitude > 0.001f
            ? combinedNormal.normalized
            : gravityUp;
        return groundedWheelCount > 0;
    }

    private static void AccumulateGroundSupport(
        WheelModuleBinding[] bindings,
        Vector3 gravityUp,
        ref int activeWheelCount,
        ref int groundedWheelCount,
        ref Vector3 combinedNormal)
    {
        foreach (WheelModuleBinding binding in bindings)
        {
            WheelCollider wheelCollider = binding.wheelCollider;
            if (wheelCollider == null || !wheelCollider.enabled)
            {
                continue;
            }

            activeWheelCount++;
            if (!wheelCollider.GetGroundHit(out WheelHit hit))
            {
                continue;
            }

            groundedWheelCount++;
            if (Vector3.Dot(hit.normal, gravityUp) >= 0.25f)
            {
                combinedNormal += hit.normal;
            }
        }
    }

    private void ApplyGroundUprightStability(
        Vector3 supportNormal,
        float groundedFraction,
        float speed)
    {
        float upAlignment = Vector3.Dot(transform.up, supportNormal);
        if (upAlignment < 0.5f)
        {
            return;
        }

        float speedFactor = Mathf.InverseLerp(
            straightLineStabilityStartSpeed,
            Mathf.Max(straightLineStabilityStartSpeed + 1f, steeringFadeEndSpeed),
            speed);
        float strengthScale = Mathf.Lerp(0.35f, 1f, speedFactor) * Mathf.Clamp01(groundedFraction);
        Vector3 yawVelocity = supportNormal * Vector3.Dot(rootBody.angularVelocity, supportNormal);
        Vector3 tiltVelocity = rootBody.angularVelocity - yawVelocity;
        Vector3 uprightError = Vector3.Cross(transform.up, supportNormal);
        Vector3 stabilizationTorque = uprightError * groundedUprightStrength -
                                      tiltVelocity * groundedTiltDamping;
        rootBody.AddTorque(
            Vector3.ClampMagnitude(stabilizationTorque * strengthScale, groundedUprightStrength),
            ForceMode.Acceleration);
    }

    private void ApplyStraightLineStability(
        Vector3 supportNormal,
        float groundedFraction,
        float speed)
    {
        if (speed < straightLineStabilityStartSpeed || Mathf.Abs(steeringInput) >= 0.15f)
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(
            transform.TransformDirection(groundDriveForwardLocal),
            supportNormal);
        if (forward.sqrMagnitude < 0.001f)
        {
            return;
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(supportNormal, forward).normalized;
        float speedFactor = Mathf.InverseLerp(
            straightLineStabilityStartSpeed,
            Mathf.Max(straightLineStabilityStartSpeed + 1f, steeringFadeEndSpeed),
            speed);
        float assistScale = speedFactor * Mathf.Clamp01(groundedFraction);
        float lateralSpeed = Vector3.Dot(rootBody.linearVelocity, right);
        float lateralAcceleration = Mathf.Clamp(
            -lateralSpeed * straightLineLateralDamping * assistScale,
            -8f,
            8f);
        rootBody.AddForce(right * lateralAcceleration, ForceMode.Acceleration);

        float yawSpeed = Vector3.Dot(rootBody.angularVelocity, supportNormal);
        rootBody.AddTorque(
            -supportNormal * yawSpeed * straightLineYawDamping * assistScale,
            ForceMode.Acceleration);
    }

    private void ClampGroundImpactMotion(Vector3 supportNormal, float speed)
    {
        if (Vector3.Dot(transform.up, supportNormal) < 0.5f)
        {
            return;
        }

        float upwardSpeed = Vector3.Dot(rootBody.linearVelocity, supportNormal);
        if (upwardSpeed > maximumGroundedUpwardSpeed)
        {
            rootBody.linearVelocity -= supportNormal * (upwardSpeed - maximumGroundedUpwardSpeed);
        }

        Vector3 yawVelocity = supportNormal * Vector3.Dot(rootBody.angularVelocity, supportNormal);
        Vector3 tiltVelocity = rootBody.angularVelocity - yawVelocity;
        float speedFactor = Mathf.InverseLerp(
            straightLineStabilityStartSpeed,
            Mathf.Max(straightLineStabilityStartSpeed + 1f, steeringFadeEndSpeed),
            speed);
        float allowedTiltSpeed = Mathf.Lerp(
            maximumGroundedTiltSpeed * 1.5f,
            maximumGroundedTiltSpeed,
            speedFactor);
        if (tiltVelocity.sqrMagnitude > allowedTiltSpeed * allowedTiltSpeed)
        {
            rootBody.angularVelocity = yawVelocity + tiltVelocity.normalized * allowedTiltSpeed;
        }
    }

    private static Vector3 GetGravityUp()
    {
        return Physics.gravity.sqrMagnitude > 0.001f
            ? -Physics.gravity.normalized
            : Vector3.up;
    }

    private bool HasFiniteRigidBodyState(Rigidbody body)
    {
        if (body == null)
        {
            return false;
        }

        return HasFiniteVector(body.position) &&
               HasFiniteQuaternion(body.rotation) &&
               HasFiniteVector(body.linearVelocity) &&
               HasFiniteVector(body.angularVelocity);
    }

    private void SanitizeRigidBodyState(Rigidbody body)
    {
        if (body == null)
        {
            return;
        }

        if (HasFiniteVector(body.position) && HasFiniteQuaternion(body.rotation))
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return;
        }

        Debug.LogWarning($"{name} simulation root disabled after invalid rigidbody state.", this);
        body.isKinematic = true;
        enabled = false;
    }

    private bool HasFiniteVector(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private bool HasFiniteQuaternion(Quaternion value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z) &&
               float.IsFinite(value.w);
    }

    private void ApplyCenterOfMassOffset()
    {
        if (rootBody == null)
        {
            return;
        }

        if (!centerOfMassCached)
        {
            baseCenterOfMass = rootBody.centerOfMass;
            centerOfMassCached = true;
        }

        Vector3 centerOfMass = baseCenterOfMass;
        centerOfMass.y += centerOfMassYOffset;
        rootBody.centerOfMass = centerOfMass;
    }

    private void ApplyGripProfile(
        WheelCollider wheelCollider,
        bool isFrontWheel,
        int supportingWheelCount)
    {
        if (wheelCollider == null)
        {
            return;
        }

        wheelCollider.wheelDampingRate = Mathf.Max(wheelCollider.wheelDampingRate, minimumWheelDampingRate);
        wheelCollider.forceAppPointDistance = forceAppPointDistance;

        JointSpring suspensionSpring = wheelCollider.suspensionSpring;
        suspensionSpring.targetPosition = 0.5f;

        float vehicleMass = rootBody != null ? Mathf.Max(0.01f, rootBody.mass) : suspensionReferenceMass;
        float massPerWheel = vehicleMass / Mathf.Max(1, supportingWheelCount);
        float massRatio = suspensionReferenceMass > 0.01f
            ? Mathf.Max(0.01f, massPerWheel / suspensionReferenceMass)
            : 1f;
        float referenceSpring = suspensionSpringAtReferenceMass * suspensionSpringMultiplier * massRatio;
        float compressionTravel = Mathf.Max(
            0.01f,
            wheelCollider.suspensionDistance * (1f - suspensionSpring.targetPosition));
        float gravity = Mathf.Max(0.01f, Physics.gravity.magnitude);
        float loadBearingSpring = massPerWheel * gravity / compressionTravel;
        suspensionSpring.spring = Mathf.Max(
            referenceSpring,
            loadBearingSpring * suspensionLoadSafetyMultiplier);

        float referenceDamper = suspensionDamperAtReferenceMass * suspensionDamperMultiplier * massRatio;
        float criticalDamper = 2f * Mathf.Sqrt(suspensionSpring.spring * massPerWheel);
        suspensionSpring.damper = Mathf.Max(
            referenceDamper,
            criticalDamper * suspensionDampingRatio);
        wheelCollider.suspensionSpring = suspensionSpring;

        WheelFrictionCurve forwardFriction = wheelCollider.forwardFriction;
        forwardFriction.extremumSlip = forwardExtremumSlip;
        forwardFriction.extremumValue = forwardExtremumValue;
        forwardFriction.asymptoteSlip = forwardAsymptoteSlip;
        forwardFriction.asymptoteValue = forwardAsymptoteValue;
        forwardFriction.stiffness = isFrontWheel ? frontForwardStiffness : rearForwardStiffness;
        wheelCollider.forwardFriction = forwardFriction;

        WheelFrictionCurve sidewaysFriction = wheelCollider.sidewaysFriction;
        sidewaysFriction.extremumSlip = sidewaysExtremumSlip;
        sidewaysFriction.extremumValue = sidewaysExtremumValue;
        sidewaysFriction.asymptoteSlip = sidewaysAsymptoteSlip;
        sidewaysFriction.asymptoteValue = sidewaysAsymptoteValue;
        sidewaysFriction.stiffness = isFrontWheel ? frontSidewaysStiffness : rearSidewaysStiffness;
        wheelCollider.sidewaysFriction = sidewaysFriction;
    }

    private void ExpandBoundsForWheelSpan(ref Bounds localBounds)
    {
        foreach (FrontWheele frontWheel in frontWheels)
        {
            EncapsulateWheelSpan(frontWheel, ref localBounds);
        }

        foreach (BackWheele backWheel in backWheels)
        {
            EncapsulateWheelSpan(backWheel, ref localBounds);
        }
    }

    private void EncapsulateWheelSpan(FrontWheele wheel, ref Bounds localBounds)
    {
        if (wheel == null)
        {
            return;
        }

        if (TryGetWheelLocalBounds(wheel.transform, out Bounds wheelBounds))
        {
            localBounds.Encapsulate(wheelBounds.min);
            localBounds.Encapsulate(wheelBounds.max);
            return;
        }

        EncapsulateWheelSpan(wheel.transform.position, wheel.GetEstimatedWorldRadius(), ref localBounds);
    }

    private void EncapsulateWheelSpan(BackWheele wheel, ref Bounds localBounds)
    {
        if (wheel == null)
        {
            return;
        }

        if (TryGetWheelLocalBounds(wheel.transform, out Bounds wheelBounds))
        {
            localBounds.Encapsulate(wheelBounds.min);
            localBounds.Encapsulate(wheelBounds.max);
            return;
        }

        EncapsulateWheelSpan(wheel.transform.position, wheel.GetEstimatedWorldRadius(), ref localBounds);
    }

    private void EncapsulateWheelSpan(Vector3 worldPosition, float radius, ref Bounds localBounds)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;
        float horizontalExtent = radius * chassisWheelWidthMultiplier;

        min.x = Mathf.Min(min.x, localPosition.x - horizontalExtent);
        max.x = Mathf.Max(max.x, localPosition.x + horizontalExtent);
        min.z = Mathf.Min(min.z, localPosition.z - horizontalExtent);
        max.z = Mathf.Max(max.z, localPosition.z + horizontalExtent);

        localBounds.SetMinMax(min, max);
    }

    private bool IsWheelChild(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        return target.GetComponentInParent<FrontWheele>(true) != null ||
               target.GetComponentInParent<BackWheele>(true) != null ||
               target.GetComponentInParent<AircraftLandingGear>(true) != null ||
               target.CompareTag("wheel");
    }

    private bool ShouldIgnoreChassisBoundsTarget(Transform target)
    {
        if (target == null)
        {
            return true;
        }

        return IsWheelChild(target) ||
               target.GetComponentInParent<VehicleSeat>(true) != null ||
               target.GetComponentInParent<PowerModule>(true) != null ||
               target.GetComponentInParent<VehicleDriveLever>(true) != null;
    }

    private bool ShouldUseCoreChassisBoundsTarget(Transform target)
    {
        Transform current = target;
        while (current != null && current != transform)
        {
            if (current.CompareTag("Cube") || current.GetComponent<Cube>() != null)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private bool ShouldUseGeneratedChassisMeshTarget(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        Transform current = target;
        while (current != null && current != transform)
        {
            if (current.name == AutoChassisMeshColliderName ||
                current.name == AutoChassisFrontColliderName ||
                current.name == AutoChassisRearColliderName)
            {
                return false;
            }

            if (current.CompareTag("Cube") || current.CompareTag("Parts"))
            {
                return true;
            }

            if (current.GetComponent<Cube>() != null ||
                current.GetComponent<FrontWheele>() != null ||
                current.GetComponent<BackWheele>() != null ||
                current.GetComponent<VehicleSeat>() != null ||
                current.GetComponent<pervane>() != null ||
                current.GetComponent<Plane>() != null ||
                current.GetComponent<PowerModule>() != null ||
                current.GetComponent<VehicleDriveLever>() != null)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private bool ShouldKeepChildColliderEnabled(Collider childCollider)
    {
        if (childCollider == null)
        {
            return false;
        }

        return IsAutoChassisCollider(childCollider) ||
               IsModuleRootCollider<pervane>(childCollider) ||
               IsModuleRootCollider<Plane>(childCollider);
    }

    private bool IsAutoChassisCollider(Collider childCollider)
    {
        if (childCollider == null || childCollider.transform.parent != transform)
        {
            return false;
        }

        string objectName = childCollider.gameObject.name;
        return objectName == AutoChassisFrontColliderName ||
               objectName == AutoChassisRearColliderName ||
               objectName == AutoChassisMeshColliderName;
    }

    private bool IsModuleRootCollider<T>(Collider childCollider) where T : Component
    {
        if (childCollider == null)
        {
            return false;
        }

        T module = childCollider.GetComponent<T>();
        return module != null && childCollider.transform == module.transform;
    }

    private void EncapsulateWorldBounds(Bounds worldBounds, ref Bounds localBounds, ref bool hasBounds)
    {
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = transform.InverseTransformPoint(worldCorner);

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }
        }
    }

    private void LiftChassisBoundsAboveWheelBottom(ref Bounds localBounds)
    {
        float lowestWheelBottom = float.PositiveInfinity;

        foreach (FrontWheele frontWheel in frontWheels)
        {
            TrackWheelBottom(frontWheel, ref lowestWheelBottom);
        }

        foreach (BackWheele backWheel in backWheels)
        {
            TrackWheelBottom(backWheel, ref lowestWheelBottom);
        }

        if (float.IsPositiveInfinity(lowestWheelBottom))
        {
            return;
        }

        float desiredMinY = lowestWheelBottom + minimumChassisGroundClearance;
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        if (min.y >= desiredMinY)
        {
            return;
        }

        float height = Mathf.Max(0.05f, max.y - min.y);
        min.y = desiredMinY;
        max.y = Mathf.Max(min.y + height, max.y + (desiredMinY - localBounds.min.y));
        localBounds.SetMinMax(min, max);
    }

    private void FlattenChassisHeight(ref Bounds localBounds)
    {
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;
        float desiredHeight = Mathf.Max(minimumChassisColliderHeight, targetChassisColliderHeight);

        max.y = min.y + desiredHeight;
        localBounds.SetMinMax(min, max);
    }

    private bool TryCreateSegmentedChassisColliders(Bounds adjustedLocalBounds)
    {
        AxleSpan frontSpan = BuildAxleSpan(frontWheels);
        AxleSpan rearSpan = BuildAxleSpan(backWheels);
        if (!frontSpan.hasValue && !rearSpan.hasValue)
        {
            return false;
        }

        float frontAverageZ = frontSpan.hasValue ? frontSpan.averageZ : adjustedLocalBounds.center.z;
        float rearAverageZ = rearSpan.hasValue ? rearSpan.averageZ : adjustedLocalBounds.center.z;
        float splitZ = (frontAverageZ + rearAverageZ) * 0.5f;
        float overlap = Mathf.Max(0f, chassisSegmentOverlap);

        bool frontOnPositiveSide = frontAverageZ >= rearAverageZ;
        float frontMinZ = frontOnPositiveSide ? splitZ - overlap : adjustedLocalBounds.min.z;
        float frontMaxZ = frontOnPositiveSide ? adjustedLocalBounds.max.z : splitZ + overlap;
        float rearMinZ = frontOnPositiveSide ? adjustedLocalBounds.min.z : splitZ - overlap;
        float rearMaxZ = frontOnPositiveSide ? splitZ + overlap : adjustedLocalBounds.max.z;

        Bounds frontBounds = BuildChassisSegmentBounds(adjustedLocalBounds, frontMinZ, frontMaxZ, frontSpan);
        Bounds rearBounds = BuildChassisSegmentBounds(adjustedLocalBounds, rearMinZ, rearMaxZ, rearSpan);

        ConfigureAutoChassisSegmentCollider(AutoChassisFrontColliderName, frontBounds);
        ConfigureAutoChassisSegmentCollider(AutoChassisRearColliderName, rearBounds);
        return true;
    }

    private Bounds BuildChassisSegmentBounds(Bounds adjustedLocalBounds, float segmentMinZ, float segmentMaxZ, AxleSpan axleSpan)
    {
        Bounds segmentBounds = adjustedLocalBounds;
        if (TryCalculateCoreSegmentBounds(segmentMinZ, segmentMaxZ, out Bounds segmentCoreBounds))
        {
            segmentBounds = segmentCoreBounds;
        }

        Vector3 min = segmentBounds.min;
        Vector3 max = segmentBounds.max;
        min.y = adjustedLocalBounds.min.y;
        max.y = adjustedLocalBounds.max.y;
        min.z = Mathf.Min(segmentMinZ, segmentMaxZ);
        max.z = Mathf.Max(segmentMinZ, segmentMaxZ);

        if (axleSpan.hasValue)
        {
            min.z = Mathf.Min(min.z, axleSpan.bounds.min.z);
            max.z = Mathf.Max(max.z, axleSpan.bounds.max.z);
        }

        segmentBounds.SetMinMax(min, max);
        return segmentBounds;
    }

    private bool TryCalculateCoreSegmentBounds(float segmentMinZ, float segmentMaxZ, out Bounds localBounds)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        localBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !ShouldUseGeneratedChassisMeshTarget(renderer.transform))
            {
                continue;
            }

            if (!TryGetLocalBounds(renderer.bounds, out Bounds candidateBounds) ||
                candidateBounds.max.z < segmentMinZ ||
                candidateBounds.min.z > segmentMaxZ)
            {
                continue;
            }

            EncapsulateLocalBounds(candidateBounds, ref localBounds, ref hasBounds);
        }

        if (!hasBounds)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            foreach (Collider childCollider in colliders)
            {
                if (childCollider == null || childCollider is WheelCollider || !ShouldUseCoreChassisBoundsTarget(childCollider.transform))
                {
                    continue;
                }

                if (!TryGetLocalBounds(childCollider.bounds, out Bounds candidateBounds) ||
                    candidateBounds.max.z < segmentMinZ ||
                    candidateBounds.min.z > segmentMaxZ)
                {
                    continue;
                }

                EncapsulateLocalBounds(candidateBounds, ref localBounds, ref hasBounds);
            }
        }

        return hasBounds;
    }

    private AxleSpan BuildAxleSpan(FrontWheele[] wheels)
    {
        AxleSpan span = default;
        foreach (FrontWheele wheel in wheels)
        {
            if (wheel == null)
            {
                continue;
            }

            AddWheelToAxleSpan(wheel.transform, wheel.transform.position, wheel.GetEstimatedWorldRadius(), ref span);
        }

        FinalizeAxleSpan(ref span);
        return span;
    }

    private AxleSpan BuildAxleSpan(BackWheele[] wheels)
    {
        AxleSpan span = default;
        foreach (BackWheele wheel in wheels)
        {
            if (wheel == null)
            {
                continue;
            }

            AddWheelToAxleSpan(wheel.transform, wheel.transform.position, wheel.GetEstimatedWorldRadius(), ref span);
        }

        FinalizeAxleSpan(ref span);
        return span;
    }

    private void AddWheelToAxleSpan(Transform wheelRoot, Vector3 worldPosition, float radius, ref AxleSpan span)
    {
        if (wheelRoot != null && TryGetWheelLocalBounds(wheelRoot, out Bounds wheelBounds))
        {
            if (!span.hasValue)
            {
                span.bounds = wheelBounds;
                span.hasValue = true;
            }
            else
            {
                span.bounds.Encapsulate(wheelBounds.min);
                span.bounds.Encapsulate(wheelBounds.max);
            }

            span.averageZ += wheelBounds.center.z;
            span.count++;
            return;
        }

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float horizontalExtent = radius * chassisWheelWidthMultiplier;
        Vector3 min = localPosition - new Vector3(horizontalExtent, 0f, horizontalExtent);
        Vector3 max = localPosition + new Vector3(horizontalExtent, 0f, horizontalExtent);

        if (!span.hasValue)
        {
            span.bounds = new Bounds(localPosition, Vector3.zero);
            span.bounds.SetMinMax(min, max);
            span.hasValue = true;
        }
        else
        {
            span.bounds.Encapsulate(min);
            span.bounds.Encapsulate(max);
        }

        span.averageZ += localPosition.z;
        span.count++;
    }

    private void FinalizeAxleSpan(ref AxleSpan span)
    {
        if (!span.hasValue || span.count <= 0)
        {
            span.averageZ = 0f;
            return;
        }

        span.averageZ /= span.count;
    }

    private bool TryGetWheelLocalBounds(Transform wheelRoot, out Bounds localBounds)
    {
        bool hasBounds = false;
        localBounds = default;

        Collider[] colliders = wheelRoot.GetComponentsInChildren<Collider>(true);
        foreach (Collider childCollider in colliders)
        {
            if (childCollider == null || childCollider is WheelCollider)
            {
                continue;
            }

            if (!TryGetLocalBounds(childCollider.bounds, out Bounds candidateBounds))
            {
                continue;
            }

            EncapsulateLocalBounds(candidateBounds, ref localBounds, ref hasBounds);
        }

        if (!hasBounds)
        {
            Renderer[] renderers = wheelRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!TryGetLocalBounds(renderer.bounds, out Bounds candidateBounds))
                {
                    continue;
                }

                EncapsulateLocalBounds(candidateBounds, ref localBounds, ref hasBounds);
            }
        }

        return hasBounds;
    }

    private void ConfigureAutoChassisSegmentCollider(string objectName, Bounds localBounds)
    {
        BoxCollider collider = EnsureAutoChassisSegmentCollider(objectName);
        collider.center = localBounds.center;
        collider.size = GetPositiveColliderSize(localBounds.size + chassisPadding);
        collider.enabled = true;
    }

    private BoxCollider EnsureAutoChassisSegmentCollider(string objectName)
    {
        Transform existing = transform.Find(objectName);
        Transform childTransform = existing;
        if (childTransform == null)
        {
            GameObject colliderObject = new GameObject(objectName);
            colliderObject.layer = gameObject.layer;
            childTransform = colliderObject.transform;
            childTransform.SetParent(transform, false);
            childTransform.localPosition = Vector3.zero;
            childTransform.localRotation = Quaternion.identity;
            childTransform.localScale = Vector3.one;
        }

        BoxCollider collider = childTransform.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = childTransform.gameObject.AddComponent<BoxCollider>();
        }

        return collider;
    }

    private bool TryCreateGeneratedChassisMeshCollider()
    {
        if (!TryBuildGeneratedChassisCollisionMesh(out Mesh collisionMesh))
        {
            SetAutoChassisMeshColliderEnabled(false);
            return false;
        }

        MeshCollider meshCollider = EnsureAutoChassisMeshCollider();
        meshCollider.enabled = false;
        meshCollider.sharedMesh = null;
        meshCollider.convex = true;
        meshCollider.cookingOptions =
            MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning |
            MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;
        meshCollider.sharedMesh = collisionMesh;
        meshCollider.enabled = true;
        ConfigureGeneratedChassisMeshPreview(meshCollider.transform, collisionMesh);
        return true;
    }

    private bool TryBuildGeneratedChassisCollisionMesh(out Mesh collisionMesh)
    {
        collisionMesh = null;
        Mesh unitCubeMesh = CreateGeneratedCollisionUnitCubeMesh();
        System.Collections.Generic.List<CombineInstance> combineInstances = new System.Collections.Generic.List<CombineInstance>();
        AppendGeneratedChassisModuleMeshes(unitCubeMesh, combineInstances);

        // Aracta gorecek bir Cube mesh'i yoksa eski guvenli govde kutusuna geri don.
        if (combineInstances.Count == 0 && TryCalculateChassisBounds(out Bounds fallbackBounds))
        {
            LiftGeneratedChassisMeshAboveWheelBottom(ref fallbackBounds);
            AddGeneratedCollisionBox(fallbackBounds, unitCubeMesh, combineInstances);
        }

        if (combineInstances.Count == 0)
        {
            if (Application.isPlaying)
            {
                Destroy(unitCubeMesh);
            }
            else
            {
                DestroyImmediate(unitCubeMesh);
            }

            return false;
        }

        ReleaseGeneratedChassisCollisionMesh();

        Mesh combinedMesh = new Mesh
        {
            name = $"{name}_GeneratedChassisCollision"
        };

        if (combineInstances.Count * 24 > 65535)
        {
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true, false);
        combinedMesh.RecalculateBounds();
        combinedMesh.RecalculateNormals();

        if (Application.isPlaying)
        {
            Destroy(unitCubeMesh);
        }
        else
        {
            DestroyImmediate(unitCubeMesh);
        }

        if (combinedMesh.vertexCount == 0)
        {
            if (Application.isPlaying)
            {
                Destroy(combinedMesh);
            }
            else
            {
                DestroyImmediate(combinedMesh);
            }

            return false;
        }

        generatedChassisCollisionMesh = combinedMesh;
        collisionMesh = generatedChassisCollisionMesh;
        return true;
    }

    private void AppendGeneratedChassisModuleMeshes(Mesh unitCubeMesh, System.Collections.Generic.List<CombineInstance> combineInstances)
    {
        if (unitCubeMesh == null || combineInstances == null)
        {
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !ShouldUseGeneratedChassisMeshTarget(renderer.transform))
            {
                continue;
            }

            if (TryGetLocalBounds(renderer.bounds, out Bounds localBounds))
            {
                if (TryClampGeneratedMeshAboveGround(ref localBounds))
                {
                    AddGeneratedCollisionBox(localBounds, unitCubeMesh, combineInstances);
                }
            }
        }
    }

    private bool TryClampGeneratedMeshAboveGround(ref Bounds localBounds)
    {
        if (!TryGetWheelEnvelopeMetrics(out float lowestWheelBottom, out float largestWheelRadius))
        {
            return true;
        }

        // Mesh'in alt yarisi tekerlerin serbest calismasi icin bos kalir.
        float minimumMeshBottom = lowestWheelBottom + Mathf.Max(minimumChassisGroundClearance, largestWheelRadius * 0.5f);
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        if (max.y <= minimumMeshBottom)
        {
            return false;
        }

        min.y = Mathf.Max(min.y, minimumMeshBottom);
        localBounds.SetMinMax(min, max);
        return true;
    }

    private void LiftGeneratedChassisMeshAboveWheelBottom(ref Bounds localBounds)
    {
        if (!TryGetWheelEnvelopeMetrics(out float lowestWheelBottom, out float largestWheelRadius))
        {
            return;
        }

        float suspensionGap = Mathf.Max(minimumChassisGroundClearance, largestWheelRadius * 0.5f);
        float desiredMinY = lowestWheelBottom + suspensionGap;
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;

        if (min.y >= desiredMinY)
        {
            return;
        }

        min.y = desiredMinY;
        if (max.y <= min.y)
        {
            max.y = min.y + Mathf.Max(0.05f, targetChassisColliderHeight);
        }

        localBounds.SetMinMax(min, max);
    }

    private bool TryGetWheelEnvelopeMetrics(out float lowestWheelBottom, out float largestWheelRadius)
    {
        lowestWheelBottom = float.PositiveInfinity;
        largestWheelRadius = 0f;

        foreach (FrontWheele frontWheel in frontWheels)
        {
            TrackWheelEnvelope(frontWheel, ref lowestWheelBottom, ref largestWheelRadius);
        }

        foreach (BackWheele backWheel in backWheels)
        {
            TrackWheelEnvelope(backWheel, ref lowestWheelBottom, ref largestWheelRadius);
        }

        return !float.IsPositiveInfinity(lowestWheelBottom) && largestWheelRadius > 0f;
    }

    private void TrackWheelEnvelope(FrontWheele wheel, ref float lowestWheelBottom, ref float largestWheelRadius)
    {
        if (wheel == null)
        {
            return;
        }

        float wheelRadius = Mathf.Max(0.01f, wheel.GetEstimatedWorldRadius());
        TrackWheelBottom(wheel.transform.position, wheelRadius, ref lowestWheelBottom);
        largestWheelRadius = Mathf.Max(largestWheelRadius, wheelRadius);
    }

    private void TrackWheelEnvelope(BackWheele wheel, ref float lowestWheelBottom, ref float largestWheelRadius)
    {
        if (wheel == null)
        {
            return;
        }

        float wheelRadius = Mathf.Max(0.01f, wheel.GetEstimatedWorldRadius());
        TrackWheelBottom(wheel.transform.position, wheelRadius, ref lowestWheelBottom);
        largestWheelRadius = Mathf.Max(largestWheelRadius, wheelRadius);
    }

    private void AddGeneratedCollisionBox(Bounds localBounds, Mesh unitCubeMesh, System.Collections.Generic.List<CombineInstance> combineInstances)
    {
        if (unitCubeMesh == null || combineInstances == null)
        {
            return;
        }

        Vector3 positiveSize = GetPositiveColliderSize(localBounds.size);
        CombineInstance combineInstance = new CombineInstance
        {
            mesh = unitCubeMesh,
            transform = Matrix4x4.TRS(localBounds.center, Quaternion.identity, positiveSize)
        };

        combineInstances.Add(combineInstance);
    }

    private Mesh CreateGeneratedCollisionUnitCubeMesh()
    {
        Mesh cubeMesh = new Mesh
        {
            name = $"{name}_GeneratedCollisionUnitCube"
        };

        Vector3[] vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f),
            new Vector3( 0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3( 0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f, -0.5f), new Vector3( 0.5f,  0.5f, -0.5f), new Vector3( 0.5f,  0.5f,  0.5f),
            new Vector3(-0.5f,  0.5f,  0.5f), new Vector3( 0.5f,  0.5f,  0.5f), new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f,  0.5f), new Vector3(-0.5f, -0.5f,  0.5f)
        };

        int[] triangles = new int[]
        {
            0, 2, 1, 0, 3, 2,
            4, 6, 5, 4, 7, 6,
            8, 10, 9, 8, 11, 10,
            12, 14, 13, 12, 15, 14,
            16, 18, 17, 16, 19, 18,
            20, 22, 21, 20, 23, 22
        };

        cubeMesh.vertices = vertices;
        cubeMesh.triangles = triangles;
        cubeMesh.RecalculateNormals();
        cubeMesh.RecalculateBounds();
        return cubeMesh;
    }

    private MeshCollider EnsureAutoChassisMeshCollider()
    {
        Transform meshChild = transform.Find(AutoChassisMeshColliderName);
        if (meshChild == null)
        {
            GameObject meshColliderObject = new GameObject(AutoChassisMeshColliderName);
            meshColliderObject.layer = gameObject.layer;
            meshChild = meshColliderObject.transform;
            meshChild.SetParent(transform, false);
        }

        meshChild.localPosition = Vector3.zero;
        meshChild.localRotation = Quaternion.identity;
        meshChild.localScale = Vector3.one;

        MeshCollider meshCollider = meshChild.GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
            meshCollider = meshChild.gameObject.AddComponent<MeshCollider>();
        }

        RemoveLegacyRootGeneratedMeshCollider();
        return meshCollider;
    }

    private void ConfigureGeneratedChassisMeshPreview(Transform meshChild, Mesh collisionMesh)
    {
        if (meshChild == null)
        {
            return;
        }

        MeshFilter meshFilter = meshChild.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = meshChild.gameObject.AddComponent<MeshFilter>();
        }

        MeshRenderer meshRenderer = meshChild.GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            meshRenderer = meshChild.gameObject.AddComponent<MeshRenderer>();
        }

        meshFilter.sharedMesh = collisionMesh;
        meshRenderer.enabled = showGeneratedChassisMesh;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        if (generatedChassisPreviewMaterial == null)
        {
            Shader previewShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (previewShader == null)
            {
                previewShader = Shader.Find("Standard");
            }

            if (previewShader == null)
            {
                previewShader = Shader.Find("Sprites/Default");
            }

            generatedChassisPreviewMaterial = new Material(previewShader)
            {
                name = "Generated Chassis Preview (Runtime)"
            };
        }

        ConfigureTransparentPreviewMaterial(generatedChassisPreviewMaterial, generatedChassisMeshColor);
        meshRenderer.sharedMaterial = generatedChassisPreviewMaterial;
    }

    private void ConfigureTransparentPreviewMaterial(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        color.a = Mathf.Clamp01(color.a);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        // URP materyallerinde Surface = Transparent yapar; diger shader'larda da alpha degeri korunur.
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    private void SetAutoChassisMeshColliderEnabled(bool enabled)
    {
        Transform meshChild = transform.Find(AutoChassisMeshColliderName);
        if (meshChild != null)
        {
            MeshCollider meshCollider = meshChild.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                if (!enabled)
                {
                    meshCollider.sharedMesh = null;
                }

                meshCollider.enabled = enabled;
            }

            MeshFilter meshFilter = meshChild.GetComponent<MeshFilter>();
            if (meshFilter != null && !enabled)
            {
                meshFilter.sharedMesh = null;
            }
        }

        if (!enabled && meshChild != null)
        {
            if (Application.isPlaying)
            {
                Destroy(meshChild.gameObject);
            }
            else
            {
                DestroyImmediate(meshChild.gameObject);
            }
        }

        RemoveLegacyRootGeneratedMeshCollider();
    }

    private void ReleaseGeneratedChassisCollisionMesh()
    {
        if (generatedChassisCollisionMesh == null)
        {
            return;
        }

        Transform meshChild = transform.Find(AutoChassisMeshColliderName);
        if (meshChild != null)
        {
            MeshCollider childMeshCollider = meshChild.GetComponent<MeshCollider>();
            if (childMeshCollider != null && childMeshCollider.sharedMesh == generatedChassisCollisionMesh)
            {
                childMeshCollider.sharedMesh = null;
            }

            MeshFilter childMeshFilter = meshChild.GetComponent<MeshFilter>();
            if (childMeshFilter != null && childMeshFilter.sharedMesh == generatedChassisCollisionMesh)
            {
                childMeshFilter.sharedMesh = null;
            }
        }

        RemoveLegacyRootGeneratedMeshCollider();

        if (Application.isPlaying)
        {
            Destroy(generatedChassisCollisionMesh);
        }
        else
        {
            DestroyImmediate(generatedChassisCollisionMesh);
        }

        generatedChassisCollisionMesh = null;

        if (generatedChassisPreviewMaterial != null)
        {
            if (Application.isPlaying)
            {
                Destroy(generatedChassisPreviewMaterial);
            }
            else
            {
                DestroyImmediate(generatedChassisPreviewMaterial);
            }

            generatedChassisPreviewMaterial = null;
        }
    }

    private void RemoveLegacyRootGeneratedMeshCollider()
    {
        MeshCollider rootMeshCollider = GetComponent<MeshCollider>();
        if (rootMeshCollider == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(rootMeshCollider);
        }
        else
        {
            DestroyImmediate(rootMeshCollider);
        }
    }

    private void DisableLegacyRootChassisCollider()
    {
        BoxCollider rootCollider = GetComponent<BoxCollider>();
        if (rootCollider != null)
        {
            rootCollider.enabled = false;
        }
    }

    private void SetAutoChassisSegmentCollidersEnabled(bool enabled)
    {
        SetAutoChassisSegmentColliderEnabled(AutoChassisFrontColliderName, enabled);
        SetAutoChassisSegmentColliderEnabled(AutoChassisRearColliderName, enabled);
    }

    private void SetAutoChassisSegmentColliderEnabled(string objectName, bool enabled)
    {
        Transform segmentTransform = transform.Find(objectName);
        if (segmentTransform == null)
        {
            return;
        }

        BoxCollider segmentCollider = segmentTransform.GetComponent<BoxCollider>();
        if (segmentCollider != null)
        {
            segmentCollider.enabled = enabled;
        }
    }

    private void TrackWheelBottom(FrontWheele wheel, ref float lowestWheelBottom)
    {
        if (wheel == null)
        {
            return;
        }

        TrackWheelBottom(wheel.transform.position, wheel.GetEstimatedWorldRadius(), ref lowestWheelBottom);
    }

    private void TrackWheelBottom(BackWheele wheel, ref float lowestWheelBottom)
    {
        if (wheel == null)
        {
            return;
        }

        TrackWheelBottom(wheel.transform.position, wheel.GetEstimatedWorldRadius(), ref lowestWheelBottom);
    }

    private void TrackWheelBottom(Vector3 worldPosition, float wheelRadius, ref float lowestWheelBottom)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float wheelBottom = localPosition.y - wheelRadius;
        if (wheelBottom < lowestWheelBottom)
        {
            lowestWheelBottom = wheelBottom;
        }
    }

    private Vector3 GetPositiveColliderSize(Vector3 size)
    {
        return new Vector3(
            Mathf.Max(0.05f, Mathf.Abs(size.x)),
            Mathf.Max(0.05f, Mathf.Abs(size.y)),
            Mathf.Max(0.05f, Mathf.Abs(size.z)));
    }

    private bool TryGetLocalBounds(Bounds worldBounds, out Bounds localBounds)
    {
        bool hasBounds = false;
        localBounds = default;
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = transform.InverseTransformPoint(worldCorner);

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }
        }

        return hasBounds;
    }

    private void EncapsulateLocalBounds(Bounds sourceBounds, ref Bounds targetBounds, ref bool hasBounds)
    {
        if (!hasBounds)
        {
            targetBounds = sourceBounds;
            hasBounds = true;
            return;
        }

        targetBounds.Encapsulate(sourceBounds.min);
        targetBounds.Encapsulate(sourceBounds.max);
    }

    private void NormalizePropellerModuleColliders()
    {
        Vector3 normalizedSize = GetPositiveColliderSize(propellerModuleColliderSize);

        foreach (pervane helicopterPropeller in helicopterPropellers)
        {
            NormalizePropellerModuleCollider(helicopterPropeller, normalizedSize);
        }

        foreach (Plane planePropeller in planePropellers)
        {
            NormalizePropellerModuleCollider(planePropeller, normalizedSize);
        }
    }

    private void NormalizePropellerModuleCollider(Component module, Vector3 normalizedSize)
    {
        if (module == null)
        {
            return;
        }

        BoxCollider moduleCollider = module.GetComponent<BoxCollider>();
        if (moduleCollider == null)
        {
            moduleCollider = module.gameObject.AddComponent<BoxCollider>();
        }

        moduleCollider.center = Vector3.zero;
        moduleCollider.size = normalizedSize;
        moduleCollider.enabled = true;
    }
}
