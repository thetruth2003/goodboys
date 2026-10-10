using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PlayerController : NetworkBehaviour
{
    private struct BuildPartSnapshot
    {
        public Rigidbody Body;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool WasActive;
    }

    private struct PlayerSnapshot
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float VerticalLookRotation;
    }

    private struct QuickBuildPlacement
    {
        public GameObject Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        public string Label;
    }

    public static bool BuildModeActive { get; private set; } = true;

    public float moveSpeed = 5f;
    public float lookSpeed = 2f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;
    public Camera playerCamera;
    public GameObject gridManager;
    public float maxRaycastDistance = 10f;
    public float vehicleAnchorRadius = 0.35f;
    public float simulationLinearDamping = 0.2f;
    public float simulationAngularDrag = 2.5f;
    public float simulationWheelLinearDamping = 0f;
    public float simulationWheelAngularDamping = 0.05f;
    public int simulationSolverIterations = 20;
    public int simulationSolverVelocityIterations = 20;
    public float simulationSleepThreshold = 0.08f;
    public float simulationMaxDepenetrationVelocity = 15f;
    public bool releaseWheelRotationConstraintsInSimulation = true;
    public float uprightAssistStrength = 1.75f;
    public float uprightAssistDamping = 0.85f;
    public float uprightAssistMaxTorque = 6f;
    [Tooltip("Araca fazladan dik kalma torku uygular. Sade WheelCollider fiziginde varsayilan kapali tutulur.")]
    public bool useSimulationUprightAssist = false;
    public KeyCode seatInteractKey = KeyCode.E;
    public float seatInteractionDistance = 3f;
    public KeyCode debugFlyToggleKey = KeyCode.P;
    public float debugFlyMoveSpeed = 12f;
    public float debugFlyVerticalSpeed = 8f;

    [Header("Koltuk Test Kamerasi")]
    [Tooltip("Koltuga oturunca araci sabit bir dunya acisindan geriden izler.")]
    public bool useSeatedVehicleTestCamera = true;
    [Min(2f)] public float seatedTestCameraDistance = 10f;
    [Min(0f)] public float seatedTestCameraHeight = 3.5f;
    [Range(1f, 2f)] public float seatedTestCameraFramePadding = 1.2f;
    [Min(0.1f)] public float seatedTestCameraPositionSharpness = 14f;
    [Min(0.1f)] public float seatedTestCameraRotationSharpness = 9f;

    public GameObject quickBuildCubePrefab;
    public GameObject quickBuildSeatPrefab;
    public GameObject quickBuildFrontWheelPrefab;
    public GameObject quickBuildRearWheelPrefab;

    private CharacterController characterController;
    private ObjectGrabber objectGrabber;
    private CoopPlayerInputState coopPlayerInputState;
    private VehicleSeat currentSeat;
    private float seatedYawRotation;
    private float verticalLookRotation = 0f;
    private float verticalVelocity = GroundedVerticalVelocity;
    private readonly List<GameObject> hiddenBuildParts = new List<GameObject>();
    private readonly List<BuildPartSnapshot> buildSnapshot = new List<BuildPartSnapshot>();
    private readonly List<GameObject> simulationVehicleRoots = new List<GameObject>();
    private bool hasBuildSnapshot;
    private PlayerSnapshot buildPlayerSnapshot;
    private bool hasBuildPlayerSnapshot;
    private bool localInputEnabled = true;
    private bool hasInitializedLocalSessionState;
    private bool isDebugFlyMode;
    private bool seatedTestCameraActive;
    private Transform seatedTestCameraOriginalParent;
    private Vector3 seatedTestCameraOriginalLocalPosition;
    private Quaternion seatedTestCameraOriginalLocalRotation;
    private Transform seatedTestCameraVehicleRoot;
    private Vector3 seatedTestCameraTargetLocalPosition;
    private Vector3 seatedTestCameraFollowForward;
    private float seatedTestCameraFollowDistance;

    private const float GroundedVerticalVelocity = -2f;

    private readonly NetworkVariable<bool> networkIsSimulationActive = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            networkIsSimulationActive.Value = !IsBuildMode;
        }
        networkIsSimulationActive.OnValueChanged += HandleSimulationModeChanged;

        if (IsClient)
        {
            if (IsNetworkSessionActive())
            {
                RequestSyncBuildPartsServerRpc();
                RequestSyncSeatsServerRpc();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        EndSeatedVehicleTestCamera();
        base.OnNetworkDespawn();
        networkIsSimulationActive.OnValueChanged -= HandleSimulationModeChanged;
    }

    private void HandleSimulationModeChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            if (IsBuildMode)
            {
                StartSimulationMode();
            }
        }
        else
        {
            if (!IsBuildMode)
            {
                EnterBuildMode();
            }
        }
    }

    private void ToggleSimulationMode()
    {
        if (IsBuildMode)
        {
            networkIsSimulationActive.Value = true;
        }
        else
        {
            networkIsSimulationActive.Value = false;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleSimulationServerRpc()
    {
        ToggleSimulationMode();
    }

    private List<VehicleSeat> GetDeterministicSeats()
    {
        List<VehicleSeat> list = new List<VehicleSeat>(FindObjectsByType<VehicleSeat>(FindObjectsSortMode.None));
        list.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;
            if (!Mathf.Approximately(pa.x, pb.x)) return pa.x.CompareTo(pb.x);
            if (!Mathf.Approximately(pa.y, pb.y)) return pa.y.CompareTo(pb.y);
            return pa.z.CompareTo(pb.z);
        });
        return list;
    }

    private List<Rigidbody> GetDeterministicBuildParts()
    {
        List<Rigidbody> list = new List<Rigidbody>(BuildVehicleUtility.GetBuildPartRigidbodies());
        list.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;
            if (!Mathf.Approximately(pa.x, pb.x)) return pa.x.CompareTo(pb.x);
            if (!Mathf.Approximately(pa.y, pb.y)) return pa.y.CompareTo(pb.y);
            return pa.z.CompareTo(pb.z);
        });
        return list;
    }

    public void NotifyBuildPartMoved(GameObject part, Vector3 pos, Quaternion rot)
    {
        if (!IsNetworkSessionActive()) return;

        List<Rigidbody> parts = GetDeterministicBuildParts();
        Rigidbody rb = part.GetComponent<Rigidbody>();
        if (rb == null) return;

        int index = parts.IndexOf(rb);
        if (index >= 0)
        {
            SubmitBuildPartMoveServerRpc(index, pos, rot);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitBuildPartMoveServerRpc(int partIndex, Vector3 pos, Quaternion rot)
    {
        List<Rigidbody> parts = GetDeterministicBuildParts();
        if (partIndex >= 0 && partIndex < parts.Count)
        {
            Rigidbody partRb = parts[partIndex];
            if (partRb != null)
            {
                partRb.transform.SetPositionAndRotation(pos, rot);
            }
        }
        SyncBuildPartMoveClientRpc(partIndex, pos, rot);
    }

    [ClientRpc]
    private void SyncBuildPartMoveClientRpc(int partIndex, Vector3 pos, Quaternion rot)
    {
        if (IsServer) return;

        List<Rigidbody> parts = GetDeterministicBuildParts();
        if (partIndex >= 0 && partIndex < parts.Count)
        {
            Rigidbody partRb = parts[partIndex];
            if (partRb != null)
            {
                partRb.transform.SetPositionAndRotation(pos, rot);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSyncBuildPartsServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong targetClientId = rpcParams.Receive.SenderClientId;
        List<Rigidbody> parts = GetDeterministicBuildParts();
        Vector3[] positions = new Vector3[parts.Count];
        Quaternion[] rotations = new Quaternion[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != null)
            {
                positions[i] = parts[i].transform.position;
                rotations[i] = parts[i].transform.rotation;
            }
        }

        ClientRpcParams clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
        };
        SyncAllBuildPartsClientRpc(positions, rotations, clientRpcParams);
    }

    [ClientRpc]
    private void SyncAllBuildPartsClientRpc(Vector3[] positions, Quaternion[] rotations, ClientRpcParams clientRpcParams = default)
    {
        List<Rigidbody> parts = GetDeterministicBuildParts();
        for (int i = 0; i < Mathf.Min(parts.Count, positions.Length); i++)
        {
            if (parts[i] != null)
            {
                parts[i].transform.SetPositionAndRotation(positions[i], rotations[i]);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestEnterSeatServerRpc(int seatIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        List<VehicleSeat> seats = GetDeterministicSeats();
        if (seatIndex < 0 || seatIndex >= seats.Count) return;

        VehicleSeat seat = seats[seatIndex];
        if (seat != null && !seat.IsOccupied)
        {
            SyncSeatStateClientRpc(clientId, seatIndex, true);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestExitSeatServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        List<VehicleSeat> seats = GetDeterministicSeats();
        for (int i = 0; i < seats.Count; i++)
        {
            VehicleSeat seat = seats[i];
            if (seat != null && seat.IsOccupied && seat.Occupant != null)
            {
                NetworkObject no = seat.Occupant.GetComponent<NetworkObject>();
                if (no != null && no.OwnerClientId == clientId)
                {
                    SyncSeatStateClientRpc(clientId, i, false);
                    return;
                }
            }
        }
    }

    [ClientRpc]
    private void SyncSeatStateClientRpc(ulong clientId, int seatIndex, bool occupying)
    {
        PlayerController targetPlayer = FindPlayerByClientId(clientId);
        if (targetPlayer == null) return;

        List<VehicleSeat> seats = GetDeterministicSeats();
        if (seatIndex < 0 || seatIndex >= seats.Count) return;

        VehicleSeat seat = seats[seatIndex];
        if (occupying)
        {
            targetPlayer.EnterSeat(seat);
        }
        else
        {
            targetPlayer.ExitCurrentSeat();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSyncSeatsServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong targetClientId = rpcParams.Receive.SenderClientId;
        List<VehicleSeat> seats = GetDeterministicSeats();
        for (int i = 0; i < seats.Count; i++)
        {
            VehicleSeat seat = seats[i];
            if (seat != null && seat.IsOccupied && seat.Occupant != null)
            {
                NetworkObject no = seat.Occupant.GetComponent<NetworkObject>();
                if (no != null)
                {
                    ulong occupantClientId = no.OwnerClientId;
                    ClientRpcParams clientRpcParams = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } }
                    };
                    SyncSingleSeatStateClientRpc(occupantClientId, i, clientRpcParams);
                }
            }
        }
    }

    [ClientRpc]
    private void SyncSingleSeatStateClientRpc(ulong clientId, int seatIndex, ClientRpcParams clientRpcParams = default)
    {
        SyncSeatStateClientRpc(clientId, seatIndex, true);
    }

    private PlayerController FindPlayerByClientId(ulong clientId)
    {
        PlayerController[] players = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        foreach (PlayerController player in players)
        {
            NetworkObject no = player.GetComponent<NetworkObject>();
            if (no != null && no.OwnerClientId == clientId)
            {
                return player;
            }
        }
        return null;
    }

    [ClientRpc]
    private void SyncVehicleTransformClientRpc(Vector3 pos, Quaternion rot, Vector3 velocity)
    {
        if (IsServer) return;

        GameObject vehicle = GetActiveSimulationVehicle();
        if (vehicle != null)
        {
            vehicle.transform.SetPositionAndRotation(pos, rot);
            
            float speed = velocity.magnitude;
            if (speed > 0.05f)
            {
                float direction = Vector3.Dot(vehicle.transform.forward, velocity) >= 0f ? 1f : -1f;
                foreach (FrontWheele fw in vehicle.GetComponentsInChildren<FrontWheele>())
                {
                    if (fw.RuntimeWheelTransform != null)
                    {
                        fw.RuntimeWheelTransform.Rotate(Vector3.right, speed * direction * Time.deltaTime * 150f, Space.Self);
                    }
                }
                foreach (BackWheele bw in vehicle.GetComponentsInChildren<BackWheele>())
                {
                    if (bw.RuntimeWheelTransform != null)
                    {
                        bw.RuntimeWheelTransform.Rotate(Vector3.right, speed * direction * Time.deltaTime * 150f, Space.Self);
                    }
                }
            }
        }
    }

    private GameObject GetActiveSimulationVehicle()
    {
        if (simulationVehicleRoots.Count > 0 && simulationVehicleRoots[0] != null)
        {
            return simulationVehicleRoots[0];
        }
        return null;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestQuickBuildPresetServerRpc()
    {
        BuildQuickVehiclePreset();
    }

    [ClientRpc]
    private void BuildQuickVehiclePresetClientRpc()
    {
        if (IsServer) return;
        BuildQuickVehiclePresetLocal();
    }

    private void BuildQuickVehiclePreset()
    {
        if (!IsBuildMode)
        {
            return;
        }

        if (IsNetworkSessionActive())
        {
            if (IsServer)
            {
                BuildQuickVehiclePresetLocal();
                BuildQuickVehiclePresetClientRpc();
            }
            else
            {
                RequestQuickBuildPresetServerRpc();
            }
        }
        else
        {
            BuildQuickVehiclePresetLocal();
        }
    }

    public bool IsBuildMode { get; private set; } = true;

    public bool IsSeated => currentSeat != null;

    public bool CanProvideSeatlessVehicleInput => localInputEnabled && !IsBuildMode && !IsSeated && !isDebugFlyMode && isActiveAndEnabled;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        objectGrabber = GetComponent<ObjectGrabber>();
        coopPlayerInputState = GetComponent<CoopPlayerInputState>();
        InitializeLocalSessionStateIfNeeded();
    }

    void Update()
    {
        if (!hasInitializedLocalSessionState)
        {
            InitializeLocalSessionStateIfNeeded();
            if (!hasInitializedLocalSessionState)
            {
                return;
            }
        }

        if (!localInputEnabled)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetScene();
            return;
        }

        if (Input.GetKeyDown(debugFlyToggleKey))
        {
            ToggleDebugFlyMode();
        }

        if (IsBuildMode)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                TurnObject(90f);
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                TurnObject(-90f);
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                TurnObject(Vector3.right, -90f, Space.Self);
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                TurnObject(Vector3.right, 90f, Space.Self);
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (IsNetworkSessionActive())
                {
                    if (IsServer)
                    {
                        ToggleSimulationMode();
                    }
                    else
                    {
                        RequestToggleSimulationServerRpc();
                    }
                }
                else
                {
                    StartSimulationMode();
                }
            }

            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                BuildQuickVehiclePreset();
            }
        }
        else if (!isDebugFlyMode && Input.GetKeyDown(seatInteractKey))
        {
            ToggleSeatInteraction();
        }

        HandleMovement();
        HandleLook();
        UpdateSeatedTransform();
    }

    private void LateUpdate()
    {
        UpdateSeatedVehicleTestCamera();
    }

    void TurnObject(float rotation)
    {
        TurnObject(Vector3.up, rotation, Space.World);
    }

    void TurnObject(Vector3 axis, float rotation, Space relativeTo)
    {
        if (objectGrabber != null && objectGrabber.IsCarrying)
        {
            objectGrabber.RotateHeldObject(axis, rotation, relativeTo);
            return;
        }

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance))
        {
            return;
        }

        Rigidbody hitBody = hit.rigidbody != null ? hit.rigidbody : hit.collider.GetComponentInParent<Rigidbody>();
        if (hitBody == null || !IsBuildPartTag(hitBody.gameObject.tag))
        {
            return;
        }

        if (objectGrabber != null && !objectGrabber.CanRotateObjectInBuildArea(hitBody.gameObject))
        {
            return;
        }

        hitBody.transform.Rotate(axis, rotation, relativeTo);
        objectGrabber?.SnapObjectToBuildRotation(hitBody.gameObject);
        Debug.Log("Temas edilen obje: " + hitBody.gameObject.name);
    }

    public float GetVehicleSteeringInput()
    {
        if (isDebugFlyMode)
        {
            return 0f;
        }

        if (!localInputEnabled && coopPlayerInputState != null && coopPlayerInputState.HasReplicatedVehicleInput)
        {
            return coopPlayerInputState.ReplicatedSteeringInput;
        }

        return Input.GetAxisRaw("Horizontal");
    }

    public float GetVehicleThrottleInput()
    {
        if (isDebugFlyMode)
        {
            return 0f;
        }

        if (!localInputEnabled && coopPlayerInputState != null && coopPlayerInputState.HasReplicatedVehicleInput)
        {
            return coopPlayerInputState.ReplicatedThrottleInput;
        }

        return Input.GetAxisRaw("Vertical");
    }

    public bool IsVehicleBrakePressed()
    {
        if (isDebugFlyMode)
        {
            return false;
        }

        if (!localInputEnabled && coopPlayerInputState != null && coopPlayerInputState.HasReplicatedVehicleInput)
        {
            return coopPlayerInputState.ReplicatedBrakePressed;
        }

        return Input.GetKey(KeyCode.Space);
    }

    public float GetLocalVehicleSteeringInput()
    {
        if (isDebugFlyMode)
        {
            return 0f;
        }

        return Input.GetAxisRaw("Horizontal");
    }

    public float GetLocalVehicleThrottleInput()
    {
        if (isDebugFlyMode)
        {
            return 0f;
        }

        return Input.GetAxisRaw("Vertical");
    }

    public bool IsLocalVehicleBrakePressed()
    {
        if (isDebugFlyMode)
        {
            return false;
        }

        return Input.GetKey(KeyCode.Space);
    }

    public void SetLocalInputEnabled(bool enabled)
    {
        localInputEnabled = enabled;
        if (enabled)
        {
            InitializeLocalSessionStateIfNeeded();
        }
        else
        {
            EndSeatedVehicleTestCamera();
        }
    }

    public bool IsLocalInputEnabled()
    {
        return localInputEnabled;
    }

    private void EnterBuildMode()
    {
        SetDebugFlyMode(false);
        ExitCurrentSeat();
        VehicleFinishLineSystem.ResetVictoryState();
        LevelTimerSystem.ResetTimerState();
        DestroySimulationVehicles();
        IsBuildMode = true;
        BuildModeActive = true;
        verticalVelocity = GroundedVerticalVelocity;
        SetHiddenBuildPartsActive(true);
        if (gridManager != null)
        {
            gridManager.SetActive(true);
        }

        SetBuildPartKinematicState(true);
        SetDynamicCenterOfMassEnabled(false, null);
        SetUprightStabilizersEnabled(false, null);
        SetWheelRootCollidersEnabled(true, null);
        SetWheelScriptsEnabled(false, null);
        objectGrabber?.SnapAllObjectsInsideBuildArea();
    }

    private void StartSimulationMode()
    {
        if (objectGrabber != null)
        {
            objectGrabber.ReleaseHeldObjectForSimulation();
            objectGrabber.SnapAllObjectsInsideBuildArea();
        }

        if (!HasAnchoredBuildPart())
        {
            Debug.LogWarning("Grid ustunde fizik acilacak bir arac bulunamadi.");
            return;
        }

        BuildModeActive = false;
        Physics.SyncTransforms();
        FinalizePendingAttachments();
        CaptureBuildSnapshot();

        HashSet<Rigidbody> vehicleBodies = CollectVehicleBodiesFromGrid();
        if (vehicleBodies.Count == 0)
        {
            BuildModeActive = true;
            Debug.LogWarning("Grid ustunde fizik acilacak bir arac bulunamadi.");
            return;
        }

        IsBuildMode = false;

        if (gridManager != null)
        {
            gridManager.SetActive(false);
        }

        verticalVelocity = GroundedVerticalVelocity;
        SetBuildPartKinematicState(true);
        HideUnusedBuildParts(vehicleBodies);
        CreateSimulationVehicles(vehicleBodies);
        DeactivateVehicleBuildParts(vehicleBodies);

        Physics.SyncTransforms();
        SetWheelRootCollidersEnabled(false, null);
        SetWheelScriptsEnabled(true, null);
        Debug.Log("Build mode kapandi, arac simulasyona gecti.");
    }

    private bool HasAnchoredBuildPart()
    {
        List<Rigidbody> allBodies = GetBuildPartRigidbodies();
        foreach (Rigidbody body in allBodies)
        {
            if (IsAnchoredToGrid(body.transform))
            {
                return true;
            }
        }

        return false;
    }

    private void FinalizePendingAttachments()
    {
        Physics.SyncTransforms();

        for (int pass = 0; pass < 3; pass++)
        {
            bool attachedSomething = false;

            AttachPoint[] cubeAttachPoints = FindObjectsByType<AttachPoint>(FindObjectsSortMode.None);
            foreach (AttachPoint attachPoint in cubeAttachPoints)
            {
                attachedSomething |= attachPoint.TryFinalizeAttachment();
            }

            AttachPointParts[] partAttachPoints = FindObjectsByType<AttachPointParts>(FindObjectsSortMode.None);
            foreach (AttachPointParts attachPoint in partAttachPoints)
            {
                attachedSomething |= attachPoint.TryFinalizeAttachment();
            }

            Physics.SyncTransforms();

            if (!attachedSomething)
            {
                break;
            }
        }
    }

    private void SetBuildPartKinematicState(bool isKinematic)
    {
        List<Rigidbody> buildBodies = GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            body.isKinematic = isKinematic;
        }
    }

    private List<Rigidbody> GetBuildPartRigidbodies()
    {
        return new List<Rigidbody>(BuildVehicleUtility.GetBuildPartRigidbodies());
    }

    private HashSet<Rigidbody> CollectVehicleBodiesFromGrid()
    {
        HashSet<Rigidbody> allBodies = BuildVehicleUtility.GetBuildPartRigidbodies();
        HashSet<Rigidbody> vehicleBodies = new HashSet<Rigidbody>();
        Queue<Rigidbody> pending = new Queue<Rigidbody>();

        foreach (Rigidbody body in allBodies)
        {
            if (IsAnchoredToGrid(body.transform))
            {
                vehicleBodies.Add(body);
                pending.Enqueue(body);
            }
        }

        while (pending.Count > 0)
        {
            Rigidbody current = pending.Dequeue();
            List<Rigidbody> connectedBodies = GetConnectedBuildBodies(current, allBodies);

            foreach (Rigidbody connectedBody in connectedBodies)
            {
                if (vehicleBodies.Add(connectedBody))
                {
                    pending.Enqueue(connectedBody);
                }
            }
        }

        IncludeFreeBalloonTopBodies(allBodies, vehicleBodies);
        return vehicleBodies;
    }

    private void IncludeFreeBalloonTopBodies(HashSet<Rigidbody> allBodies, HashSet<Rigidbody> vehicleBodies)
    {
        List<Transform> candidateCubeRoots = new List<Transform>();
        foreach (Rigidbody body in allBodies)
        {
            if (body != null && body.CompareTag("Cube"))
            {
                candidateCubeRoots.Add(body.transform);
            }
        }

        foreach (Rigidbody body in allBodies)
        {
            if (body == null || vehicleBodies.Contains(body) || body.GetComponent<BalloonTopRise>() == null)
            {
                continue;
            }

            bool anchoredToGrid = IsAnchoredToGrid(body.transform);
            bool hasSupportCube = BalloonTopRise.TryFindSupportCubeBelow(
                body.transform,
                candidateCubeRoots,
                BalloonTopRise.DefaultSupportSearchPadding,
                out _,
                out _);

            if (anchoredToGrid || hasSupportCube)
            {
                vehicleBodies.Add(body);
            }
        }
    }

    private bool IsAnchoredToGrid(Transform targetTransform)
    {
        if (targetTransform == null)
        {
            return false;
        }

        if (TryGetAnchoringBounds(targetTransform, out Bounds bounds))
        {
            Vector3 halfExtents = new Vector3(
                Mathf.Max(0.05f, bounds.extents.x * 0.9f),
                Mathf.Max(0.04f, Mathf.Min(0.12f, bounds.extents.y * 0.25f)),
                Mathf.Max(0.05f, bounds.extents.z * 0.9f));

            Vector3 boxCenter = new Vector3(
                bounds.center.x,
                bounds.min.y + halfExtents.y + 0.02f,
                bounds.center.z);

            Collider[] bottomHits = Physics.OverlapBox(
                boxCenter,
                halfExtents,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Collide);

            foreach (Collider hit in bottomHits)
            {
                if (hit != null && hit.CompareTag("Cell"))
                {
                    return true;
                }
            }
        }

        Collider[] hits = Physics.OverlapSphere(
            targetTransform.position,
            vehicleAnchorRadius,
            ~0,
            QueryTriggerInteraction.Collide);

        foreach (Collider hit in hits)
        {
            if (hit.CompareTag("Cell"))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetAnchoringBounds(Transform targetTransform, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        Collider[] colliders = targetTransform.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || collider is WheelCollider)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private List<Rigidbody> GetConnectedBuildBodies(Rigidbody source, IReadOnlyCollection<Rigidbody> allBodies)
    {
        return BuildVehicleUtility.GetConnectedBodies(source, allBodies);
    }

    private void SetWheelScriptsEnabled(bool enabled, HashSet<Rigidbody> activeBodies)
    {
        GameObject[] wheels = GameObject.FindGameObjectsWithTag("wheel");
        foreach (GameObject wheel in wheels)
        {
            Rigidbody body = wheel.GetComponent<Rigidbody>();
            bool matchesFilter = activeBodies == null || (body != null && activeBodies.Contains(body));
            bool shouldEnable = enabled && matchesFilter;

            FrontWheele frontWheel = wheel.GetComponent<FrontWheele>();
            if (frontWheel != null)
            {
                if (!shouldEnable)
                {
                    frontWheel.SetWheelColliderEnabled(false);
                }

                frontWheel.enabled = shouldEnable;

                if (shouldEnable)
                {
                    frontWheel.AutoSetupForSimulation();
                }
            }

            BackWheele backWheel = wheel.GetComponent<BackWheele>();
            if (backWheel != null)
            {
                if (!shouldEnable)
                {
                    backWheel.SetWheelColliderEnabled(false);
                }

                backWheel.enabled = shouldEnable;

                if (shouldEnable)
                {
                    backWheel.AutoSetupForSimulation();
                }
            }
        }
    }

    private void SetWheelRootCollidersEnabled(bool enabled, HashSet<Rigidbody> activeBodies)
    {
        GameObject[] wheels = GameObject.FindGameObjectsWithTag("wheel");
        foreach (GameObject wheel in wheels)
        {
            Rigidbody body = wheel.GetComponent<Rigidbody>();
            bool shouldToggle = activeBodies == null || (body != null && activeBodies.Contains(body));
            bool isSimulationWheel = wheel.GetComponentInParent<ModularVehicleController>(true) != null;

            if (!shouldToggle)
            {
                continue;
            }

            Collider[] wheelColliders = wheel.GetComponentsInChildren<Collider>(true);
            foreach (Collider wheelCollider in wheelColliders)
            {
                if (wheelCollider is WheelCollider)
                {
                    // Simulation wheels use the detached RuntimeWheelCollider that
                    // lives directly under the vehicle root. The prefab's nested
                    // WheelCollider must stay off or the wheel gets simulated twice.
                    wheelCollider.enabled = enabled && !isSimulationWheel;
                    continue;
                }

                wheelCollider.enabled = enabled;
            }
        }
    }

    private void ResetScene()
    {
        if (IsNetworkSessionActive())
        {
            Debug.Log("Network oturumunda lokal reset kapali.");
            return;
        }

        BuildModeActive = true;
        Time.timeScale = 1f;

        if (RestoreBuildSnapshot())
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.buildIndex >= 0)
        {
            SceneManager.LoadScene(activeScene.buildIndex);
            return;
        }

        SceneManager.LoadScene(activeScene.name);
    }

    private bool IsBuildPartTag(string tagName)
    {
        return tagName == "Cube" || tagName == "Parts" || tagName == "wheel";
    }

    private void BuildQuickVehiclePresetLocal()
    {
        if (!IsBuildMode)
        {
            return;
        }

        if (objectGrabber != null && objectGrabber.IsCarrying)
        {
            Debug.Log("Hazir arac kurmadan once eldeki objeyi birak.");
            return;
        }

        if (!TryGetQuickBuildAnchorCell(out Vector3 anchorCell))
        {
            Debug.LogWarning("Hazir arac icin uygun bir cell bulunamadi.");
            return;
        }

        GameObject cubePrefab = ResolveQuickBuildPrefab(quickBuildCubePrefab, "Assets/Script/prefeb/Cube.prefab");
        GameObject seatPrefab = ResolveQuickBuildPrefab(quickBuildSeatPrefab, "Assets/Script/prefeb/Seat.prefab");
        GameObject frontWheelPrefab = ResolveQuickBuildPrefab(quickBuildFrontWheelPrefab, "Assets/Script/prefeb/wheelpushFront.prefab");
        GameObject rearWheelPrefab = ResolveQuickBuildPrefab(quickBuildRearWheelPrefab, "Assets/Script/prefeb/wheelpushBack.prefab");

        if (cubePrefab == null || seatPrefab == null || frontWheelPrefab == null || rearWheelPrefab == null)
        {
            Debug.LogWarning("Hazir arac prefab baglantilari eksik.");
            return;
        }

        Vector3 snappedForward = GetSnappedCardinalDirection(playerCamera != null ? playerCamera.transform.forward : transform.forward);
        Vector3 snappedRight = Vector3.Cross(Vector3.up, snappedForward).normalized;
        Quaternion bodyRotation = Quaternion.LookRotation(snappedForward, Vector3.up);
        Quaternion wheelRotation = bodyRotation;

        List<QuickBuildPlacement> placements = new List<QuickBuildPlacement>
        {
            new QuickBuildPlacement { Prefab = seatPrefab, Position = anchorCell, Rotation = bodyRotation, Label = "Seat" },
            new QuickBuildPlacement { Prefab = cubePrefab, Position = anchorCell + snappedForward, Rotation = bodyRotation, Label = "FrontCube" },
            new QuickBuildPlacement { Prefab = cubePrefab, Position = anchorCell - snappedForward, Rotation = bodyRotation, Label = "BackCube" },
            new QuickBuildPlacement { Prefab = frontWheelPrefab, Position = anchorCell + snappedForward + snappedRight, Rotation = wheelRotation, Label = "FrontRightWheel" },
            new QuickBuildPlacement { Prefab = frontWheelPrefab, Position = anchorCell + snappedForward - snappedRight, Rotation = wheelRotation, Label = "FrontLeftWheel" },
            new QuickBuildPlacement { Prefab = rearWheelPrefab, Position = anchorCell - snappedForward + snappedRight, Rotation = wheelRotation, Label = "RearRightWheel" },
            new QuickBuildPlacement { Prefab = rearWheelPrefab, Position = anchorCell - snappedForward - snappedRight, Rotation = wheelRotation, Label = "RearLeftWheel" }
        };

        foreach (QuickBuildPlacement placement in placements)
        {
            if (IsBuildCellOccupied(placement.Position))
            {
                Debug.LogWarning($"Hazir arac kurulumu iptal edildi. Dolu hucre: {placement.Label} @ {placement.Position}");
                return;
            }
        }

        foreach (QuickBuildPlacement placement in placements)
        {
            GameObject instance = Instantiate(placement.Prefab, placement.Position, placement.Rotation);
            instance.name = placement.Prefab.name;

            Rigidbody rb = instance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.Sleep();
            }
        }

        Physics.SyncTransforms();
        FinalizePendingAttachments();
        objectGrabber?.SnapAllObjectsInsideBuildArea();
        Debug.Log("Hazir mini arac semasi olusturuldu.");
    }

    private bool TryGetQuickBuildAnchorCell(out Vector3 anchorCell)
    {
        anchorCell = Vector3.zero;
        Ray ray = playerCamera != null
            ? playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
            : new Ray(transform.position + Vector3.up, transform.forward);

        RaycastHit[] hits = Physics.RaycastAll(ray, maxRaycastDistance);
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || !hit.collider.CompareTag("Cell"))
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                anchorCell = hit.collider.transform.position;
            }
        }

        if (!float.IsPositiveInfinity(nearestDistance))
        {
            return true;
        }

        Collider[] nearbyCells = Physics.OverlapSphere(transform.position + transform.forward * 2f, 3f, ~0, QueryTriggerInteraction.Collide);
        float nearestCellDistance = float.PositiveInfinity;

        foreach (Collider collider in nearbyCells)
        {
            if (collider == null || !collider.CompareTag("Cell"))
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, collider.transform.position);
            if (distance < nearestCellDistance)
            {
                nearestCellDistance = distance;
                anchorCell = collider.transform.position;
            }
        }

        return !float.IsPositiveInfinity(nearestCellDistance);
    }

    private Vector3 GetSnappedCardinalDirection(Vector3 sourceDirection)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(sourceDirection, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.forward;
        }

        planarDirection.Normalize();
        float forwardDot = Vector3.Dot(planarDirection, Vector3.forward);
        float backDot = Vector3.Dot(planarDirection, Vector3.back);
        float rightDot = Vector3.Dot(planarDirection, Vector3.right);
        float leftDot = Vector3.Dot(planarDirection, Vector3.left);

        float bestDot = forwardDot;
        Vector3 bestDirection = Vector3.forward;

        if (backDot > bestDot)
        {
            bestDot = backDot;
            bestDirection = Vector3.back;
        }

        if (rightDot > bestDot)
        {
            bestDot = rightDot;
            bestDirection = Vector3.right;
        }

        if (leftDot > bestDot)
        {
            bestDirection = Vector3.left;
        }

        return bestDirection;
    }

    private bool IsBuildCellOccupied(Vector3 cellPosition)
    {
        HashSet<Rigidbody> buildBodies = BuildVehicleUtility.GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null)
            {
                continue;
            }

            if (Vector3.Distance(body.transform.position, cellPosition) <= 0.1f)
            {
                return true;
            }
        }

        return false;
    }

    private GameObject ResolveQuickBuildPrefab(GameObject assignedPrefab, string editorAssetPath)
    {
        if (assignedPrefab != null)
        {
            return assignedPrefab;
        }

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<GameObject>(editorAssetPath);
#else
        return null;
#endif
    }

    private void HandleMovement()
    {
        if (isDebugFlyMode)
        {
            HandleDebugFlyMovement();
            return;
        }

        if (ShouldRedirectMovementToSeatlessVehicle())
        {
            verticalVelocity = GroundedVerticalVelocity;
            return;
        }

        if (IsSeated)
        {
            return;
        }

        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        Vector3 horizontalMove = (transform.right * moveX + transform.forward * moveZ) * moveSpeed;

        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = GroundedVerticalVelocity;
        }

        if (Input.GetKeyDown(KeyCode.Space) && characterController.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontalMove + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void HandleDebugFlyMovement()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        float verticalInput = 0f;

        if (Input.GetKey(KeyCode.Space))
        {
            verticalInput += 1f;
        }

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            verticalInput -= 1f;
        }

        Vector3 horizontalDirection = transform.right * moveX + transform.forward * moveZ;
        if (horizontalDirection.sqrMagnitude > 1f)
        {
            horizontalDirection.Normalize();
        }

        float climbSpeed = debugFlyVerticalSpeed > 0f ? debugFlyVerticalSpeed : debugFlyMoveSpeed;
        Vector3 velocity = horizontalDirection * debugFlyMoveSpeed + Vector3.up * (verticalInput * climbSpeed);

        if (characterController != null && characterController.enabled)
        {
            characterController.Move(velocity * Time.deltaTime);
        }
        else
        {
            transform.position += velocity * Time.deltaTime;
        }

        verticalVelocity = GroundedVerticalVelocity;
    }

    private bool ShouldRedirectMovementToSeatlessVehicle()
    {
        if (IsBuildMode || IsSeated)
        {
            return false;
        }

        ModularVehicleController[] vehicleControllers = FindObjectsByType<ModularVehicleController>(FindObjectsSortMode.None);
        foreach (ModularVehicleController vehicleController in vehicleControllers)
        {
            if (vehicleController != null && vehicleController.CanBeControlledWithoutSeatBy(this))
            {
                return true;
            }
        }

        return false;
    }

    private void ToggleSeatInteraction()
    {
        if (IsSeated)
        {
            ExitCurrentSeat();
            return;
        }

        VehicleSeat closestSeat = FindClosestAvailableSeat();
        if (closestSeat == null)
        {
            return;
        }

        EnterSeat(closestSeat);
    }

    private VehicleSeat FindClosestAvailableSeat()
    {
        VehicleSeat[] seats = FindObjectsByType<VehicleSeat>(FindObjectsSortMode.None);
        VehicleSeat closestSeat = null;
        float closestDistance = float.MaxValue;

        foreach (VehicleSeat seat in seats)
        {
            if (seat == null || !seat.CanBeEnteredBy(this))
            {
                continue;
            }

            float allowedDistance = Mathf.Max(seatInteractionDistance, seat.InteractionRadius);
            float distance = Vector3.Distance(transform.position, seat.GetSeatWorldPosition());
            if (distance > allowedDistance || distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestSeat = seat;
        }

        return closestSeat;
    }

    private void EnterSeat(VehicleSeat seat)
    {
        if (seat == null)
        {
            return;
        }

        SetDebugFlyMode(false);
        currentSeat = seat;
        currentSeat.EnterSeat(this);
        verticalVelocity = GroundedVerticalVelocity;

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        seatedYawRotation = 0f;
        currentSeat.AttachPlayer(transform);
        UpdateSeatedTransform();
        BeginSeatedVehicleTestCamera(currentSeat);
    }

    private void ExitCurrentSeat()
    {
        if (currentSeat == null)
        {
            EndSeatedVehicleTestCamera();
            DetachFromVehicleHierarchy();

            if (characterController != null)
            {
                characterController.enabled = true;
            }

            return;
        }

        VehicleSeat seatToLeave = currentSeat;
        currentSeat = null;
        seatToLeave.ExitSeat(this);
        EndSeatedVehicleTestCamera();

        DetachFromVehicleHierarchy();

        Vector3 exitPosition = seatToLeave.GetExitWorldPosition();
        Quaternion uprightRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (characterController != null)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(exitPosition, uprightRotation);
            characterController.enabled = true;
        }
        else
        {
            transform.SetPositionAndRotation(exitPosition, uprightRotation);
        }

        verticalVelocity = GroundedVerticalVelocity;
    }

    private void HandleLook()
    {
        if (playerCamera == null)
        {
            return;
        }

        if (seatedTestCameraActive)
        {
            return;
        }

        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        verticalLookRotation = Mathf.Clamp(verticalLookRotation - mouseY, -90f, 90f);

        if (IsSeated)
        {
            seatedYawRotation += mouseX;
        }
        else
        {
            transform.Rotate(Vector3.up * mouseX);
        }

        playerCamera.transform.localRotation = Quaternion.Euler(verticalLookRotation, 0f, 0f);
    }

    private void ToggleDebugFlyMode()
    {
        if (IsBuildMode || IsSeated)
        {
            SetDebugFlyMode(false);
            return;
        }

        SetDebugFlyMode(!isDebugFlyMode);
    }

    private void SetDebugFlyMode(bool enabled)
    {
        if (isDebugFlyMode == enabled)
        {
            return;
        }

        isDebugFlyMode = enabled;
        verticalVelocity = GroundedVerticalVelocity;
    }

    private void PrepareVehicleBodiesForSimulation(HashSet<Rigidbody> vehicleBodies)
    {
        foreach (Rigidbody body in vehicleBodies)
        {
            bool isWheel = body.CompareTag("wheel");

            body.isKinematic = true;
            body.interpolation = isWheel ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = isWheel
                ? CollisionDetectionMode.Discrete
                : CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = isWheel
                ? simulationWheelLinearDamping
                : Mathf.Max(body.linearDamping, simulationLinearDamping);
            body.angularDamping = isWheel
                ? simulationWheelAngularDamping
                : Mathf.Max(body.angularDamping, simulationAngularDrag);
            body.solverIterations = Mathf.Max(body.solverIterations, simulationSolverIterations);
            body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, simulationSolverVelocityIterations);
            body.sleepThreshold = Mathf.Max(body.sleepThreshold, simulationSleepThreshold);
            body.maxDepenetrationVelocity = Mathf.Min(body.maxDepenetrationVelocity, simulationMaxDepenetrationVelocity);

            if (releaseWheelRotationConstraintsInSimulation && body.CompareTag("wheel"))
            {
                body.constraints = RigidbodyConstraints.None;
            }

            FixedJoint[] joints = body.GetComponents<FixedJoint>();
            foreach (FixedJoint joint in joints)
            {
                joint.enableCollision = false;
                joint.enablePreprocessing = true;
            }
        }
    }

    private void SetDynamicCenterOfMassEnabled(bool enabled, HashSet<Rigidbody> activeBodies)
    {
        List<Rigidbody> buildBodies = GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            DynamicCenterOfMass dynamicCenter = body.GetComponent<DynamicCenterOfMass>();
            if (dynamicCenter == null)
            {
                continue;
            }

            bool shouldEnable = enabled && activeBodies != null && activeBodies.Contains(body);
            dynamicCenter.enabled = shouldEnable;

            if (shouldEnable)
            {
                dynamicCenter.RefreshNow();
            }
        }
    }

    private void SetUprightStabilizersEnabled(bool enabled, HashSet<Rigidbody> activeBodies)
    {
        List<Rigidbody> buildBodies = GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            VehicleUprightStabilizer stabilizer = body.GetComponent<VehicleUprightStabilizer>();
            if (stabilizer == null && body.gameObject.tag != "wheel")
            {
                stabilizer = body.gameObject.AddComponent<VehicleUprightStabilizer>();
            }

            if (stabilizer == null)
            {
                continue;
            }

            bool shouldEnable = enabled &&
                                activeBodies != null &&
                                activeBodies.Contains(body) &&
                                body.gameObject.tag != "wheel";

            stabilizer.uprightStrength = uprightAssistStrength;
            stabilizer.angularDamping = uprightAssistDamping;
            stabilizer.maxTorque = uprightAssistMaxTorque;
            stabilizer.enabled = shouldEnable;
        }
    }

    private void HideUnusedBuildParts(HashSet<Rigidbody> activeBodies)
    {
        hiddenBuildParts.Clear();

        List<Rigidbody> buildBodies = GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null || activeBodies.Contains(body))
            {
                continue;
            }

            hiddenBuildParts.Add(body.gameObject);
            body.gameObject.SetActive(false);
        }
    }

    private void SetHiddenBuildPartsActive(bool active)
    {
        foreach (GameObject hiddenPart in hiddenBuildParts)
        {
            if (hiddenPart != null)
            {
                hiddenPart.SetActive(active);
            }
        }

        if (active)
        {
            hiddenBuildParts.Clear();
        }
    }

    private void EnsureWheelIndicatorsExist()
    {
        return;
    }

    private void CaptureBuildSnapshot()
    {
        buildSnapshot.Clear();

        List<Rigidbody> buildBodies = GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null)
            {
                continue;
            }

            buildSnapshot.Add(new BuildPartSnapshot
            {
                Body = body,
                Position = body.transform.position,
                Rotation = body.transform.rotation,
                WasActive = body.gameObject.activeSelf
            });
        }

        hasBuildSnapshot = buildSnapshot.Count > 0;
        CapturePlayerSnapshot();
    }

    private void CapturePlayerSnapshot()
    {
        buildPlayerSnapshot = new PlayerSnapshot
        {
            Position = transform.position,
            Rotation = transform.rotation,
            VerticalLookRotation = verticalLookRotation
        };

        hasBuildPlayerSnapshot = true;
    }

    private bool RestoreBuildSnapshot()
    {
        if (!hasBuildSnapshot || buildSnapshot.Count == 0)
        {
            return false;
        }

        if (objectGrabber != null)
        {
            objectGrabber.ReleaseHeldObjectForSimulation();
        }

        ExitCurrentSeat();
        DetachFromVehicleHierarchy();

        DestroySimulationVehicles();
        SetWheelScriptsEnabled(false, null);
        SetWheelRootCollidersEnabled(true, null);
        SetDynamicCenterOfMassEnabled(false, null);
        SetUprightStabilizersEnabled(false, null);
        SetHiddenBuildPartsActive(true);

        foreach (BuildPartSnapshot snapshot in buildSnapshot)
        {
            if (snapshot.Body == null)
            {
                continue;
            }

            GameObject bodyObject = snapshot.Body.gameObject;
            bodyObject.SetActive(true);

            if (!snapshot.Body.isKinematic)
            {
                snapshot.Body.linearVelocity = Vector3.zero;
                snapshot.Body.angularVelocity = Vector3.zero;
            }

            snapshot.Body.isKinematic = true;
            snapshot.Body.transform.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
            bodyObject.SetActive(snapshot.WasActive);
        }

        Physics.SyncTransforms();
        EnterBuildMode();
        RestorePlayerSnapshot();
        return true;
    }

    private void RestorePlayerSnapshot()
    {
        if (!hasBuildPlayerSnapshot)
        {
            return;
        }

        DetachFromVehicleHierarchy();

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(buildPlayerSnapshot.Position, buildPlayerSnapshot.Rotation);

        verticalLookRotation = buildPlayerSnapshot.VerticalLookRotation;
        if (playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(verticalLookRotation, 0f, 0f);
        }

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        verticalVelocity = GroundedVerticalVelocity;
    }

    private void DetachFromVehicleHierarchy()
    {
        if (transform.parent != null)
        {
            transform.SetParent(null, true);
        }
    }

    private void UpdateSeatedTransform()
    {
        if (currentSeat == null)
        {
            return;
        }

        Quaternion seatRotation = currentSeat.GetSeatWorldRotation();
        Quaternion playerRotation = seatRotation * Quaternion.Euler(0f, seatedYawRotation, 0f);
        transform.SetPositionAndRotation(currentSeat.GetSeatWorldPosition(), playerRotation);
    }

    private void BeginSeatedVehicleTestCamera(VehicleSeat seat)
    {
        EndSeatedVehicleTestCamera();

        if (!useSeatedVehicleTestCamera || !localInputEnabled || playerCamera == null || !playerCamera.enabled || seat == null)
        {
            return;
        }

        ModularVehicleController vehicleController = seat.VehicleController;
        if (vehicleController == null)
        {
            return;
        }

        seatedTestCameraVehicleRoot = vehicleController.transform;
        seatedTestCameraOriginalParent = playerCamera.transform.parent;
        seatedTestCameraOriginalLocalPosition = playerCamera.transform.localPosition;
        seatedTestCameraOriginalLocalRotation = playerCamera.transform.localRotation;

        Bounds vehicleBounds;
        if (!TryGetVehicleCameraBounds(seatedTestCameraVehicleRoot, out vehicleBounds))
        {
            vehicleBounds = new Bounds(seatedTestCameraVehicleRoot.position, Vector3.one * 2f);
        }

        seatedTestCameraTargetLocalPosition = seatedTestCameraVehicleRoot.InverseTransformPoint(vehicleBounds.center);

        Vector3 initialForward = Vector3.ProjectOnPlane(seat.GetSeatWorldRotation() * Vector3.forward, Vector3.up);
        if (initialForward.sqrMagnitude < 0.001f)
        {
            initialForward = Vector3.ProjectOnPlane(seatedTestCameraVehicleRoot.forward, Vector3.up);
        }

        if (initialForward.sqrMagnitude < 0.001f)
        {
            initialForward = Vector3.forward;
        }

        initialForward.Normalize();
        seatedTestCameraFollowForward = initialForward;
        seatedTestCameraFollowDistance = CalculateSeatedCameraFramingDistance(vehicleBounds);

        playerCamera.transform.SetParent(null, true);
        seatedTestCameraActive = true;
        SetSeatedVehicleTestCameraPose(true);
    }

    private void UpdateSeatedVehicleTestCamera()
    {
        if (!seatedTestCameraActive || currentSeat == null || playerCamera == null || seatedTestCameraVehicleRoot == null)
        {
            return;
        }

        SetSeatedVehicleTestCameraPose(false);
    }

    private void SetSeatedVehicleTestCameraPose(bool snapImmediately)
    {
        Vector3 desiredForward = Vector3.ProjectOnPlane(
            currentSeat.GetSeatWorldRotation() * Vector3.forward,
            Vector3.up);
        if (desiredForward.sqrMagnitude > 0.001f)
        {
            desiredForward.Normalize();
            float directionBlend = snapImmediately
                ? 1f
                : 1f - Mathf.Exp(-seatedTestCameraRotationSharpness * Time.deltaTime);
            seatedTestCameraFollowForward = Vector3.Slerp(
                seatedTestCameraFollowForward,
                desiredForward,
                directionBlend).normalized;
        }

        Vector3 targetPosition = seatedTestCameraVehicleRoot.TransformPoint(seatedTestCameraTargetLocalPosition);
        Vector3 desiredCameraPosition = targetPosition -
                                        seatedTestCameraFollowForward * seatedTestCameraFollowDistance +
                                        Vector3.up * seatedTestCameraHeight;
        float positionBlend = snapImmediately
            ? 1f
            : 1f - Mathf.Exp(-seatedTestCameraPositionSharpness * Time.deltaTime);
        playerCamera.transform.position = Vector3.Lerp(
            playerCamera.transform.position,
            desiredCameraPosition,
            positionBlend);

        Vector3 lookDirection = targetPosition - playerCamera.transform.position;
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            float rotationBlend = snapImmediately
                ? 1f
                : 1f - Mathf.Exp(-seatedTestCameraRotationSharpness * Time.deltaTime);
            playerCamera.transform.rotation = Quaternion.Slerp(
                playerCamera.transform.rotation,
                desiredRotation,
                rotationBlend);
        }
    }

    private void EndSeatedVehicleTestCamera()
    {
        if (!seatedTestCameraActive)
        {
            return;
        }

        if (playerCamera != null)
        {
            playerCamera.transform.SetParent(seatedTestCameraOriginalParent, false);
            playerCamera.transform.localPosition = seatedTestCameraOriginalLocalPosition;
            playerCamera.transform.localRotation = seatedTestCameraOriginalLocalRotation;
        }

        seatedTestCameraActive = false;
        seatedTestCameraOriginalParent = null;
        seatedTestCameraVehicleRoot = null;
        seatedTestCameraFollowForward = Vector3.zero;
        seatedTestCameraFollowDistance = 0f;
    }

    private float CalculateSeatedCameraFramingDistance(Bounds vehicleBounds)
    {
        float minimumDistance = Mathf.Max(2f, seatedTestCameraDistance);
        if (playerCamera == null)
        {
            return minimumDistance;
        }

        float halfVerticalFov = Mathf.Max(1f, playerCamera.fieldOfView * 0.5f) * Mathf.Deg2Rad;
        float halfHorizontalFov = Mathf.Atan(Mathf.Tan(halfVerticalFov) * Mathf.Max(0.1f, playerCamera.aspect));
        float limitingHalfFov = Mathf.Max(0.05f, Mathf.Min(halfVerticalFov, halfHorizontalFov));
        float boundsRadius = Mathf.Max(1f, vehicleBounds.extents.magnitude);
        float fittedDistance = boundsRadius / Mathf.Tan(limitingHalfFov);
        return Mathf.Max(minimumDistance, fittedDistance * seatedTestCameraFramePadding);
    }

    private static bool TryGetVehicleCameraBounds(Transform vehicleRoot, out Bounds bounds)
    {
        bounds = default;
        if (vehicleRoot == null)
        {
            return false;
        }

        bool hasBounds = false;
        Renderer[] renderers = vehicleRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer vehicleRenderer in renderers)
        {
            if (vehicleRenderer == null || !vehicleRenderer.enabled || !vehicleRenderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = vehicleRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(vehicleRenderer.bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        Collider[] colliders = vehicleRoot.GetComponentsInChildren<Collider>(true);
        foreach (Collider vehicleCollider in colliders)
        {
            if (vehicleCollider == null || !vehicleCollider.enabled || !vehicleCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = vehicleCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(vehicleCollider.bounds);
            }
        }

        return hasBounds;
    }

    private void InitializeLocalSessionStateIfNeeded()
    {
        if (hasInitializedLocalSessionState || ShouldDeferLocalPlayerInitialization())
        {
            return;
        }

        hasInitializedLocalSessionState = true;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        EnterBuildMode();
    }

    private bool ShouldDeferLocalPlayerInitialization()
    {
        NetworkObject networkObject = GetComponent<NetworkObject>();
        return networkObject != null &&
               NetworkManager.Singleton != null &&
               NetworkManager.Singleton.IsListening &&
               !networkObject.IsOwner;
    }

    private bool IsNetworkSessionActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private void DeactivateVehicleBuildParts(HashSet<Rigidbody> activeBodies)
    {
        foreach (Rigidbody body in activeBodies)
        {
            if (body == null)
            {
                continue;
            }

            body.gameObject.SetActive(false);
        }
    }

    private void CreateSimulationVehicles(HashSet<Rigidbody> vehicleBodies)
    {
        List<HashSet<Rigidbody>> vehicleGroups = SplitVehicleGroups(vehicleBodies);

        foreach (HashSet<Rigidbody> vehicleGroup in vehicleGroups)
        {
            if (vehicleGroup.Count == 0)
            {
                continue;
            }

            Rigidbody primaryBody = GetPrimaryBody(vehicleGroup);
            if (primaryBody == null)
            {
                continue;
            }

            GameObject simulationRoot = new GameObject($"{primaryBody.gameObject.name}_SimulationRoot");
            simulationRoot.transform.SetPositionAndRotation(primaryBody.transform.position, Quaternion.identity);

            foreach (Rigidbody body in vehicleGroup)
            {
                if (body == null)
                {
                    continue;
                }

                GameObject clone = Instantiate(body.gameObject, body.transform.position, body.transform.rotation, simulationRoot.transform);
                clone.name = body.gameObject.name;
                StripSimulationClone(clone);
                clone.SetActive(true);
            }

            Rigidbody rootBody = simulationRoot.AddComponent<Rigidbody>();
            ConfigureSimulationRootBody(rootBody, vehicleGroup);
            if (useSimulationUprightAssist)
            {
                AddSimulationRootStabilizer(simulationRoot);
            }
            ModularVehicleController vehicleController = simulationRoot.AddComponent<ModularVehicleController>();
            vehicleController.AssembleForSimulation();
            simulationVehicleRoots.Add(simulationRoot);
        }
    }

    private List<HashSet<Rigidbody>> SplitVehicleGroups(HashSet<Rigidbody> vehicleBodies)
    {
        List<HashSet<Rigidbody>> groups = new List<HashSet<Rigidbody>>();
        HashSet<Rigidbody> remaining = new HashSet<Rigidbody>(vehicleBodies);

        while (remaining.Count > 0)
        {
            Rigidbody seed = null;
            foreach (Rigidbody body in remaining)
            {
                seed = body;
                break;
            }

            if (seed == null)
            {
                break;
            }

            HashSet<Rigidbody> connectedGroup = BuildVehicleUtility.CollectConnectedBodies(seed);
            connectedGroup.IntersectWith(vehicleBodies);

            if (connectedGroup.Count == 0)
            {
                connectedGroup.Add(seed);
            }

            groups.Add(connectedGroup);
            remaining.ExceptWith(connectedGroup);
        }

        MergeBalloonTopBodiesIntoSupportGroups(groups);
        groups.RemoveAll(group => group == null || group.Count == 0);
        return groups;
    }

    private void MergeBalloonTopBodiesIntoSupportGroups(List<HashSet<Rigidbody>> groups)
    {
        if (groups == null || groups.Count == 0)
        {
            return;
        }

        List<Transform> cubeRoots = new List<Transform>();
        Dictionary<Transform, HashSet<Rigidbody>> cubeRootToGroup = new Dictionary<Transform, HashSet<Rigidbody>>();

        foreach (HashSet<Rigidbody> group in groups)
        {
            foreach (Rigidbody body in group)
            {
                if (body == null || !body.CompareTag("Cube"))
                {
                    continue;
                }

                cubeRoots.Add(body.transform);
                cubeRootToGroup[body.transform] = group;
            }
        }

        if (cubeRoots.Count == 0)
        {
            return;
        }

        List<(Rigidbody body, HashSet<Rigidbody> sourceGroup, HashSet<Rigidbody> targetGroup)> pendingMoves =
            new List<(Rigidbody body, HashSet<Rigidbody> sourceGroup, HashSet<Rigidbody> targetGroup)>();

        foreach (HashSet<Rigidbody> group in groups)
        {
            foreach (Rigidbody body in group)
            {
                if (body == null || body.GetComponent<BalloonTopRise>() == null)
                {
                    continue;
                }

                bool foundSupport = BalloonTopRise.TryFindSupportCubeBelow(
                    body.transform,
                    cubeRoots,
                    BalloonTopRise.DefaultSupportSearchPadding,
                    out Transform supportCube,
                    out _);

                if (!foundSupport ||
                    supportCube == null ||
                    !cubeRootToGroup.TryGetValue(supportCube, out HashSet<Rigidbody> targetGroup) ||
                    targetGroup == null ||
                    targetGroup == group)
                {
                    continue;
                }

                pendingMoves.Add((body, group, targetGroup));
            }
        }

        foreach ((Rigidbody body, HashSet<Rigidbody> sourceGroup, HashSet<Rigidbody> targetGroup) move in pendingMoves)
        {
            move.sourceGroup.Remove(move.body);
            move.targetGroup.Add(move.body);
        }
    }

    private Rigidbody GetPrimaryBody(HashSet<Rigidbody> vehicleGroup)
    {
        return BuildVehicleUtility.GetPreferredPrimaryBody(vehicleGroup);
    }

    private void StripSimulationClone(GameObject cloneRoot)
    {
        if (cloneRoot == null)
        {
            return;
        }

        FixedJoint[] cloneJoints = cloneRoot.GetComponentsInChildren<FixedJoint>(true);
        foreach (FixedJoint joint in cloneJoints)
        {
            DestroyImmediate(joint);
        }

        ReleasedBuildSnapHandler[] snapHandlers = cloneRoot.GetComponentsInChildren<ReleasedBuildSnapHandler>(true);
        foreach (ReleasedBuildSnapHandler snapHandler in snapHandlers)
        {
            DestroyImmediate(snapHandler);
        }

        Rigidbody[] cloneBodies = cloneRoot.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in cloneBodies)
        {
            DestroyImmediate(body);
        }

        Cube[] cubeScripts = cloneRoot.GetComponentsInChildren<Cube>(true);
        foreach (Cube cubeScript in cubeScripts)
        {
            DestroyImmediate(cubeScript);
        }

        Parts[] partScripts = cloneRoot.GetComponentsInChildren<Parts>(true);
        foreach (Parts partScript in partScripts)
        {
            DestroyImmediate(partScript);
        }

        AttachPoint[] attachPoints = cloneRoot.GetComponentsInChildren<AttachPoint>(true);
        foreach (AttachPoint attachPoint in attachPoints)
        {
            DestroyImmediate(attachPoint);
        }

        AttachPointParts[] attachPointParts = cloneRoot.GetComponentsInChildren<AttachPointParts>(true);
        foreach (AttachPointParts partAttachPoint in attachPointParts)
        {
            DestroyImmediate(partAttachPoint);
        }

        DynamicCenterOfMass[] dynamicCenters = cloneRoot.GetComponentsInChildren<DynamicCenterOfMass>(true);
        foreach (DynamicCenterOfMass dynamicCenter in dynamicCenters)
        {
            DestroyImmediate(dynamicCenter);
        }

        VehicleUprightStabilizer[] stabilizers = cloneRoot.GetComponentsInChildren<VehicleUprightStabilizer>(true);
        foreach (VehicleUprightStabilizer stabilizer in stabilizers)
        {
            DestroyImmediate(stabilizer);
        }

        WheelBuildDirectionIndicator[] wheelIndicators = cloneRoot.GetComponentsInChildren<WheelBuildDirectionIndicator>(true);
        foreach (WheelBuildDirectionIndicator indicator in wheelIndicators)
        {
            DestroyImmediate(indicator);
        }

        Transform[] childTransforms = cloneRoot.GetComponentsInChildren<Transform>(true);
        foreach (Transform childTransform in childTransforms)
        {
            if (childTransform != null && childTransform.name == "WheelDirectionArrow")
            {
                DestroyImmediate(childTransform.gameObject);
            }
        }

        Collider[] cloneColliders = cloneRoot.GetComponentsInChildren<Collider>(true);
        foreach (Collider cloneCollider in cloneColliders)
        {
            if (cloneCollider != null && cloneCollider.isTrigger)
            {
                cloneCollider.enabled = false;
            }
        }

        bool isWheelClone = cloneRoot.GetComponent<FrontWheele>() != null ||
                            cloneRoot.GetComponent<BackWheele>() != null ||
                            cloneRoot.GetComponent<AircraftLandingGear>() != null;

        if (isWheelClone)
        {
            foreach (Collider cloneCollider in cloneColliders)
            {
                if (cloneCollider == null || cloneCollider is WheelCollider)
                {
                    continue;
                }

                DestroyImmediate(cloneCollider);
            }

            return;
        }

        EnsureCompoundPartCollider(cloneRoot);
    }

    // Only parts that shipped without a physical collider get a small fallback.
    // Existing child colliders remain untouched and form the compound Rigidbody body.
    private void EnsureCompoundPartCollider(GameObject cloneRoot)
    {
        foreach (Collider colliderComponent in cloneRoot.GetComponentsInChildren<Collider>(true))
        {
            if (colliderComponent != null &&
                !colliderComponent.isTrigger &&
                !(colliderComponent is WheelCollider))
            {
                colliderComponent.enabled = true;
                return;
            }
        }

        Renderer[] renderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds localBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            EncapsulateRendererBoundsInLocalSpace(cloneRoot.transform, renderer.bounds, ref localBounds, ref hasBounds);
        }

        if (!hasBounds)
        {
            return;
        }

        Transform simulationRoot = cloneRoot.transform.parent;
        if (simulationRoot == null)
        {
            return;
        }

        // Some imported models are mirrored with a negative scale. A BoxCollider
        // on that transform is invalid, so keep the fallback as an identity-scale
        // sibling under the simulation root instead.
        GameObject fallbackObject = new GameObject($"{cloneRoot.name}_FallbackCollider");
        fallbackObject.layer = cloneRoot.layer;
        Transform fallbackTransform = fallbackObject.transform;
        fallbackTransform.SetParent(simulationRoot, false);
        fallbackTransform.localPosition = Vector3.zero;
        fallbackTransform.localRotation = Quaternion.identity;
        fallbackTransform.localScale = Vector3.one;

        localBounds = default;
        hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null)
            {
                EncapsulateRendererBoundsInLocalSpace(simulationRoot, renderer.bounds, ref localBounds, ref hasBounds);
            }
        }

        if (!hasBounds)
        {
            Destroy(fallbackObject);
            return;
        }

        BoxCollider fallbackCollider = fallbackObject.AddComponent<BoxCollider>();
        fallbackCollider.center = localBounds.center;
        fallbackCollider.size = new Vector3(
            Mathf.Max(0.05f, localBounds.size.x),
            Mathf.Max(0.05f, localBounds.size.y),
            Mathf.Max(0.05f, localBounds.size.z));
    }

    private static void EncapsulateRendererBoundsInLocalSpace(
        Transform root,
        Bounds worldBounds,
        ref Bounds localBounds,
        ref bool hasBounds)
    {
        Vector3 center = worldBounds.center;
        Vector3 extents = worldBounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = root.InverseTransformPoint(corner);

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

    private void ConfigureSimulationRootBody(Rigidbody rootBody, HashSet<Rigidbody> sourceBodies)
    {
        if (rootBody == null)
        {
            return;
        }

        float totalMass = 0f;
        Vector3 weightedCenter = Vector3.zero;

        foreach (Rigidbody sourceBody in sourceBodies)
        {
            if (sourceBody == null)
            {
                continue;
            }

            float massContribution = GetSimulationMassContribution(sourceBody);
            Vector3 centerContribution = GetSimulationCenterOfMassContribution(sourceBody);

            totalMass += massContribution;
            weightedCenter += centerContribution * massContribution;
        }

        rootBody.mass = Mathf.Max(0.1f, totalMass);
        rootBody.useGravity = true;
        rootBody.isKinematic = false;
        rootBody.interpolation = RigidbodyInterpolation.Interpolate;
        rootBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rootBody.linearDamping = simulationLinearDamping;
        rootBody.angularDamping = simulationAngularDrag;
        rootBody.solverIterations = simulationSolverIterations;
        rootBody.solverVelocityIterations = simulationSolverVelocityIterations;
        rootBody.sleepThreshold = simulationSleepThreshold;
        rootBody.maxDepenetrationVelocity = simulationMaxDepenetrationVelocity;

        if (totalMass > 0.0001f)
        {
            weightedCenter /= totalMass;
            rootBody.centerOfMass = rootBody.transform.InverseTransformPoint(weightedCenter);
        }
    }

    private float GetSimulationMassContribution(Rigidbody sourceBody)
    {
        if (sourceBody == null)
        {
            return 0f;
        }

        if (!sourceBody.CompareTag("wheel"))
        {
            return sourceBody.mass;
        }

        WheelCollider wheelCollider = GetAssignedWheelCollider(sourceBody.gameObject);
        if (wheelCollider != null)
        {
            return Mathf.Max(0.01f, wheelCollider.mass);
        }

        return Mathf.Max(0.01f, sourceBody.mass);
    }

    private Vector3 GetSimulationCenterOfMassContribution(Rigidbody sourceBody)
    {
        if (sourceBody == null)
        {
            return Vector3.zero;
        }

        WheelCollider wheelCollider = sourceBody.CompareTag("wheel")
            ? GetAssignedWheelCollider(sourceBody.gameObject)
            : null;

        if (wheelCollider != null)
        {
            return wheelCollider.transform.TransformPoint(wheelCollider.center);
        }

        return sourceBody.worldCenterOfMass;
    }

    private WheelCollider GetAssignedWheelCollider(GameObject wheelObject)
    {
        if (wheelObject == null)
        {
            return null;
        }

        FrontWheele frontWheel = wheelObject.GetComponent<FrontWheele>();
        if (frontWheel != null && frontWheel.RuntimeWheelCollider != null)
        {
            return frontWheel.RuntimeWheelCollider;
        }

        BackWheele backWheel = wheelObject.GetComponent<BackWheele>();
        if (backWheel != null && backWheel.RuntimeWheelCollider != null)
        {
            return backWheel.RuntimeWheelCollider;
        }

        return wheelObject.GetComponentInChildren<WheelCollider>(true);
    }

    private void AddSimulationRootStabilizer(GameObject simulationRoot)
    {
        if (simulationRoot == null)
        {
            return;
        }

        VehicleUprightStabilizer stabilizer = simulationRoot.GetComponent<VehicleUprightStabilizer>();
        if (stabilizer == null)
        {
            stabilizer = simulationRoot.AddComponent<VehicleUprightStabilizer>();
        }

        stabilizer.uprightStrength = uprightAssistStrength;
        stabilizer.angularDamping = uprightAssistDamping;
        stabilizer.maxTorque = uprightAssistMaxTorque;
        stabilizer.enabled = true;
    }

    private void DestroySimulationVehicles()
    {
        foreach (GameObject simulationVehicleRoot in simulationVehicleRoots)
        {
            if (simulationVehicleRoot != null)
            {
                simulationVehicleRoot.SetActive(false);
                Destroy(simulationVehicleRoot);
            }
        }

        simulationVehicleRoots.Clear();
    }
}
