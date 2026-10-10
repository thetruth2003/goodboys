using UnityEngine;

public class CoopPlayerInputState : MonoBehaviour
{
    private float replicatedSteeringInput;
    private float replicatedThrottleInput;
    private bool replicatedBrakePressed;
    private bool hasReplicatedVehicleInput;

    public bool HasReplicatedVehicleInput => hasReplicatedVehicleInput;

    public float ReplicatedSteeringInput => replicatedSteeringInput;

    public float ReplicatedThrottleInput => replicatedThrottleInput;

    public bool ReplicatedBrakePressed => replicatedBrakePressed;

    public void SetReplicatedVehicleInput(float steeringInput, float throttleInput, bool brakePressed)
    {
        replicatedSteeringInput = Mathf.Clamp(steeringInput, -1f, 1f);
        replicatedThrottleInput = Mathf.Clamp(throttleInput, -1f, 1f);
        replicatedBrakePressed = brakePressed;
        hasReplicatedVehicleInput = true;
    }

    public void ClearReplicatedVehicleInput()
    {
        replicatedSteeringInput = 0f;
        replicatedThrottleInput = 0f;
        replicatedBrakePressed = false;
        hasReplicatedVehicleInput = false;
    }
}
