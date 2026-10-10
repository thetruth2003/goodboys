using System.Collections.Generic;
using UnityEngine;

public class Cube : MonoBehaviour
{
    public List<Rigidbody> connectedRigidbodies = new List<Rigidbody>();

    public void AddConnectedRigidbody(Rigidbody rb)
    {
        if (!connectedRigidbodies.Contains(rb))
        {
            connectedRigidbodies.Add(rb);
        }
    }

    public bool IsRigidbodyConnected(Rigidbody rb)
    {
        return connectedRigidbodies.Contains(rb);
    }

    public List<Rigidbody> GetConnectedRigidbodies()
    {
        return new List<Rigidbody>(connectedRigidbodies);
    }

    public void RemoveConnectedRigidbody(Rigidbody rb)
    {
        connectedRigidbodies.Remove(rb);
    }

    public void ClearConnectedRigidbodies()
    {
        connectedRigidbodies.Clear();
    }
}
