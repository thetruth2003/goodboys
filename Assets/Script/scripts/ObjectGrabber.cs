using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ObjectGrabber : MonoBehaviour
{
    public Camera playerCamera;
    public float maxGrabDistance = 100f;
    public float grabMoveSpeed = 10f;
    public float grabDistanceScrollSpeed = 1.5f;
    public float minHoldDistance = 1.5f;
    public float cellSearchRadius = 0.4f;
    public float occupiedCellThreshold = 0.1f;
    [Header("Release Throw")]
    public float throwVelocityThreshold = 1.25f;
    public float maxThrowVelocity = 16f;
    [Range(0.01f, 1f)] public float heldVelocitySmoothing = 0.2f;
    [Range(0.05f, 0.95f)] public float motorInsideCubePreviewAlpha = 0.35f;
    [Range(0.01f, 0.25f)] public float carryCollisionPadding = 0.05f;
    public float minimumCarryBoundsExtent = 0.05f;

    private GameObject grabbedObject = null;
    private float objectDistance;
    private PlayerController playerController;
    private Vector3 lastValidPosition;
    private bool hasLastValidPosition;
    private bool grabbedObjectStartedOutsideBuildArea;
    private Vector3 previousHeldPosition;
    private Vector3 estimatedHeldVelocity;
    private bool hasHeldMotionSample;
    private readonly HashSet<EntityId> freelyReleasedObjectIds = new HashSet<EntityId>();
    private readonly HashSet<GameObject> previewedContainerCubes = new HashSet<GameObject>();
    private readonly Dictionary<Renderer, Material[]> previewOriginalMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Material, Material> transparentMaterialCache = new Dictionary<Material, Material>();
    private const string FreePhysicsColliderName = "FreePhysicsCollider";

    public bool IsCarrying = false;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (playerController != null && !playerController.IsBuildMode)
        {
            if (previewedContainerCubes.Count > 0)
            {
                ClearMotorContainerPreview();
            }

            return;
        }

        if (Input.GetMouseButtonDown(0) && grabbedObject == null)
        {
            TryGrabObject();
        }

        if (Input.GetMouseButtonUp(0) && grabbedObject != null)
        {
            ReleaseObject();
        }

        if (grabbedObject != null)
        {
            AdjustHoldDistance();
            MoveGrabbedObject();
            UpdateHeldMotionEstimate();

            if (playerController != null)
            {
                playerController.NotifyBuildPartMoved(grabbedObject, grabbedObject.transform.position, grabbedObject.transform.rotation);
            }
        }
        else
        {
            RefreshMotorContainerTransparency(null);
        }
    }

    void TryGrabObject()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance))
        {
            return;
        }

        GameObject candidate = GetBuildRootObject(hit.collider);
        if (candidate == null)
        {
            return;
        }

        grabbedObject = candidate;
        objectDistance = Mathf.Clamp(
            Vector3.Distance(playerCamera.transform.position, grabbedObject.transform.position),
            minHoldDistance,
            maxGrabDistance
        );
        IsCarrying = true;
        grabbedObjectStartedOutsideBuildArea = !IsObjectInsideBuildArea(grabbedObject);

        if (!grabbedObjectStartedOutsideBuildArea)
        {
            lastValidPosition = grabbedObject.transform.position;
            hasLastValidPosition = true;
        }
        else
        {
            lastValidPosition = Vector3.zero;
            hasLastValidPosition = false;
        }

        if (!grabbedObjectStartedOutsideBuildArea && !IsWheelObject(grabbedObject))
        {
            SnapRotationToBuildAngles(grabbedObject);
        }

        previousHeldPosition = grabbedObject.transform.position;
        estimatedHeldVelocity = Vector3.zero;
        hasHeldMotionSample = true;

        Rigidbody rb = grabbedObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            DetachObjectFromBuild(grabbedObject);
            SetLoosePhysicsState(grabbedObject, false);

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.isKinematic = true;
            rb.Sleep();
        }
    }

    void ReleaseObject()
    {
        if (grabbedObject == null)
        {
            return;
        }

        Vector3 releaseVelocity = GetReleaseVelocity();

        if (ShouldSnapToBuild(grabbedObject))
        {
            PlaceObjectInCell(grabbedObject);
        }
        else
        {
            ReleaseObjectFreely(grabbedObject, releaseVelocity);
        }

        grabbedObject = null;
        RefreshMotorContainerTransparency(null);
        IsCarrying = false;
        hasHeldMotionSample = false;
        estimatedHeldVelocity = Vector3.zero;
    }

    void MoveGrabbedObject()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 rawTargetPosition = ray.origin + ray.direction * objectDistance;
        Vector3 safeTargetPosition = GetSafeCarryTargetPosition(rawTargetPosition);

        bool hasCellTarget = TryGetNearestAvailableCellPosition(safeTargetPosition, grabbedObject, out Vector3 cellPosition);
        RefreshMotorContainerTransparency(hasCellTarget ? (Vector3?)cellPosition : null);

        if (hasCellTarget)
        {
            if (ShouldResetRotationForBuildPlacement(grabbedObject))
            {
                ResetRotationForBuildPlacement(grabbedObject);
            }
            else
            {
                SnapRotationToBuildAngles(grabbedObject);
            }

            MoveObjectTowards(cellPosition);
            lastValidPosition = cellPosition;
            hasLastValidPosition = true;
            return;
        }

        if (IsNearAnyCell(safeTargetPosition))
        {
            SnapRotationToBuildAngles(grabbedObject);

            if (hasLastValidPosition)
            {
                MoveObjectTowards(lastValidPosition);
            }

            return;
        }

        MoveObjectTowards(safeTargetPosition);
    }

    void PlaceObjectInCell(GameObject obj)
    {
        Rigidbody rb = obj.GetComponent<Rigidbody>();
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

        SetLoosePhysicsState(obj, false);

        if (TryGetNearestAvailableCellPosition(obj.transform.position, obj, out Vector3 cellPosition))
        {
            obj.transform.position = cellPosition;

            if (ShouldResetRotationForBuildPlacement(obj))
            {
                ResetRotationForBuildPlacement(obj);
            }

            SnapRotationToBuildAngles(obj);

            freelyReleasedObjectIds.Remove(obj.GetEntityId());
            grabbedObjectStartedOutsideBuildArea = false;

            lastValidPosition = cellPosition;
            hasLastValidPosition = true;
            Debug.Log($"{obj.name} {cellPosition} pozisyonuna yerlestirildi.");
            return;
        }

        if (hasLastValidPosition)
        {
            obj.transform.position = lastValidPosition;
        }

        Debug.LogWarning($"{obj.name} dolu bir hucreye yerlestirilemedi, son gecerli konuma dondu.");
    }

    public bool RotateHeldObject(float rotation)
    {
        if (grabbedObject == null)
        {
            return false;
        }

        if (!CanRotateObjectInBuildArea(grabbedObject))
        {
            return false;
        }

        grabbedObject.transform.Rotate(0f, rotation, 0f, Space.World);
        SnapRotationToBuildAngles(grabbedObject);
        return true;
    }

    public bool RotateHeldObject(Vector3 axis, float rotation, Space relativeTo)
    {
        if (grabbedObject == null)
        {
            return false;
        }

        if (!CanRotateObjectInBuildArea(grabbedObject))
        {
            return false;
        }

        grabbedObject.transform.Rotate(axis, rotation, relativeTo);
        SnapRotationToBuildAngles(grabbedObject);
        return true;
    }

    public bool CanRotateObjectInBuildArea(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (IsWheelObject(obj))
        {
            return false;
        }

        return IsObjectInsideBuildArea(obj) || ShouldSnapToBuild(obj);
    }

    public void SnapObjectToBuildRotation(GameObject obj)
    {
        if (!CanRotateObjectInBuildArea(obj))
        {
            return;
        }

        SnapRotationToBuildAngles(obj);
    }

    public void SnapAllObjectsInsideBuildArea()
    {
        HashSet<Rigidbody> buildBodies = BuildVehicleUtility.GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null || !IsObjectInsideBuildArea(body.gameObject) || IsWheelObject(body.gameObject))
            {
                continue;
            }

            SnapRotationToBuildAngles(body.gameObject);
        }
    }

    public void ReleaseHeldObjectForSimulation()
    {
        if (grabbedObject == null)
        {
            return;
        }

        PlaceObjectInCell(grabbedObject);
        grabbedObject = null;
        RefreshMotorContainerTransparency(null);
        IsCarrying = false;
    }

    private GameObject GetBuildRootObject(Collider hitCollider)
    {
        Rigidbody hitBody = hitCollider.attachedRigidbody;
        if (hitBody == null)
        {
            hitBody = hitCollider.GetComponentInParent<Rigidbody>();
        }

        if (hitBody == null)
        {
            return null;
        }

        string tagName = hitBody.gameObject.tag;
        if (tagName != "Cube" && tagName != "Parts" && tagName != "wheel")
        {
            return null;
        }

        return hitBody.gameObject;
    }

    private void AdjustHoldDistance()
    {
        float scrollInput = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scrollInput) < Mathf.Epsilon)
        {
            return;
        }

        objectDistance = Mathf.Clamp(
            objectDistance + scrollInput * grabDistanceScrollSpeed,
            minHoldDistance,
            maxGrabDistance
        );
    }

    private void MoveObjectTowards(Vector3 targetPosition)
    {
        grabbedObject.transform.position = Vector3.Lerp(
            grabbedObject.transform.position,
            targetPosition,
            Time.deltaTime * grabMoveSpeed
        );
    }

    private void UpdateHeldMotionEstimate()
    {
        if (grabbedObject == null)
        {
            hasHeldMotionSample = false;
            estimatedHeldVelocity = Vector3.zero;
            return;
        }

        Vector3 currentPosition = grabbedObject.transform.position;
        if (!hasHeldMotionSample || Time.deltaTime <= 0.0001f)
        {
            previousHeldPosition = currentPosition;
            hasHeldMotionSample = true;
            estimatedHeldVelocity = Vector3.zero;
            return;
        }

        Vector3 rawVelocity = (currentPosition - previousHeldPosition) / Time.deltaTime;
        estimatedHeldVelocity = Vector3.Lerp(estimatedHeldVelocity, rawVelocity, heldVelocitySmoothing);
        previousHeldPosition = currentPosition;
    }

    private Vector3 GetReleaseVelocity()
    {
        if (estimatedHeldVelocity.magnitude < throwVelocityThreshold)
        {
            return Vector3.zero;
        }

        return Vector3.ClampMagnitude(estimatedHeldVelocity, maxThrowVelocity);
    }

    private Vector3 GetSafeCarryTargetPosition(Vector3 targetPosition)
    {
        if (grabbedObject == null)
        {
            return targetPosition;
        }

        if (!TryGetCarryBounds(grabbedObject, out Bounds carryBounds))
        {
            return targetPosition;
        }

        Vector3 currentPosition = grabbedObject.transform.position;
        Vector3 moveDelta = targetPosition - currentPosition;
        float moveDistance = moveDelta.magnitude;
        if (moveDistance <= 0.0001f)
        {
            return targetPosition;
        }

        Vector3 castDirection = moveDelta / moveDistance;
        Vector3 castHalfExtents = new Vector3(
            Mathf.Max(carryBounds.extents.x, minimumCarryBoundsExtent),
            Mathf.Max(carryBounds.extents.y, minimumCarryBoundsExtent),
            Mathf.Max(carryBounds.extents.z, minimumCarryBoundsExtent));

        RaycastHit[] hits = Physics.BoxCastAll(
            carryBounds.center,
            castHalfExtents,
            castDirection,
            grabbedObject.transform.rotation,
            moveDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        foreach (RaycastHit hit in hits)
        {
            if (ShouldIgnoreCarryCollision(hit.collider))
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
            }
        }

        if (float.IsPositiveInfinity(nearestDistance))
        {
            return targetPosition;
        }

        float safeDistance = Mathf.Max(0f, nearestDistance - carryCollisionPadding);
        return currentPosition + castDirection * safeDistance;
    }

    private bool ShouldSnapToBuild(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        return TryGetNearestAvailableCellPosition(obj.transform.position, obj, out _) ||
               IsNearAnyCell(obj.transform.position);
    }

    private bool ShouldResetRotationForBuildPlacement(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj.CompareTag("wheel") ||
            obj.GetComponent<FrontWheele>() != null ||
            obj.GetComponent<BackWheele>() != null)
        {
            return false;
        }

        return grabbedObjectStartedOutsideBuildArea || freelyReleasedObjectIds.Contains(obj.GetEntityId());
    }

    private void ResetRotationForBuildPlacement(GameObject obj)
    {
        obj.transform.rotation = Quaternion.identity;
    }

    private void ReleaseObjectFreely(GameObject obj, Vector3 releaseVelocity)
    {
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            return;
        }

        freelyReleasedObjectIds.Add(obj.GetEntityId());
        SetLoosePhysicsState(obj, true);
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.WakeUp();
        rb.linearVelocity = releaseVelocity;
        rb.angularVelocity = Vector3.zero;
    }

    public bool TrySnapReleasedObjectToCell(GameObject obj, Collider cellCollider)
    {
        if (obj == null || cellCollider == null || !cellCollider.CompareTag("Cell"))
        {
            return false;
        }

        if (!freelyReleasedObjectIds.Contains(obj.GetEntityId()))
        {
            return false;
        }

        Vector3 cellPosition = cellCollider.transform.position;
        if (IsCellOccupied(cellPosition, obj))
        {
            return false;
        }

        return TryPlaceObjectAtCell(obj, cellPosition, false);
    }

    private void DetachObjectFromBuild(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        Rigidbody targetBody = obj.GetComponent<Rigidbody>();
        if (targetBody == null)
        {
            return;
        }

        HashSet<Rigidbody> buildBodies = BuildVehicleUtility.GetBuildPartRigidbodies();

        FixedJoint[] ownJoints = obj.GetComponents<FixedJoint>();
        foreach (FixedJoint joint in ownJoints)
        {
            Destroy(joint);
        }

        foreach (Rigidbody body in buildBodies)
        {
            if (body == null || body == targetBody)
            {
                continue;
            }

            FixedJoint[] joints = body.GetComponents<FixedJoint>();
            foreach (FixedJoint joint in joints)
            {
                if (joint != null && joint.connectedBody == targetBody)
                {
                    Destroy(joint);
                }
            }
        }

        ResetAllAttachmentFlags(buildBodies);
        RefreshCubeConnectionCaches(buildBodies);
    }

    private void ResetAllAttachmentFlags(IEnumerable<Rigidbody> buildBodies)
    {
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null)
            {
                continue;
            }

            AttachPoint[] cubeAttachPoints = body.GetComponentsInChildren<AttachPoint>(true);
            foreach (AttachPoint attachPoint in cubeAttachPoints)
            {
                attachPoint.ClearConnection();
            }

            AttachPointParts[] partAttachPoints = body.GetComponentsInChildren<AttachPointParts>(true);
            foreach (AttachPointParts attachPoint in partAttachPoints)
            {
                attachPoint.ClearConnection();
            }
        }
    }

    private void RefreshCubeConnectionCaches(IEnumerable<Rigidbody> buildBodies)
    {
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null)
            {
                continue;
            }

            Cube cube = body.GetComponent<Cube>();
            if (cube != null)
            {
                cube.ClearConnectedRigidbodies();
            }
        }

        foreach (Rigidbody body in buildBodies)
        {
            if (body == null)
            {
                continue;
            }

            Cube cube = body.GetComponent<Cube>();
            if (cube == null)
            {
                continue;
            }

            FixedJoint[] joints = body.GetComponents<FixedJoint>();
            foreach (FixedJoint joint in joints)
            {
                if (joint == null || joint.connectedBody == null)
                {
                    continue;
                }

                cube.AddConnectedRigidbody(joint.connectedBody);

                Cube connectedCube = joint.connectedBody.GetComponent<Cube>();
                if (connectedCube != null)
                {
                    connectedCube.AddConnectedRigidbody(body);
                }
            }
        }
    }

    private void SetLoosePhysicsState(GameObject obj, bool looseMode)
    {
        if (obj == null)
        {
            return;
        }

        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);

        foreach (Collider collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            if (collider.transform.name == FreePhysicsColliderName)
            {
                continue;
            }

            if (collider is WheelCollider)
            {
                collider.enabled = false;
                continue;
            }

            collider.enabled = !looseMode;
        }

        if (!looseMode)
        {
            DisableFreePhysicsCollider(obj);
            SetReleasedBuildSnapEnabled(obj, false);
            return;
        }

        BoxCollider runtimeCollider = GetOrCreateFreePhysicsCollider(obj);
        FitFreePhysicsColliderToRenderers(obj, runtimeCollider);
        runtimeCollider.enabled = true;
        SetReleasedBuildSnapEnabled(obj, true);
    }

    private void DisableFreePhysicsCollider(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        Transform runtimeColliderTransform = obj.transform.Find(FreePhysicsColliderName);
        if (runtimeColliderTransform == null)
        {
            return;
        }

        Collider runtimeCollider = runtimeColliderTransform.GetComponent<Collider>();
        if (runtimeCollider != null)
        {
            runtimeCollider.enabled = false;
        }
    }

    private BoxCollider GetOrCreateFreePhysicsCollider(GameObject obj)
    {
        Transform runtimeColliderTransform = obj.transform.Find(FreePhysicsColliderName);
        if (runtimeColliderTransform == null)
        {
            GameObject colliderObject = new GameObject(FreePhysicsColliderName);
            colliderObject.transform.SetParent(obj.transform, false);
            runtimeColliderTransform = colliderObject.transform;
        }

        BoxCollider runtimeCollider = runtimeColliderTransform.GetComponent<BoxCollider>();
        if (runtimeCollider == null)
        {
            runtimeCollider = runtimeColliderTransform.gameObject.AddComponent<BoxCollider>();
        }

        runtimeCollider.isTrigger = false;
        return runtimeCollider;
    }

    private void FitFreePhysicsColliderToRenderers(GameObject obj, BoxCollider runtimeCollider)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds localBounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Vector3 center = renderer.bounds.center;
            Vector3 extents = renderer.bounds.extents;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 worldCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                        Vector3 localCorner = obj.transform.InverseTransformPoint(worldCorner);

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

        if (!hasBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.one * 0.5f);
        }

        runtimeCollider.center = localBounds.center;
        runtimeCollider.size = localBounds.size;
    }

    private bool TryGetNearestAvailableCellPosition(Vector3 targetPosition, GameObject movingObject, out Vector3 cellPosition)
    {
        cellPosition = Vector3.zero;
        Collider[] hits = Physics.OverlapSphere(targetPosition, cellSearchRadius);

        bool foundCell = false;
        float nearestDistance = float.MaxValue;

        foreach (Collider hit in hits)
        {
            if (!hit.CompareTag("Cell"))
            {
                continue;
            }

            Vector3 candidatePosition = hit.transform.position;
            if (IsCellOccupied(candidatePosition, movingObject))
            {
                continue;
            }

            float distance = Vector3.Distance(targetPosition, candidatePosition);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                cellPosition = candidatePosition;
                foundCell = true;
            }
        }

        return foundCell;
    }

    private bool IsNearAnyCell(Vector3 targetPosition)
    {
        Collider[] hits = Physics.OverlapSphere(targetPosition, cellSearchRadius);
        foreach (Collider hit in hits)
        {
            if (hit.CompareTag("Cell"))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsObjectInsideBuildArea(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        return TryGetNearestAvailableCellPosition(obj.transform.position, obj, out _) ||
               IsNearAnyCell(obj.transform.position);
    }

    private void SnapRotationToBuildAngles(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        Vector3 euler = obj.transform.eulerAngles;
        obj.transform.rotation = Quaternion.Euler(
            SnapAngleToQuarterTurn(euler.x),
            SnapAngleToQuarterTurn(euler.y),
            SnapAngleToQuarterTurn(euler.z));
    }

    private float SnapAngleToQuarterTurn(float angle)
    {
        angle = Mathf.Repeat(angle, 360f);
        return Mathf.Repeat(Mathf.Round(angle / 90f) * 90f, 360f);
    }

    private bool IsWheelObject(GameObject obj)
    {
        return obj != null &&
               (obj.CompareTag("wheel") ||
                obj.GetComponent<FrontWheele>() != null ||
                obj.GetComponent<BackWheele>() != null);
    }

    private bool TryGetCarryBounds(GameObject obj, out Bounds bounds)
    {
        bool hasBounds = false;
        bounds = default;

        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
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

        if (hasBounds)
        {
            return true;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
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

    private bool ShouldIgnoreCarryCollision(Collider collider)
    {
        if (collider == null || collider.isTrigger)
        {
            return true;
        }

        if (grabbedObject != null && collider.transform.IsChildOf(grabbedObject.transform))
        {
            return true;
        }

        if (collider.CompareTag("Cell"))
        {
            return true;
        }

        Rigidbody attachedBody = collider.attachedRigidbody != null
            ? collider.attachedRigidbody
            : collider.GetComponentInParent<Rigidbody>();

        return attachedBody != null && IsBuildPartTag(attachedBody.gameObject.tag);
    }

    private bool IsBuildPartTag(string tagName)
    {
        return tagName == "Cube" || tagName == "Parts" || tagName == "wheel";
    }

    private bool IsCellOccupied(Vector3 cellPosition, GameObject movingObject)
    {
        HashSet<Rigidbody> buildBodies = BuildVehicleUtility.GetBuildPartRigidbodies();
        foreach (Rigidbody body in buildBodies)
        {
            if (body == null || body.gameObject == movingObject)
            {
                continue;
            }

            if (Vector3.Distance(body.transform.position, cellPosition) <= occupiedCellThreshold)
            {
                if (CanShareCellWithMovingObject(movingObject, body.gameObject))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private bool TryPlaceObjectAtCell(GameObject obj, Vector3 cellPosition, bool requireCellAvailabilityCheck)
    {
        if (obj == null)
        {
            return false;
        }

        if (requireCellAvailabilityCheck && IsCellOccupied(cellPosition, obj))
        {
            return false;
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
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

        SetLoosePhysicsState(obj, false);
        obj.transform.position = cellPosition;

        if (ShouldResetRotationForBuildPlacement(obj))
        {
            ResetRotationForBuildPlacement(obj);
        }

        SnapRotationToBuildAngles(obj);
        freelyReleasedObjectIds.Remove(obj.GetEntityId());
        grabbedObjectStartedOutsideBuildArea = false;
        lastValidPosition = cellPosition;
        hasLastValidPosition = true;
        Physics.SyncTransforms();
        return true;
    }

    private void SetReleasedBuildSnapEnabled(GameObject obj, bool enabled)
    {
        if (obj == null)
        {
            return;
        }

        ReleasedBuildSnapHandler snapHandler = obj.GetComponent<ReleasedBuildSnapHandler>();
        if (enabled)
        {
            if (snapHandler == null)
            {
                snapHandler = obj.AddComponent<ReleasedBuildSnapHandler>();
            }

            snapHandler.Initialize(this);
            return;
        }

        if (snapHandler != null)
        {
            snapHandler.DisableHandler();
        }
    }

    private bool CanShareCellWithMovingObject(GameObject movingObject, GameObject stationaryObject)
    {
        return IsMotorModule(movingObject) && IsContainerCube(stationaryObject);
    }

    private bool IsMotorModule(GameObject target)
    {
        return target != null && target.GetComponentInChildren<PowerModule>(true) != null;
    }

    private bool IsContainerCube(GameObject target)
    {
        return target != null &&
               target.CompareTag("Cube") &&
               target.GetComponent<Cube>() != null &&
               target.GetComponent<PowerModule>() == null &&
               target.GetComponent<VehicleSeat>() == null;
    }

    private void RefreshMotorContainerTransparency(Vector3? candidateCellPosition)
    {
        HashSet<GameObject> desiredTransparentCubes = GetPlacedMotorContainerCubes();

        if (IsMotorModule(grabbedObject) && candidateCellPosition.HasValue)
        {
            foreach (GameObject cube in GetContainerCubesAtCell(candidateCellPosition.Value, grabbedObject))
            {
                desiredTransparentCubes.Add(cube);
            }
        }

        if (previewedContainerCubes.SetEquals(desiredTransparentCubes))
        {
            return;
        }

        ClearMotorContainerPreview();

        foreach (GameObject cube in desiredTransparentCubes)
        {
            ApplyTransparentPreview(cube);
            previewedContainerCubes.Add(cube);
        }
    }

    private HashSet<GameObject> GetPlacedMotorContainerCubes()
    {
        HashSet<GameObject> cubes = new HashSet<GameObject>();
        HashSet<Rigidbody> buildBodies = BuildVehicleUtility.GetBuildPartRigidbodies();

        foreach (Rigidbody body in buildBodies)
        {
            if (body == null || body.gameObject == grabbedObject || !IsMotorModule(body.gameObject))
            {
                continue;
            }

            foreach (GameObject cube in GetContainerCubesAtCell(body.transform.position, body.gameObject))
            {
                cubes.Add(cube);
            }
        }

        return cubes;
    }

    private List<GameObject> GetContainerCubesAtCell(Vector3 cellPosition, GameObject movingObject)
    {
        List<GameObject> cubes = new List<GameObject>();
        HashSet<Rigidbody> buildBodies = BuildVehicleUtility.GetBuildPartRigidbodies();

        foreach (Rigidbody body in buildBodies)
        {
            if (body == null || body.gameObject == movingObject)
            {
                continue;
            }

            if (Vector3.Distance(body.transform.position, cellPosition) > occupiedCellThreshold)
            {
                continue;
            }

            if (IsContainerCube(body.gameObject))
            {
                cubes.Add(body.gameObject);
            }
        }

        return cubes;
    }

    private void ApplyTransparentPreview(GameObject cubeRoot)
    {
        if (cubeRoot == null)
        {
            return;
        }

        Renderer[] renderers = cubeRoot.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || previewOriginalMaterials.ContainsKey(renderer))
            {
                continue;
            }

            Material[] originalMaterials = renderer.sharedMaterials;
            previewOriginalMaterials[renderer] = originalMaterials;

            Material[] transparentMaterials = new Material[originalMaterials.Length];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                transparentMaterials[i] = GetTransparentPreviewMaterial(originalMaterials[i]);
            }

            renderer.sharedMaterials = transparentMaterials;
        }
    }

    private Material GetTransparentPreviewMaterial(Material originalMaterial)
    {
        if (originalMaterial == null)
        {
            return null;
        }

        if (transparentMaterialCache.TryGetValue(originalMaterial, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material transparentMaterial = new Material(originalMaterial);
        transparentMaterial.name = originalMaterial.name + "_MotorInsidePreview";

        if (transparentMaterial.HasProperty("_Surface"))
        {
            transparentMaterial.SetFloat("_Surface", 1f);
        }

        if (transparentMaterial.HasProperty("_Blend"))
        {
            transparentMaterial.SetFloat("_Blend", 0f);
        }

        if (transparentMaterial.HasProperty("_SrcBlend"))
        {
            transparentMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (transparentMaterial.HasProperty("_DstBlend"))
        {
            transparentMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (transparentMaterial.HasProperty("_ZWrite"))
        {
            transparentMaterial.SetFloat("_ZWrite", 0f);
        }

        transparentMaterial.SetOverrideTag("RenderType", "Transparent");
        transparentMaterial.renderQueue = (int)RenderQueue.Transparent;
        transparentMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        transparentMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        if (transparentMaterial.HasProperty("_BaseColor"))
        {
            Color baseColor = transparentMaterial.GetColor("_BaseColor");
            baseColor.a = Mathf.Min(baseColor.a, motorInsideCubePreviewAlpha);
            transparentMaterial.SetColor("_BaseColor", baseColor);
        }

        if (transparentMaterial.HasProperty("_Color"))
        {
            Color color = transparentMaterial.GetColor("_Color");
            color.a = Mathf.Min(color.a, motorInsideCubePreviewAlpha);
            transparentMaterial.SetColor("_Color", color);
        }

        transparentMaterialCache[originalMaterial] = transparentMaterial;
        return transparentMaterial;
    }

    private void ClearMotorContainerPreview()
    {
        foreach (KeyValuePair<Renderer, Material[]> entry in previewOriginalMaterials)
        {
            if (entry.Key != null)
            {
                entry.Key.sharedMaterials = entry.Value;
            }
        }

        previewOriginalMaterials.Clear();
        previewedContainerCubes.Clear();
    }
}
