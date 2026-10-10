using System;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelCatalog", menuName = "Goodboys/Level Catalog")]
public class LevelCatalog : ScriptableObject
{
    [Serializable]
    public class LevelEntry
    {
        [SerializeField] private string displayName = "Level";
        [SerializeField] private string sceneName = "level1";
        [TextArea(2, 4)]
        [SerializeField] private string description = "Yeni gorev seni bekliyor.";

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Level" : displayName.Trim();

        public string SceneName => string.IsNullOrWhiteSpace(sceneName) ? "level1" : sceneName.Trim();

        public string Description => string.IsNullOrWhiteSpace(description) ? "Yeni gorev seni bekliyor." : description.Trim();
    }

    [SerializeField] private LevelEntry[] levels = Array.Empty<LevelEntry>();

    public int LevelCount => levels?.Length ?? 0;

    public LevelEntry GetLevel(int levelNumber)
    {
        int index = levelNumber - 1;
        if (levels == null || index < 0 || index >= levels.Length)
        {
            return null;
        }

        return levels[index];
    }
}
