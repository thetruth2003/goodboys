using System.Collections.Generic;
using UnityEngine;

public class Parts : MonoBehaviour
{
    public List<Transform> attachPoints = new List<Transform>();

    private void OnTriggerEnter(Collider other)
    {
        if (PlayerController.BuildModeActive)
        {
            return;
        }

        // Part attachment is finalized from the cube attach points when Tab is pressed.
    }
}
