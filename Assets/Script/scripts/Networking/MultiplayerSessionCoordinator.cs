using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
#if MULTIPLAYER_SERVICES_SDK_INSTALLED
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
#endif
#if STEAMWORKS_NET
using Steamworks;
#endif

[DisallowMultipleComponent]
public class MultiplayerSessionCoordinator : MonoBehaviour
{
    private enum SessionMode
    {
        None = 0,
        Host = 1,
        Client = 2
    }

    private const string RuntimeConfigResourceName = "MultiplayerRuntimeConfig";
    private const string RuntimeRootName = "Goodboys Multiplayer Runtime";
    private const string NetworkRootName = "Goodboys Network Runtime";
    private const string LevelIndexSessionPropertyKey = "levelIndex";
    private const string LevelSceneSessionPropertyKey = "levelScene";

    public static MultiplayerSessionCoordinator Instance { get; private set; }

    [SerializeField] private MultiplayerRuntimeConfig runtimeConfig;

    private NetworkManager networkManager;
    private UnityTransport unityTransport;
    private readonly HashSet<ulong> positionedClientIds = new HashSet<ulong>();
    private string statusMessage = "Cok oyunculu menu hazir.";
    private string currentRoomCode = string.Empty;
    private bool isBusy;
    private bool pendingSpawnRefresh;
    private bool hasConsumedLaunchInvite;
    private SessionMode sessionMode = SessionMode.None;

#if MULTIPLAYER_SERVICES_SDK_INSTALLED
    private ISession activeSession;
    private IHostSession activeHostSession;
#endif

#if STEAMWORKS_NET
    private bool steamInitialized;
    private Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequestedCallback;
#endif

    public bool IsBusy => isBusy;

    public string StatusMessage => statusMessage;

    public string CurrentRoomCode => currentRoomCode;

    public bool IsNetworkSessionActive => networkManager != null && networkManager.IsListening;

    public bool IsHostSession => IsNetworkSessionActive && networkManager.IsHost;

    public static MultiplayerSessionCoordinator GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

        MultiplayerSessionCoordinator existing = FindFirstObjectByType<MultiplayerSessionCoordinator>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject runtimeRoot = new GameObject(RuntimeRootName);
        Instance = runtimeRoot.AddComponent<MultiplayerSessionCoordinator>();
        DontDestroyOnLoad(runtimeRoot);
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;

        if (runtimeConfig == null)
        {
            runtimeConfig = Resources.Load<MultiplayerRuntimeConfig>(RuntimeConfigResourceName);
        }

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;

