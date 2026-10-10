using UnityEngine;

public class VehicleSeat : MonoBehaviour
{
    public enum SeatRole
    {
        Driver = 0,
        Passenger = 1,
        SteeringOperator = 2,
        ThrottleOperator = 3
    }

    [SerializeField] private SeatRole seatRole = SeatRole.Passenger;
    [SerializeField] private Transform seatPoint;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private bool useSeatPointForExit = true;
    [SerializeField] private Vector3 seatLocalPosition = new Vector3(0f, 1.05f, 0f);
    [SerializeField] private Vector3 seatLocalEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 exitLocalOffset = new Vector3(1.2f, 0.15f, -0.9f);
    [SerializeField] private float interactionRadius = 2.75f;
    [SerializeField] private bool drawSeatGizmos = true;

    private PlayerController occupant;
    private ModularVehicleController cachedVehicleController;

    public bool IsDriverSeat => seatRole == SeatRole.Driver;

    public bool CanControlSteering => seatRole == SeatRole.Driver || seatRole == SeatRole.SteeringOperator;

    public bool CanControlThrottle => seatRole == SeatRole.Driver || seatRole == SeatRole.ThrottleOperator;

    public bool IsSpecializedControlSeat => seatRole == SeatRole.SteeringOperator || seatRole == SeatRole.ThrottleOperator;

    public bool IsOccupied => occupant != null;

    public PlayerController Occupant => occupant;

    public float InteractionRadius => Mathf.Max(0.5f, interactionRadius);

    public ModularVehicleController VehicleController
    {
        get
        {
            if (cachedVehicleController == null)
            {
                cachedVehicleController = GetComponentInParent<ModularVehicleController>(true);
            }

            return cachedVehicleController;
        }
    }

    public bool CanBeEnteredBy(PlayerController player)
    {
        return player != null &&
               !IsOccupied &&
               gameObject.activeInHierarchy &&
               VehicleController != null;
    }

    public Vector3 GetSeatWorldPosition()
    {
        if (seatPoint != null)
        {
            return seatPoint.position;
        }

        return transform.TransformPoint(seatLocalPosition);
    }

    public Quaternion GetSeatWorldRotation()
    {
        if (seatPoint != null)
        {
            return seatPoint.rotation;
        }

        return transform.rotation * Quaternion.Euler(seatLocalEulerAngles);
    }

    public Vector3 GetExitWorldPosition()
    {
        if (useSeatPointForExit)
        {
            return GetSeatWorldPosition();
        }

        if (exitPoint != null)
        {
            return exitPoint.position;
        }

        return transform.TransformPoint(exitLocalOffset);
    }

    public void EnterSeat(PlayerController player)
    {
        occupant = player;
    }

    public void ExitSeat(PlayerController player)
    {
        if (occupant == player)
        {
            occupant = null;
        }
    }

    public void AttachPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        playerTransform.SetPositionAndRotation(GetSeatWorldPosition(), GetSeatWorldRotation());
    }

    private void OnDisable()
    {
        occupant = null;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawSeatGizmos)
        {
            return;
        }

        Vector3 seatPosition = GetSeatWorldPosition();
        Vector3 exitPosition = GetExitWorldPosition();

        Color previousColor = Gizmos.color;

        Gizmos.color = new Color(0.15f, 0.5f, 1f, 0.9f);
        Gizmos.DrawSphere(seatPosition, 0.12f);
        Gizmos.DrawLine(transform.position, seatPosition);

        Gizmos.color = new Color(0.2f, 1f, 0.45f, 0.9f);
        Gizmos.DrawWireSphere(seatPosition, InteractionRadius);

        Gizmos.color = new Color(1f, 0.7f, 0.15f, 0.9f);
        Gizmos.DrawSphere(exitPosition, 0.1f);
        Gizmos.DrawLine(seatPosition, exitPosition);

        Gizmos.color = previousColor;
    }
}
