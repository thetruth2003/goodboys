using UnityEngine;

public class VehicleDriveLever : MonoBehaviour
{
    [SerializeField] private ModularVehicleController targetVehicle;
    [SerializeField] private bool leverUpStartsForward = true;

    public bool IsLeverUp { get; private set; } = true;

    private void Awake()
    {
        if (targetVehicle == null)
        {
            targetVehicle = GetComponentInParent<ModularVehicleController>();
        }

        IsLeverUp = leverUpStartsForward;
        ApplyMode();
    }

    public void SetLeverUp()
    {
        IsLeverUp = true;
        ApplyMode();
    }

    public void SetLeverDown()
    {
        IsLeverUp = false;
        ApplyMode();
    }

    public void ToggleLever()
    {
        IsLeverUp = !IsLeverUp;
        ApplyMode();
    }

    private void ApplyMode()
    {
        if (targetVehicle == null)
        {
            targetVehicle = GetComponentInParent<ModularVehicleController>();
        }

        if (targetVehicle == null)
        {
            return;
        }

        targetVehicle.SetDriveMode(IsLeverUp);
    }
}
