using System.Collections.Generic;
using UnityEngine;

public class AttachPoint : MonoBehaviour
{
    public GameObject connectedCube = null;

    private const float FinalizePadding = 0.01f;

    private Collider triggerCollider;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        return;
    }

    public bool IsOccupied()
    {
        return connectedCube != null;
    }

    public void ClearConnection()
    {
        connectedCube = null;
    }

    public bool TryFinalizeAttachment()
    {
        if (connectedCube != null)
        {
            return false;
        }

        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<Collider>();
        }

        if (triggerCollider == null)
        {
            return false;
        }

        Bounds bounds = triggerCollider.bounds;
        Collider[] overlaps = Physics.OverlapBox(
            bounds.center,
            bounds.extents + Vector3.one * FinalizePadding,
            transform.rotation,
            ~0,
            QueryTriggerInteraction.Collide
        );

        foreach (Collider overlap in overlaps)
        {
            if (overlap == triggerCollider)
            {
                continue;
            }

            if (TryAttachCollider(overlap))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryAttachCollider(Collider other)
    {
        Rigidbody parentRigidbody = transform.parent != null ? transform.parent.GetComponent<Rigidbody>() : null;
        Rigidbody otherRigidbody = GetRootRigidbody(other);

        if (parentRigidbody == null || otherRigidbody == null || otherRigidbody == parentRigidbody)
        {
            return false;
        }

        if (otherRigidbody.gameObject.tag != "Cube")
        {
            return false;
        }

        Cube parentCubeScript = transform.parent.GetComponentInParent<Cube>();
        Cube otherCubeScript = otherRigidbody.GetComponentInParent<Cube>();
        if (parentCubeScript == null || otherCubeScript == null)
        {
            return false;
        }

        if (parentCubeScript.IsRigidbodyConnected(otherRigidbody) ||
            otherCubeScript.IsRigidbodyConnected(parentRigidbody) ||
            HasJointConnection(parentRigidbody, otherRigidbody))
        {
            connectedCube = otherRigidbody.gameObject;
            return false;
        }

        FixedJoint joint = transform.parent.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = otherRigidbody;
        joint.enableCollision = false;
        joint.enablePreprocessing = true;

        connectedCube = otherRigidbody.gameObject;

        parentCubeScript.AddConnectedRigidbody(otherRigidbody);
        otherCubeScript.AddConnectedRigidbody(parentRigidbody);
        SyncAllConnections(parentCubeScript, otherCubeScript);

        Debug.Log($"AttachPoint {name} baglandi: {otherRigidbody.gameObject.name}");
        return true;
    }

    private Rigidbody GetRootRigidbody(Collider other)
    {
        Rigidbody otherRigidbody = other.attachedRigidbody;
        if (otherRigidbody == null)
        {
            otherRigidbody = other.GetComponentInParent<Rigidbody>();
        }

        return otherRigidbody;
    }

    private bool HasJointConnection(Rigidbody parentRigidbody, Rigidbody otherRigidbody)
    {
        FixedJoint[] joints = parentRigidbody.GetComponents<FixedJoint>();
        foreach (FixedJoint existingJoint in joints)
        {
            if (existingJoint.connectedBody == otherRigidbody)
            {
                return true;
            }
        }

        return false;
    }

    private void SyncAllConnections(Cube cubeA, Cube cubeB)
    {
        HashSet<Rigidbody> allConnectedRigidbodies = new HashSet<Rigidbody>();

        allConnectedRigidbodies.UnionWith(cubeA.GetConnectedRigidbodies());
        allConnectedRigidbodies.UnionWith(cubeB.GetConnectedRigidbodies());
        allConnectedRigidbodies.Add(cubeA.GetComponent<Rigidbody>());
        allConnectedRigidbodies.Add(cubeB.GetComponent<Rigidbody>());

        foreach (Rigidbody rb in allConnectedRigidbodies)
        {
            Cube connectedCubeScript = rb.GetComponentInParent<Cube>();
            if (connectedCubeScript == null)
            {
                continue;
            }

            foreach (Rigidbody otherRb in allConnectedRigidbodies)
            {
                if (rb != otherRb)
                {
                    connectedCubeScript.AddConnectedRigidbody(otherRb);
                }
            }
        }
    }
}
