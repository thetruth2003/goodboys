using UnityEngine;

[CreateAssetMenu(fileName = "MultiplayerRuntimeConfig", menuName = "Goodboys/Multiplayer Runtime Config")]
public class MultiplayerRuntimeConfig : ScriptableObject
{
    private const string DefaultSessionType = "goodboys-room";
    private const string DefaultGameplayScene = "level1";
    private const string DefaultRoomCodeKey = "goodboys_join_code";

    [SerializeField] private string sessionType = DefaultSessionType;
    [SerializeField] private string gameplaySceneName = DefaultGameplayScene;
    [SerializeField] private GameObject networkPlayerPrefab;
    [SerializeField] private Vector3 playerSpawnSpacing = new Vector3(2.75f, 0f, 0f);
    [SerializeField] private string roomCodeArgumentKey = DefaultRoomCodeKey;

    public string SessionType => string.IsNullOrWhiteSpace(sessionType) ? DefaultSessionType : sessionType.Trim();

    public string GameplaySceneName => string.IsNullOrWhiteSpace(gameplaySceneName) ? DefaultGameplayScene : gameplaySceneName.Trim();

    public GameObject NetworkPlayerPrefab => networkPlayerPrefab;

    public Vector3 PlayerSpawnSpacing => playerSpawnSpacing;

    public string RoomCodeArgumentKey => string.IsNullOrWhiteSpace(roomCodeArgumentKey) ? DefaultRoomCodeKey : roomCodeArgumentKey.Trim();

    public string BuildRoomCodeConnectString(string roomCode)
    {
        return $"{RoomCodeArgumentKey}={roomCode}";
    }
}
