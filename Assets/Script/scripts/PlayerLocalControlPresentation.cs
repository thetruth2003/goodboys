using UnityEngine;

public class PlayerLocalControlPresentation : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;
    [SerializeField] private ObjectGrabber objectGrabber;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Camera[] playerCameras = System.Array.Empty<Camera>();
    [SerializeField] private AudioListener[] audioListeners = System.Array.Empty<AudioListener>();

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (objectGrabber == null)
        {
            objectGrabber = GetComponent<ObjectGrabber>();
        }

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (playerCameras == null || playerCameras.Length == 0)
        {
            playerCameras = GetComponentsInChildren<Camera>(true);
        }

        if (audioListeners == null || audioListeners.Length == 0)
        {
            audioListeners = GetComponentsInChildren<AudioListener>(true);
        }
    }

    public void SetLocalControl(bool enabled)
    {
        if (playerController != null)
        {
            playerController.SetLocalInputEnabled(enabled);
        }

        if (objectGrabber != null)
        {
            objectGrabber.enabled = enabled;
        }

        if (characterController != null)
        {
            characterController.enabled = enabled;
        }

        foreach (Camera playerCamera in playerCameras)
        {
            if (playerCamera != null)
            {
                playerCamera.enabled = enabled;
            }
        }

        foreach (AudioListener audioListener in audioListeners)
        {
            if (audioListener != null)
            {
                audioListener.enabled = enabled;
            }
        }
    }
}
