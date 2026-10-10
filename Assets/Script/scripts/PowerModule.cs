using UnityEngine;

public class PowerModule : MonoBehaviour
{
    public enum PowerTier
    {
        Weak,
        Medium,
        Strong
    }

    [SerializeField] private PowerTier powerTier = PowerTier.Weak;

    public PowerTier Tier => powerTier;

    public float PowerMultiplier
    {
        get
        {
            switch (powerTier)
            {
                case PowerTier.Medium:
                    return 2f;
                case PowerTier.Strong:
                    return 2.5f;
                default:
                    return 1.5f;
            }
        }
    }
}
