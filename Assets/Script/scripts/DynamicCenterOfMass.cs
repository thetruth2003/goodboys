using System.Collections.Generic;
using UnityEngine;

public class DynamicCenterOfMass : MonoBehaviour
{
    private const float RefreshInterval = 0.1f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        RefreshNow();
        CancelInvoke(nameof(UpdateCenterOfMass));
        InvokeRepeating(nameof(UpdateCenterOfMass), RefreshInterval, RefreshInterval);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(UpdateCenterOfMass));
    }

    public void RefreshNow()
    {
        UpdateCenterOfMass();
    }

    private void UpdateCenterOfMass()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (rb == null)
        {
            return;
        }

        List<Rigidbody> connectedBodies = new List<Rigidbody>(BuildVehicleUtility.CollectConnectedBodies(rb));
        if (connectedBodies.Count == 0)
        {
            return;
        }

        Vector3 newCenterOfMass = Vector3.zero;
        float totalMass = 0f;

        foreach (Rigidbody body in connectedBodies)
        {
            newCenterOfMass += body.worldCenterOfMass * body.mass;
            totalMass += body.mass;
        }

        if (totalMass > 0f)
        {
            newCenterOfMass /= totalMass;
            rb.centerOfMass = transform.InverseTransformPoint(newCenterOfMass);
        }
    }
}
