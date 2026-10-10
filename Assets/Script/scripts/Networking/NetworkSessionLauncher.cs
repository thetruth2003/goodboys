using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkSessionLauncher : MonoBehaviour
{
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameObject networkPlayerPrefab;
    [SerializeField] private GameObject offlineLocalPlayerRoot;
    [SerializeField] private string connectAddress = "127.0.0.1";
    [SerializeField] private ushort connectPort = 7777;
    [SerializeField] private bool drawDebugGui = true;

    private UnityTransport unityTransport;

    private void Awake()
    {
        Application.runInBackground = true;
        ResolveReferences();
    }

    public void StartHost()
    {
        if (!PrepareForSessionStart())
        {
            return;
        }

        SetOfflineLocalPlayerActive(false);
        networkManager.StartHost();
    }

    public void StartClient()
    {
        if (!PrepareForSessionStart())
        {
            return;
        }

        SetOfflineLocalPlayerActive(false);
        networkManager.StartClient();
    }

    public void StopSession()
    {
        if (networkManager == null || !networkManager.IsListening)
        {
            SetOfflineLocalPlayerActive(true);
            return;
        }

        networkManager.Shutdown();
        SetOfflineLocalPlayerActive(true);
    }

    private void OnGUI()
    {
        if (!drawDebugGui)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(20f, 20f, 240f, 180f), GUI.skin.box);
        GUILayout.Label("Online Test");
        GUILayout.Label("IP / Host");
        connectAddress = GUILayout.TextField(connectAddress ?? string.Empty);
        GUILayout.Label("Port: " + connectPort);

        GUI.enabled = networkManager == null || !networkManager.IsListening;
        if (GUILayout.Button("Host Baslat"))
        {
            StartHost();
        }

        if (GUILayout.Button("Client Baglan"))
        {
            StartClient();
        }

        GUI.enabled = networkManager != null && networkManager.IsListening;
        if (GUILayout.Button("Baglantiyi Kapat"))
        {
            StopSession();
        }

        GUI.enabled = true;
        GUILayout.EndArea();
    }

    private bool PrepareForSessionStart()
    {
        ResolveReferences();

        if (networkManager == null || unityTransport == null)
        {
            Debug.LogWarning("NetworkSessionLauncher icin NetworkManager veya UnityTransport bulunamadi.");
            return false;
        }

        if (networkPlayerPrefab != null)
        {
            networkManager.NetworkConfig.PlayerPrefab = networkPlayerPrefab;
        }

        if (networkManager.NetworkConfig.PlayerPrefab == null)
        {
            Debug.LogWarning("NetworkManager PlayerPrefab bos. NetworkObject olan oyuncu prefabini baglaman gerekiyor.");
            return false;
        }

        if (networkManager.IsListening)
        {
            return false;
        }

        unityTransport.SetConnectionData(connectAddress, connectPort);
        return true;
    }

    private void ResolveReferences()
    {
        if (networkManager == null)
        {
            networkManager = FindFirstObjectByType<NetworkManager>();
        }

        if (networkManager != null && unityTransport == null)
        {
            unityTransport = networkManager.GetComponent<UnityTransport>();
        }

        if (offlineLocalPlayerRoot == null)
        {
            PlayerController playerController = FindFirstObjectByType<PlayerController>();
            if (playerController != null)
            {
                offlineLocalPlayerRoot = playerController.gameObject;
            }
        }
    }

    private void SetOfflineLocalPlayerActive(bool active)
    {
        if (offlineLocalPlayerRoot != null)
        {
            offlineLocalPlayerRoot.SetActive(active);
        }
    }
}
