using UnityEngine;

public class AttachPointParts : MonoBehaviour
{
    public bool isConnected = false;

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
        return isConnected;
    }

    public void MarkConnected()
    {
        isConnected = true;
    }

    public void ClearConnection()
    {
        isConnected = false;
    }

    public bool TryFinalizeAttachment()
    {
        if (isConnected)
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

        if (!IsAttachableTargetRigidbody(otherRigidbody))
        {
            return false;
        }

        if (HasJointConnection(parentRigidbody, otherRigidbody))
        {
            isConnected = true;
            return false;
        }

        FixedJoint joint = transform.parent.gameObject.AddComponent<FixedJoint>();
        joint.connectedBody = otherRigidbody;
        joint.enableCollision = false;
        joint.enablePreprocessing = true;
        isConnected = true;

        Cube parentCube = transform.parent.GetComponentInParent<Cube>();
        if (parentCube != null)
        {
            parentCube.AddConnectedRigidbody(otherRigidbody);
        }

        Cube otherCube = otherRigidbody.GetComponentInParent<Cube>();
        if (otherCube != null)
        {
            otherCube.AddConnectedRigidbody(parentRigidbody);
        }

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

    private bool IsAttachableTargetRigidbody(Rigidbody otherRigidbody)
    {
        string tagName = otherRigidbody.gameObject.tag;
        return tagName == "Cube" || tagName == "Parts" || tagName == "wheel";
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
}
