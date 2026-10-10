using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ReleasedBuildSnapHandler : MonoBehaviour
{
    private ObjectGrabber objectGrabber;

    public void Initialize(ObjectGrabber grabber)
    {
        objectGrabber = grabber;
        enabled = true;
    }

    public void DisableHandler()
    {
        enabled = false;
        objectGrabber = null;
    }

    private void OnTriggerEnter(Collider other)
    {
        TrySnapToCell(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null)
        {
            return;
        }

        TrySnapToCell(collision.collider);
    }

    private void TrySnapToCell(Collider other)
    {
        if (!enabled || other == null || !other.CompareTag("Cell"))
        {
            return;
        }

        if (objectGrabber == null)
        {
            objectGrabber = FindFirstObjectByType<ObjectGrabber>();
            if (objectGrabber == null)
            {
                return;
            }
        }

        if (objectGrabber.TrySnapReleasedObjectToCell(gameObject, other))
        {
            DisableHandler();
        }
    }
}
