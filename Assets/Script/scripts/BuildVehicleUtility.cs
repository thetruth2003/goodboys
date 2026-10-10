using System.Collections.Generic;
using UnityEngine;

public static class BuildVehicleUtility
{
    public static HashSet<Rigidbody> GetBuildPartRigidbodies()
    {
        HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();
        AddTaggedRigidbodies("Cube", bodies);
        AddTaggedRigidbodies("Parts", bodies);
        AddTaggedRigidbodies("wheel", bodies);
        return bodies;
    }

    public static HashSet<Rigidbody> CollectConnectedBodies(Rigidbody source)
    {
        HashSet<Rigidbody> connectedBodies = new HashSet<Rigidbody>();
        HashSet<Rigidbody> allBodies = GetBuildPartRigidbodies();

        if (source == null)
        {
            return connectedBodies;
        }

        Queue<Rigidbody> pending = new Queue<Rigidbody>();
        pending.Enqueue(source);

        while (pending.Count > 0)
        {
            Rigidbody current = pending.Dequeue();
            if (current == null || !connectedBodies.Add(current))
            {
                continue;
            }

            foreach (Rigidbody connectedBody in GetConnectedBodies(current, allBodies))
            {
                if (!connectedBodies.Contains(connectedBody))
                {
                    pending.Enqueue(connectedBody);
                }
            }
        }

        return connectedBodies;
    }

    public static Rigidbody GetPrimaryConnectedBody(Rigidbody source)
    {
        HashSet<Rigidbody> connectedBodies = CollectConnectedBodies(source);
        return GetPreferredPrimaryBody(connectedBodies, source);
    }

    public static Rigidbody GetPreferredPrimaryBody(IEnumerable<Rigidbody> bodies, Rigidbody fallback = null)
    {
        Rigidbody primaryBody = fallback;
        int bestPriority = GetPrimaryBodyPriority(fallback);
        float highestMass = fallback != null ? fallback.mass : float.MinValue;

        if (bodies == null)
        {
            return primaryBody;
        }

        foreach (Rigidbody body in bodies)
        {
            if (body == null)
            {
                continue;
            }

            int priority = GetPrimaryBodyPriority(body);
            if (primaryBody == null || priority > bestPriority || (priority == bestPriority && body.mass > highestMass))
            {
                bestPriority = priority;
                highestMass = body.mass;
                primaryBody = body;
            }
        }

        return primaryBody;
    }

    public static float GetVehicleDirectionSign(Transform partTransform, Rigidbody source)
    {
        if (partTransform == null)
        {
            return 1f;
        }

        Rigidbody primaryBody = GetPrimaryConnectedBody(source);
        Transform referenceTransform = primaryBody != null ? primaryBody.transform : partTransform;

        Vector3 partForward = Vector3.ProjectOnPlane(partTransform.forward, Vector3.up);
        Vector3 referenceForward = Vector3.ProjectOnPlane(referenceTransform.forward, Vector3.up);

        if (partForward.sqrMagnitude < 0.0001f || referenceForward.sqrMagnitude < 0.0001f)
        {
            return 1f;
        }

        return Vector3.Dot(partForward.normalized, referenceForward.normalized) < 0f ? -1f : 1f;
    }

    public static float GetVehicleSideSign(Transform partTransform, Rigidbody source)
    {
        if (partTransform == null)
        {
            return 1f;
        }

        Rigidbody primaryBody = GetPrimaryConnectedBody(source);
        Transform referenceTransform = primaryBody != null ? primaryBody.transform : partTransform;
        Vector3 localPosition = referenceTransform.InverseTransformPoint(partTransform.position);

        if (Mathf.Abs(localPosition.x) < 0.001f)
        {
            return 1f;
        }

        return localPosition.x >= 0f ? 1f : -1f;
    }

    public static List<Rigidbody> GetConnectedBodies(Rigidbody source, IReadOnlyCollection<Rigidbody> allBodies)
    {
        List<Rigidbody> connectedBodies = new List<Rigidbody>();
        if (source == null)
        {
            return connectedBodies;
        }

        HashSet<Rigidbody> allBodyLookup = allBodies as HashSet<Rigidbody> ?? new HashSet<Rigidbody>(allBodies);

        FixedJoint[] ownJoints = source.GetComponents<FixedJoint>();
        foreach (FixedJoint joint in ownJoints)
        {
            Rigidbody connectedBody = joint.connectedBody;
            if (connectedBody != null && allBodyLookup.Contains(connectedBody) && !connectedBodies.Contains(connectedBody))
            {
                connectedBodies.Add(connectedBody);
            }
        }

        foreach (Rigidbody candidate in allBodyLookup)
        {
            if (candidate == null || candidate == source)
            {
                continue;
            }

            FixedJoint[] candidateJoints = candidate.GetComponents<FixedJoint>();
            foreach (FixedJoint joint in candidateJoints)
            {
                if (joint.connectedBody == source && !connectedBodies.Contains(candidate))
                {
                    connectedBodies.Add(candidate);
                    break;
                }
            }
        }

        return connectedBodies;
    }

    public static float GetConnectedPowerMultiplier(Rigidbody source)
    {
        if (source == null)
        {
            return 1f;
        }

        HashSet<Rigidbody> connectedBodies = CollectConnectedBodies(source);
        return GetPowerMultiplier(connectedBodies);
    }

    public static float GetPowerMultiplier(IReadOnlyCollection<Rigidbody> bodies)
    {
        if (bodies == null || bodies.Count == 0)
        {
            return 1f;
        }

        float totalMultiplier = 1f;
        bool hasAnyModule = false;

        foreach (Rigidbody body in bodies)
        {
            if (body == null)
            {
                continue;
            }

            PowerModule[] modules = body.GetComponentsInChildren<PowerModule>(true);
            foreach (PowerModule module in modules)
            {
                if (module != null)
                {
                    totalMultiplier *= module.PowerMultiplier;
                    hasAnyModule = true;
                }
            }
        }

        return hasAnyModule ? totalMultiplier : 1f;
    }

    private static void AddTaggedRigidbodies(string tagName, HashSet<Rigidbody> bodies)
    {
        GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(tagName);
        foreach (GameObject taggedObject in taggedObjects)
        {
            Rigidbody body = taggedObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                bodies.Add(body);
            }
        }
    }

    private static int GetPrimaryBodyPriority(Rigidbody body)
    {
        if (body == null)
        {
            return int.MinValue;
        }

        if (body.CompareTag("Cube"))
        {
            return 300;
        }

        if (body.GetComponentInChildren<VehicleSeat>(true) != null)
        {
            return 0;
        }

        if (body.GetComponentInChildren<PowerModule>(true) != null)
        {
            return 25;
        }

        if (body.CompareTag("Parts"))
        {
            return 200;
        }

        if (body.CompareTag("wheel"))
        {
            return 1;
        }

        return 2;
    }
}
