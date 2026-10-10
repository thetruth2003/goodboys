using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkPlayerOwnershipBridge : NetworkBehaviour
{
    [SerializeField] private PlayerLocalControlPresentation localControlPresentation;
    [SerializeField] private CoopPlayerInputState coopPlayerInputState;

    private void Awake()
    {
        if (localControlPresentation == null)
        {
            localControlPresentation = GetComponent<PlayerLocalControlPresentation>();
        }

        if (coopPlayerInputState == null)
        {
            coopPlayerInputState = GetComponent<CoopPlayerInputState>();
        }
    }

    public override void OnNetworkSpawn()
    {
        ApplyOwnershipState();
    }

    public override void OnGainedOwnership()
    {
        ApplyOwnershipState();
    }

    public override void OnLostOwnership()
    {
        ApplyOwnershipState();
    }

    public override void OnNetworkDespawn()
    {
        if (localControlPresentation != null)
        {
            localControlPresentation.SetLocalControl(false);
        }

        if (coopPlayerInputState != null)
        {
            coopPlayerInputState.ClearReplicatedVehicleInput();
        }
    }

    private void ApplyOwnershipState()
    {
        bool allowLocalControl = IsOwner && IsClient;

        if (localControlPresentation != null)
        {
            localControlPresentation.SetLocalControl(allowLocalControl);
        }

        if (!allowLocalControl && !IsServer && coopPlayerInputState != null)
        {
            coopPlayerInputState.ClearReplicatedVehicleInput();
        }
    }
}
