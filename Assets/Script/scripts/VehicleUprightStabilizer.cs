using UnityEngine;

public class VehicleUprightStabilizer : MonoBehaviour
{
    public float uprightStrength = 1.75f;
    public float angularDamping = 0.85f;
    public float activationAngle = 4f;
    public float fallingSpeedThreshold = -0.15f;
    public float maxTorque = 6f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (rb == null || rb.isKinematic)
        {
            return;
        }

        if (!IsFinite(rb.position) || !IsFinite(rb.rotation) || !IsFinite(rb.angularVelocity) || !IsFinite(rb.linearVelocity))
        {
            return;
        }

        float uprightAngle = Vector3.Angle(transform.up, Vector3.up);
        if (uprightAngle < activationAngle && rb.linearVelocity.y > fallingSpeedThreshold)
        {
            return;
        }

        Vector3 uprightError = Vector3.Cross(transform.up, Vector3.up);
        Vector3 stabilizationTorque = Vector3.ClampMagnitude(uprightError * uprightStrength, maxTorque);
        Vector3 dampingTorque = -rb.angularVelocity * angularDamping;

        rb.AddTorque(stabilizationTorque + dampingTorque, ForceMode.Acceleration);
    }

    private bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private bool IsFinite(Quaternion value)
    {
        return float.IsFinite(value.x) &&
               float.IsFinite(value.y) &&
               float.IsFinite(value.z) &&
               float.IsFinite(value.w);
    }
}