        TryConsumeLaunchInvite();
    }

    private void Update()
    {
#if STEAMWORKS_NET
        if (steamInitialized)
        {
            SteamAPI.RunCallbacks();
        }
#endif

        if (pendingSpawnRefresh)
        {
            pendingSpawnRefresh = false;
            RefreshNetworkGameplayState();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
#if STEAMWORKS_NET
        if (steamInitialized)
        {
            SteamAPI.Shutdown();
            steamInitialized = false;
        }
#endif
    }

    public void StartHostingRoom()
    {
#if MULTIPLAYER_SERVICES_SDK_INSTALLED
        if (!CanRunSessionOperation())
        {
            return;
        }

        _ = StartHostingRoomAsync();
#else
        SetStatus("Unity Multiplayer Services henuz resolve edilmedi. Unity'yi bir kez acip package import'unu bitir.");
#endif
    }

    public void JoinRoomByCode(string roomCode)
    {
#if MULTIPLAYER_SERVICES_SDK_INSTALLED
        if (!CanRunSessionOperation())
        {
            return;
        }

        string sanitizedCode = SanitizeRoomCode(roomCode);
        if (string.IsNullOrEmpty(sanitizedCode))
        {
            SetStatus("Gecerli bir Room ID girmen lazim.");
            return;
        }

        _ = JoinRoomByCodeAsync(sanitizedCode);
#else
        SetStatus("Unity Multiplayer Services henuz resolve edilmedi. Unity'yi bir kez acip package import'unu bitir.");
#endif
    }

    public void InviteFriendViaSteam()
    {
        if (string.IsNullOrEmpty(currentRoomCode))
        {
            SetStatus("Once oda kurman lazim; kod olusmadan davet gonderemem.");
            return;
        }

#if STEAMWORKS_NET
        if (!EnsureSteamInitialized())
        {
            SetStatus("Steam overlay acilamadi. Editor icinde test ediyorsan Steam acik ve app id tanimli olmali.");
            return;
        }

        string connectString = runtimeConfig != null
            ? runtimeConfig.BuildRoomCodeConnectString(currentRoomCode)
            : $"goodboys_join_code={currentRoomCode}";

        SteamFriends.ActivateGameOverlayInviteDialogConnectString(connectString);
        SetStatus($"Steam daveti acildi. Giden baglanti kodu: {currentRoomCode}");
#else
        SetStatus("Steamworks.NET paketi resolve olunca Invite Friend butonu overlay uzerinden calisacak.");
#endif
    }

#if MULTIPLAYER_SERVICES_SDK_INSTALLED
    private async Task StartHostingRoomAsync()
    {
        isBusy = true;
        SetStatus("Oda kuruluyor...");

        try
        {
            await ShutdownActiveSessionAsync();
            EnsureRuntimeConfig();
            await EnsureServicesInitializedAsync();
            EnsureNetworkManager();
            TryInitializeSteamIfAvailable();

            SessionOptions options = new SessionOptions
            {
                Type = runtimeConfig.SessionType,
                Name = BuildSessionName(),
                MaxPlayers = 4,
                IsPrivate = true,
                IsLocked = false,
                SessionProperties = BuildCurrentLevelSessionProperties()
            }
            .WithRelayNetwork();

            activeHostSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            activeSession = activeHostSession;
            sessionMode = SessionMode.Host;
            currentRoomCode = SanitizeRoomCode(activeHostSession.Code);
            positionedClientIds.Clear();

            SetStatus(string.IsNullOrEmpty(currentRoomCode)
                ? "Oda acildi ama join kodu alinamadi."
                : $"Oda hazir. Room ID: {currentRoomCode}");

            TryLoadGameplaySceneAsHost();
        }
        catch (Exception exception)
        {
            SetStatus($"Oda kurulurken hata oldu: {exception.Message}");
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task JoinRoomByCodeAsync(string roomCode)
    {
        isBusy = true;
        SetStatus($"Odaya baglaniliyor: {roomCode}");

        try
        {
            await ShutdownActiveSessionAsync();
            EnsureRuntimeConfig();
            await EnsureServicesInitializedAsync();
            EnsureNetworkManager();
            TryInitializeSteamIfAvailable();

            JoinSessionOptions options = new JoinSessionOptions
            {
                Type = runtimeConfig.SessionType
            };

            activeSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(roomCode, options);
            activeHostSession = null;
            sessionMode = SessionMode.Client;
            currentRoomCode = SanitizeRoomCode(activeSession.Code);
            positionedClientIds.Clear();
            ApplySessionLevelMetadata(activeSession);

            SetStatus($"Odaya baglanildi. Room ID: {currentRoomCode}");
        }
        catch (Exception exception)
        {
            SetStatus($"Odaya baglanirken hata oldu: {exception.Message}");
        }
        finally
        {
            isBusy = false;
        }
    }

    private async Task EnsureServicesInitializedAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private async Task ShutdownActiveSessionAsync()
    {
        currentRoomCode = string.Empty;
        sessionMode = SessionMode.None;
        positionedClientIds.Clear();

        try
        {
            if (activeHostSession != null)
            {
                await activeHostSession.DeleteAsync();
            }
            else if (activeSession != null)
            {
                await activeSession.LeaveAsync();
            }
        }
        catch
        {
            // Session zaten kopmus olabilir; burada sessizce temizliyoruz.
        }
        finally
        {
            activeHostSession = null;
            activeSession = null;
        }

        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
            await Task.Yield();
        }
    }
#endif

    private bool CanRunSessionOperation()
    {
        LevelProgressionManager.EnsureInitialized();
        EnsureRuntimeConfig();
        if (runtimeConfig == null)
        {
            SetStatus("Multiplayer config bulunamadi. Assets/Resources/MultiplayerRuntimeConfig.asset eksik.");
            return false;
        }

        if (runtimeConfig.NetworkPlayerPrefab == null)
        {
            SetStatus("Network player prefab bagli degil. MultiplayerRuntimeConfig icindeki prefab referansini kontrol et.");
            return false;
        }

        return !isBusy;
    }

    private void EnsureRuntimeConfig()
    {
        if (runtimeConfig == null)
        {
            runtimeConfig = Resources.Load<MultiplayerRuntimeConfig>(RuntimeConfigResourceName);
        }
    }

    private void EnsureNetworkManager()
    {
        if (networkManager != null)
        {
            ConfigureNetworkManager();
            return;
        }

        GameObject networkRoot = new GameObject(NetworkRootName);
        networkRoot.transform.SetParent(transform, false);
        DontDestroyOnLoad(networkRoot);

        unityTransport = networkRoot.AddComponent<UnityTransport>();
        networkManager = networkRoot.AddComponent<NetworkManager>();
        ConfigureNetworkManager();

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.SceneManager.OnSceneEvent -= HandleSceneEvent;
        networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
    }

    private void ConfigureNetworkManager()
    {
        EnsureRuntimeConfig();
        if (networkManager == null || runtimeConfig == null)
        {
            return;
        }

        networkManager.NetworkConfig.NetworkTransport = unityTransport;
        networkManager.NetworkConfig.PlayerPrefab = runtimeConfig.NetworkPlayerPrefab;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        networkManager.NetworkConfig.ConnectionApproval = false;
        networkManager.NetworkConfig.ForceSamePrefabs = false;
        networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = true;
    }

    private void TryLoadGameplaySceneAsHost()
    {
        if (runtimeConfig == null || networkManager == null || !networkManager.IsHost)
        {
            return;
        }

        int selectedLevel = LevelProgressionManager.GetSelectedLevelNumber();
        string targetSceneName = LevelProgressionManager.GetLevelSceneName(selectedLevel, runtimeConfig.GameplaySceneName);
        LevelProgressionManager.SetActiveLevel(selectedLevel);

        if (SceneManager.GetActiveScene().name == targetSceneName)
        {
            pendingSpawnRefresh = true;
            return;
        }

        SceneEventProgressStatus sceneStatus = networkManager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
        if (sceneStatus != SceneEventProgressStatus.Started)
        {
            SetStatus($"Oda acildi ama sahne gecisi baslatilamadi: {sceneStatus}");
        }
    }

    private void HandleSceneEvent(SceneEvent sceneEvent)
    {
        if (runtimeConfig == null)
        {
            return;
        }

        if ((sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted ||
             sceneEvent.SceneEventType == SceneEventType.SynchronizeComplete) &&
            LevelProgressionManager.IsLevelScene(SceneManager.GetActiveScene().name, runtimeConfig.GameplaySceneName))
        {
            pendingSpawnRefresh = true;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (runtimeConfig == null || networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        if (LevelProgressionManager.IsLevelScene(scene.name, runtimeConfig.GameplaySceneName))
        {
            LevelProgressionManager.EnsureActiveLevelForScene(scene.name);
            positionedClientIds.Clear();
            pendingSpawnRefresh = true;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (runtimeConfig == null || networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (LevelProgressionManager.IsLevelScene(SceneManager.GetActiveScene().name, runtimeConfig.GameplaySceneName))
        {
            pendingSpawnRefresh = true;
        }
    }

    private void RefreshNetworkGameplayState()
    {
        if (runtimeConfig == null || networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        if (!LevelProgressionManager.IsLevelScene(SceneManager.GetActiveScene().name, runtimeConfig.GameplaySceneName))
        {
            return;
        }

        Vector3 spawnAnchorPosition;
        Quaternion spawnAnchorRotation;
        TryGetOfflineScenePlayerAnchor(out spawnAnchorPosition, out spawnAnchorRotation);
        DisableOfflineScenePlayers();

        if (!networkManager.IsServer)
        {
            return;
        }

        List<NetworkObject> playerObjects = new List<NetworkObject>();
        foreach (NetworkClient client in networkManager.ConnectedClientsList.OrderBy(client => client.ClientId))
        {
            if (client.PlayerObject != null)
            {
                playerObjects.Add(client.PlayerObject);
            }
        }

        for (int index = 0; index < playerObjects.Count; index++)
        {
            NetworkObject playerObject = playerObjects[index];
            if (playerObject == null || positionedClientIds.Contains(playerObject.OwnerClientId))
            {
                continue;
            }

            Vector3 offset = new Vector3(
                runtimeConfig.PlayerSpawnSpacing.x * index,
                runtimeConfig.PlayerSpawnSpacing.y * index,
                runtimeConfig.PlayerSpawnSpacing.z * index);

            Vector3 targetPosition = spawnAnchorPosition + spawnAnchorRotation * offset;
            MovePlayerObject(playerObject, targetPosition, spawnAnchorRotation);
            positionedClientIds.Add(playerObject.OwnerClientId);
        }
    }

    private void MovePlayerObject(NetworkObject playerObject, Vector3 targetPosition, Quaternion targetRotation)
    {
        if (playerObject == null)
        {
            return;
        }

        CharacterController characterController = playerObject.GetComponent<CharacterController>();
        bool restoreCharacterController = characterController != null && characterController.enabled;
        if (restoreCharacterController)
        {
            characterController.enabled = false;
        }

        playerObject.transform.SetPositionAndRotation(targetPosition, targetRotation);

        if (restoreCharacterController)
        {
            characterController.enabled = true;
        }
    }

    private void DisableOfflineScenePlayers()
    {
        foreach (PlayerController playerController in Resources.FindObjectsOfTypeAll<PlayerController>())
        {
            if (playerController == null)
            {
                continue;
            }

            GameObject playerObject = playerController.gameObject;
            if (!playerObject.scene.IsValid() || !playerObject.scene.isLoaded || playerObject.scene != SceneManager.GetActiveScene())
            {
                continue;
            }

            if (playerObject.GetComponent<NetworkObject>() != null)
            {
                continue;
            }

            if (playerObject.activeSelf)
            {
                playerObject.SetActive(false);
            }
        }
    }

    private bool TryGetOfflineScenePlayerAnchor(out Vector3 position, out Quaternion rotation)
    {
        foreach (PlayerController playerController in Resources.FindObjectsOfTypeAll<PlayerController>())
        {
            if (playerController == null)
            {
                continue;
            }

            GameObject playerObject = playerController.gameObject;
            if (!playerObject.scene.IsValid() || !playerObject.scene.isLoaded || playerObject.scene != SceneManager.GetActiveScene())
            {
                continue;
            }

            if (playerObject.GetComponent<NetworkObject>() != null)
            {
                continue;
            }

            position = playerObject.transform.position;
            rotation = playerObject.transform.rotation;
            return true;
        }

        position = Vector3.zero;
        rotation = Quaternion.identity;
        return false;
    }

    private void TryConsumeLaunchInvite()
    {
        if (hasConsumedLaunchInvite)
        {
            return;
        }

        hasConsumedLaunchInvite = true;
        string launchText = string.Join(" ", Environment.GetCommandLineArgs());

#if STEAMWORKS_NET
        if (EnsureSteamInitialized())
        {
            if (SteamApps.GetLaunchCommandLine(out string launchCommand, 2048) > 0)
            {
                launchText += " " + launchCommand;
            }
        }
#endif

        string inviteCode = ExtractRoomCodeFromText(launchText);
        if (!string.IsNullOrEmpty(inviteCode))
        {
            JoinRoomByCode(inviteCode);
        }
    }

    private bool TryInitializeSteamIfAvailable()
    {
#if STEAMWORKS_NET
        return EnsureSteamInitialized();
#else
        return false;
#endif
    }

    private string ExtractRoomCodeFromText(string rawText)
    {
        EnsureRuntimeConfig();
        if (runtimeConfig == null || string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        string prefix = runtimeConfig.RoomCodeArgumentKey + "=";
        string[] tokens = rawText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            int prefixIndex = token.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex < 0)
            {
                continue;
            }

            string value = token.Substring(prefixIndex + prefix.Length).Trim('\"', '\'', ';', ',');
            string sanitizedCode = SanitizeRoomCode(value);
            if (!string.IsNullOrEmpty(sanitizedCode))
            {
                return sanitizedCode;
            }
        }

        return string.Empty;
    }

    private string BuildSessionName()
    {
        return $"goodboys-{Guid.NewGuid().ToString("N").Substring(0, 6)}";
    }

    public bool TryLoadNetworkLevel(int levelNumber, string sceneName)
    {
        EnsureRuntimeConfig();
        if (networkManager == null || !networkManager.IsListening)
        {
            return false;
        }

        if (!networkManager.IsHost)
        {
            SetStatus("Sadece host sonraki gorevi baslatabilir.");
            return false;
        }

        LevelProgressionManager.SetActiveLevel(levelNumber);
#if MULTIPLAYER_SERVICES_SDK_INSTALLED
        _ = SaveCurrentLevelMetadataAsync(levelNumber, sceneName);
#endif
        Time.timeScale = 1f;

        if (SceneManager.GetActiveScene().name == sceneName)
        {
            pendingSpawnRefresh = true;
            return true;
        }

        SceneEventProgressStatus sceneStatus = networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        bool started = sceneStatus == SceneEventProgressStatus.Started;
        if (!started)
        {
            SetStatus($"Level yuklenemedi: {sceneStatus}");
        }

        return started;
    }

    public void LeaveToMainMenu(string menuSceneName)
    {
#if MULTIPLAYER_SERVICES_SDK_INSTALLED
        _ = LeaveToMainMenuAsync(menuSceneName);
#else
        Time.timeScale = 1f;
        SceneManager.LoadScene(menuSceneName);
#endif
    }

    private string SanitizeRoomCode(string roomCode)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
        {
            return string.Empty;
        }

        return new string(roomCode
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray())
            .ToUpperInvariant();
    }

    private void SetStatus(string message)
    {
        statusMessage = string.IsNullOrWhiteSpace(message) ? "Hazir." : message;
        Debug.Log($"[MultiplayerSessionCoordinator] {statusMessage}");
    }

#if MULTIPLAYER_SERVICES_SDK_INSTALLED
    private Dictionary<string, SessionProperty> BuildCurrentLevelSessionProperties()
    {
        int selectedLevel = Mathf.Max(1, LevelProgressionManager.GetSelectedLevelNumber());
        string sceneName = LevelProgressionManager.GetLevelSceneName(selectedLevel, runtimeConfig != null ? runtimeConfig.GameplaySceneName : "level2");

        LevelProgressionManager.SetActiveLevel(selectedLevel);

        return new Dictionary<string, SessionProperty>
        {
            { LevelIndexSessionPropertyKey, new SessionProperty(selectedLevel.ToString()) },
            { LevelSceneSessionPropertyKey, new SessionProperty(sceneName) }
        };
    }

    private void ApplySessionLevelMetadata(ISession session)
    {
        if (session == null || session.Properties == null)
        {
            return;
        }

        if (session.Properties.TryGetValue(LevelIndexSessionPropertyKey, out SessionProperty levelIndexProperty) &&
            int.TryParse(levelIndexProperty.Value, out int levelNumber))
        {
            LevelProgressionManager.SelectLevel(levelNumber);
            LevelProgressionManager.SetActiveLevel(levelNumber);
        }
    }

    private async Task SaveCurrentLevelMetadataAsync(int levelNumber, string sceneName)
    {
        if (activeHostSession == null)
        {
            return;
        }

        try
        {
            activeHostSession.SetProperty(LevelIndexSessionPropertyKey, new SessionProperty(levelNumber.ToString()));
            activeHostSession.SetProperty(LevelSceneSessionPropertyKey, new SessionProperty(sceneName));
            await activeHostSession.SavePropertiesAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Level bilgisi kaydedilemedi: {exception.Message}");
        }
    }

    private async Task LeaveToMainMenuAsync(string menuSceneName)
    {
        await ShutdownActiveSessionAsync();
        Time.timeScale = 1f;
        SceneManager.LoadScene(menuSceneName);
    }
#endif

#if STEAMWORKS_NET
    private bool EnsureSteamInitialized()
    {
        if (steamInitialized)
        {
            return true;
        }

        try
        {
            steamInitialized = SteamAPI.Init();
            if (steamInitialized)
            {
                richPresenceJoinRequestedCallback = Callback<GameRichPresenceJoinRequested_t>.Create(HandleRichPresenceJoinRequested);
            }

            return steamInitialized;
        }
        catch (Exception)
        {
            steamInitialized = false;
            return false;
        }
    }

    private void HandleRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback)
    {
        string inviteCode = ExtractRoomCodeFromText(callback.m_rgchConnect);
        if (!string.IsNullOrEmpty(inviteCode))
        {
            JoinRoomByCode(inviteCode);
        }
    }
#endif
}
