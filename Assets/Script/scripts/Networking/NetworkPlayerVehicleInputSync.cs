using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CoopPlayerInputState))]
public class NetworkPlayerVehicleInputSync : NetworkBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private CoopPlayerInputState coopPlayerInputState;
    [SerializeField] private float resendThreshold = 0.01f;

    private float lastSentSteeringInput;
    private float lastSentThrottleInput;
    private bool lastSentBrakePressed;
    private bool hasSentInputAtLeastOnce;

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (coopPlayerInputState == null)
        {
            coopPlayerInputState = GetComponent<CoopPlayerInputState>();
        }
    }

    private void Update()
    {
        if (!IsSpawned || !IsOwner || playerController == null)
        {
            return;
        }

        float steeringInput = playerController.GetLocalVehicleSteeringInput();
        float throttleInput = playerController.GetLocalVehicleThrottleInput();
        bool brakePressed = playerController.IsLocalVehicleBrakePressed();

        if (!ShouldSendInput(steeringInput, throttleInput, brakePressed))
        {
            return;
        }

        lastSentSteeringInput = steeringInput;
        lastSentThrottleInput = throttleInput;
        lastSentBrakePressed = brakePressed;
        hasSentInputAtLeastOnce = true;

        if (IsServer)
        {
            ApplyReplicatedInput(steeringInput, throttleInput, brakePressed);
            return;
        }

        SubmitVehicleInputServerRpc(steeringInput, throttleInput, brakePressed);
    }

    public override void OnNetworkDespawn()
    {
        if (coopPlayerInputState != null)
        {
            coopPlayerInputState.ClearReplicatedVehicleInput();
        }

        hasSentInputAtLeastOnce = false;
        lastSentSteeringInput = 0f;
        lastSentThrottleInput = 0f;
        lastSentBrakePressed = false;
    }

    [ServerRpc]
    private void SubmitVehicleInputServerRpc(float steeringInput, float throttleInput, bool brakePressed)
    {
        ApplyReplicatedInput(steeringInput, throttleInput, brakePressed);
    }

    private void ApplyReplicatedInput(float steeringInput, float throttleInput, bool brakePressed)
    {
        if (coopPlayerInputState != null)
        {
            coopPlayerInputState.SetReplicatedVehicleInput(steeringInput, throttleInput, brakePressed);
        }
    }

    private bool ShouldSendInput(float steeringInput, float throttleInput, bool brakePressed)
    {
        if (!hasSentInputAtLeastOnce)
        {
            return true;
        }

        return Mathf.Abs(lastSentSteeringInput - steeringInput) > resendThreshold ||
               Mathf.Abs(lastSentThrottleInput - throttleInput) > resendThreshold ||
               lastSentBrakePressed != brakePressed;
    }
}
