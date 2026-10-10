using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class VehicleWindZone : MonoBehaviour
{
    [Header("Wind")]
    [Tooltip("Ruzgarin local uzaydaki yonu. Ornek: (1,0,0) saga iter, (0,1,0) yukari iter.")]
    [SerializeField] private Vector3 localWindDirection = Vector3.right;

    [Tooltip("Sabit itis gucu. ForceMode.Acceleration olarak uygulanir.")]
    [Min(0f)]
    [SerializeField] private float baseAcceleration = 10f;

    [Tooltip("Ek gust gucu. Ruzgar bazen bununla daha sert vurur.")]
    [Min(0f)]
    [SerializeField] private float gustAcceleration = 3f;

    [Tooltip("Gust dalgalanmasi ne kadar hizli degissin.")]
    [Min(0.01f)]
    [SerializeField] private float gustChangeSpeed = 0.55f;

    [Tooltip("Arac zone merkezindeyken 1, kenara yaklasinca bu carpan kadar guc alsin.")]
    [Range(0f, 1f)]
    [SerializeField] private float edgeAccelerationMultiplier = 0.6f;

    [Header("Filter")]
    [SerializeField] private LayerMask affectedLayers = ~0;

    [Header("Debug")]
    [SerializeField] private bool showSceneGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0.3f, 0.7f, 1f, 0.2f);
    [SerializeField] private Color gizmoArrowColor = new Color(0.2f, 0.9f, 1f, 0.9f);

    private readonly Collider[] overlapResults = new Collider[64];
    private readonly HashSet<Rigidbody> affectedBodies = new HashSet<Rigidbody>();
    private BoxCollider triggerCollider;
    private float gustSeed;

    private void Awake()
    {
        EnsureTriggerSetup();
        InitializeSeeds();
    }

    private void OnValidate()
    {
        EnsureTriggerSetup();
        if (localWindDirection.sqrMagnitude <= 0.0001f)
        {
            localWindDirection = Vector3.right;
        }
    }

    private void FixedUpdate()
    {
        if (PlayerController.BuildModeActive)
        {
            return;
        }

        EnsureTriggerSetup();
        if (triggerCollider == null)
        {
            return;
        }

        CollectAffectedBodies();
        ApplyWindToBodies();
    }

    private void EnsureTriggerSetup()
    {
        if (triggerCollider == null)
        {
            triggerCollider = GetComponent<BoxCollider>();
        }

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void CollectAffectedBodies()
    {
        affectedBodies.Clear();

        Vector3 worldCenter = transform.TransformPoint(triggerCollider.center);
        Vector3 halfExtents = GetWorldHalfExtents(triggerCollider);
        Quaternion orientation = transform.rotation;

        int hitCount = Physics.OverlapBoxNonAlloc(
            worldCenter,
            halfExtents,
            overlapResults,
            orientation,
            affectedLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            Collider overlap = overlapResults[i];
            if (overlap == null)
            {
                continue;
            }

            Rigidbody targetBody = ResolveVehicleRootBody(overlap);
            if (targetBody == null || targetBody.isKinematic)
            {
                continue;
            }

            affectedBodies.Add(targetBody);
        }
    }

    private void ApplyWindToBodies()
    {
        if (affectedBodies.Count == 0)
        {
            return;
        }

        Vector3 worldDirection = transform.TransformDirection(localWindDirection.normalized);
        float gustFactor = Mathf.Lerp(
            0.35f,
            1.25f,
            Mathf.PerlinNoise(gustSeed, Time.time * gustChangeSpeed));

        foreach (Rigidbody body in affectedBodies)
        {
            if (body == null)
            {
                continue;
            }

            float edgeFactor = CalculateEdgeFactor(body.worldCenterOfMass);
            float acceleration = (baseAcceleration + gustAcceleration * gustFactor) * edgeFactor;
            if (acceleration <= 0f)
            {
                continue;
            }

            body.AddForce(worldDirection * acceleration, ForceMode.Acceleration);
        }
    }

    private Rigidbody ResolveVehicleRootBody(Collider overlap)
    {
        Rigidbody attachedBody = overlap.attachedRigidbody;
        if (attachedBody == null)
        {
            attachedBody = overlap.GetComponentInParent<Rigidbody>();
        }

        if (attachedBody == null)
        {
            return null;
        }

        ModularVehicleController controller = attachedBody.GetComponent<ModularVehicleController>();
        if (controller == null)
        {
            controller = attachedBody.GetComponentInParent<ModularVehicleController>();
        }

        if (controller == null || !controller.isActiveAndEnabled)
        {
            return null;
        }

        return controller.GetComponent<Rigidbody>();
    }

    private float CalculateEdgeFactor(Vector3 worldPoint)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint) - triggerCollider.center;
        Vector3 halfExtents = triggerCollider.size * 0.5f;

        float xFactor = halfExtents.x > 0.001f ? 1f - Mathf.Clamp01(Mathf.Abs(localPoint.x) / halfExtents.x) : 1f;
        float yFactor = halfExtents.y > 0.001f ? 1f - Mathf.Clamp01(Mathf.Abs(localPoint.y) / halfExtents.y) : 1f;
        float zFactor = halfExtents.z > 0.001f ? 1f - Mathf.Clamp01(Mathf.Abs(localPoint.z) / halfExtents.z) : 1f;

        float centerFactor = Mathf.Clamp01(Mathf.Min(xFactor, yFactor, zFactor));
        return Mathf.Lerp(edgeAccelerationMultiplier, 1f, centerFactor);
    }

    private Vector3 GetWorldHalfExtents(BoxCollider boxCollider)
    {
        Vector3 scaledSize = Vector3.Scale(boxCollider.size, AbsVector(transform.lossyScale));
        return scaledSize * 0.5f;
    }

    private Vector3 AbsVector(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private void InitializeSeeds()
    {
        gustSeed = Random.Range(4000f, 5000f);
    }

    private void OnDrawGizmos()
    {
        if (!showSceneGizmo)
        {
            return;
        }

        EnsureTriggerSetup();
        if (triggerCollider == null)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(triggerCollider.center, triggerCollider.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.95f);
        Gizmos.DrawWireCube(triggerCollider.center, triggerCollider.size);

        Gizmos.matrix = previousMatrix;
        Gizmos.color = gizmoArrowColor;

        Vector3 center = transform.TransformPoint(triggerCollider.center);
        Vector3 direction = transform.TransformDirection(localWindDirection.normalized);
        float arrowLength = Mathf.Max(triggerCollider.size.x, Mathf.Max(triggerCollider.size.y, triggerCollider.size.z)) * 0.7f;
        Vector3 tip = center + direction * arrowLength;

        Gizmos.DrawLine(center, tip);
        Gizmos.DrawLine(tip, tip - direction * 0.35f + transform.right * 0.2f);
        Gizmos.DrawLine(tip, tip - direction * 0.35f - transform.right * 0.2f);
        Gizmos.color = previousColor;
    }
}
