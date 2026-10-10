using UnityEngine;
using UnityEngine.SceneManagement;

public static class LevelProgressionManager
{
    public struct CompletionResult
    {
        public int CompletedLevelNumber;
        public bool CompletedForFirstTime;
        public bool UnlockedNextLevel;
        public bool HasNextLevel;
        public int NextLevelNumber;
    }

    private const string LevelCatalogResourceName = "LevelCatalog";
    private const string HighestUnlockedLevelKey = "goodboys.level.highestUnlocked";
    private const string CompletedLevelsMaskKey = "goodboys.level.completedMask";
    private const string SelectedLevelKey = "goodboys.level.selected";
    private const string MainMenuSceneName = "00 - MainMenu";

    private static LevelCatalog cachedCatalog;
    private static int activeLevelNumber = -1;

    public static LevelCatalog Catalog
    {
        get
        {
            if (cachedCatalog == null)
            {
                cachedCatalog = Resources.Load<LevelCatalog>(LevelCatalogResourceName);
            }

            return cachedCatalog;
        }
    }

    public static int TotalLevels => Mathf.Max(0, Catalog != null ? Catalog.LevelCount : 0);

    public static int HighestUnlockedLevel
    {
        get
        {
            if (TotalLevels <= 0)
            {
                return 0;
            }

            int savedValue = PlayerPrefs.GetInt(HighestUnlockedLevelKey, 1);
            return Mathf.Clamp(savedValue, 1, TotalLevels);
        }
    }

    public static void EnsureInitialized()
    {
        if (TotalLevels <= 0)
        {
            return;
        }

        bool changed = false;
        int highestUnlocked = Mathf.Clamp(PlayerPrefs.GetInt(HighestUnlockedLevelKey, 1), 1, TotalLevels);
        while (highestUnlocked < TotalLevels && IsCompleted(highestUnlocked))
        {
            highestUnlocked++;
        }

        if (PlayerPrefs.GetInt(HighestUnlockedLevelKey, 0) != highestUnlocked)
        {
            PlayerPrefs.SetInt(HighestUnlockedLevelKey, highestUnlocked);
            changed = true;
        }

        int selectedLevel = PlayerPrefs.GetInt(SelectedLevelKey, 1);
        if (selectedLevel <= 0 || selectedLevel > highestUnlocked)
        {
            PlayerPrefs.SetInt(SelectedLevelKey, highestUnlocked);
            changed = true;
        }

        if (changed)
        {
            PlayerPrefs.Save();
        }
    }

    public static bool IsUnlocked(int levelNumber)
    {
        return levelNumber >= 1 &&
               levelNumber <= TotalLevels &&
               levelNumber <= HighestUnlockedLevel;
    }

    public static bool IsCompleted(int levelNumber)
    {
        if (levelNumber < 1 || levelNumber > 31)
        {
            return false;
        }

        int mask = PlayerPrefs.GetInt(CompletedLevelsMaskKey, 0);
        int bit = 1 << (levelNumber - 1);
        return (mask & bit) != 0;
    }

    public static void SelectLevel(int levelNumber)
    {
        if (!IsUnlocked(levelNumber))
        {
            return;
        }

        PlayerPrefs.SetInt(SelectedLevelKey, levelNumber);
        PlayerPrefs.Save();
    }

    public static int GetSelectedLevelNumber()
    {
        EnsureInitialized();
        if (TotalLevels <= 0)
        {
            return 0;
        }

        int selectedLevel = PlayerPrefs.GetInt(SelectedLevelKey, 1);
        if (!IsUnlocked(selectedLevel))
        {
            selectedLevel = HighestUnlockedLevel;
            PlayerPrefs.SetInt(SelectedLevelKey, selectedLevel);
        }

        return Mathf.Clamp(selectedLevel, 1, TotalLevels);
    }

    public static int GetActiveLevelNumber()
    {
        if (activeLevelNumber > 0)
        {
            return activeLevelNumber;
        }

        int selectedLevel = GetSelectedLevelNumber();
        activeLevelNumber = selectedLevel;
        return activeLevelNumber;
    }

    public static void SetActiveLevel(int levelNumber)
    {
        if (levelNumber < 1 || levelNumber > TotalLevels)
        {
            return;
        }

        activeLevelNumber = levelNumber;
        PlayerPrefs.SetInt(SelectedLevelKey, levelNumber);
        PlayerPrefs.Save();
    }

    public static void EnsureActiveLevelForScene(string sceneName)
    {
        if (activeLevelNumber > 0)
        {
            return;
        }

        int selectedLevel = GetSelectedLevelNumber();
        LevelCatalog.LevelEntry selectedEntry = GetLevel(selectedLevel);
        if (selectedEntry != null && selectedEntry.SceneName == sceneName)
        {
            activeLevelNumber = selectedLevel;
            return;
        }

        for (int levelNumber = 1; levelNumber <= TotalLevels; levelNumber++)
        {
            LevelCatalog.LevelEntry entry = GetLevel(levelNumber);
            if (entry != null && entry.SceneName == sceneName)
            {
                activeLevelNumber = levelNumber;
                return;
            }
        }

        activeLevelNumber = Mathf.Clamp(selectedLevel, 1, Mathf.Max(1, TotalLevels));
    }

    public static LevelCatalog.LevelEntry GetLevel(int levelNumber)
    {
        return Catalog != null ? Catalog.GetLevel(levelNumber) : null;
    }

    public static string GetLevelDisplayName(int levelNumber)
    {
        LevelCatalog.LevelEntry entry = GetLevel(levelNumber);
        return entry != null ? entry.DisplayName : $"Level {levelNumber}";
    }

    public static string GetLevelDescription(int levelNumber)
    {
        LevelCatalog.LevelEntry entry = GetLevel(levelNumber);
        return entry != null ? entry.Description : "Yeni gorev seni bekliyor.";
    }

    public static string GetLevelSceneName(int levelNumber, string fallbackSceneName)
    {
        LevelCatalog.LevelEntry entry = GetLevel(levelNumber);
        if (entry != null && !string.IsNullOrWhiteSpace(entry.SceneName))
        {
            return entry.SceneName;
        }

        return fallbackSceneName;
    }

    public static bool IsLevelScene(string sceneName, string fallbackSceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        for (int levelNumber = 1; levelNumber <= TotalLevels; levelNumber++)
        {
            if (GetLevelSceneName(levelNumber, fallbackSceneName) == sceneName)
            {
                return true;
            }
        }

        return false;
    }

    public static bool StartSelectedSingleplayerLevel(string fallbackSceneName)
    {
        return StartLevel(GetSelectedLevelNumber(), fallbackSceneName);
    }

    public static bool RestartActiveLevel()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        EnsureActiveLevelForScene(currentSceneName);

        int levelNumber = GetActiveLevelNumber();
        if (levelNumber >= 1 && levelNumber <= TotalLevels)
        {
            return StartLevel(levelNumber, currentSceneName);
        }

        if (string.IsNullOrWhiteSpace(currentSceneName))
        {
            return false;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(currentSceneName);
        return true;
    }

    public static bool StartLevel(int levelNumber, string fallbackSceneName)
    {
        if (!IsUnlocked(levelNumber))
        {
            return false;
        }

        string sceneName = GetLevelSceneName(levelNumber, fallbackSceneName);
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        SelectLevel(levelNumber);
        SetActiveLevel(levelNumber);
        Time.timeScale = 1f;

        MultiplayerSessionCoordinator coordinator = MultiplayerSessionCoordinator.Instance;
        if (coordinator != null && coordinator.IsNetworkSessionActive)
        {
            return coordinator.TryLoadNetworkLevel(levelNumber, sceneName);
        }

        SceneManager.LoadScene(sceneName);
        return true;
    }

    public static CompletionResult CompleteActiveLevel()
    {
        EnsureInitialized();
        int levelNumber = Mathf.Clamp(GetActiveLevelNumber(), 1, Mathf.Max(1, TotalLevels));

        CompletionResult result = new CompletionResult
        {
            CompletedLevelNumber = levelNumber,
            HasNextLevel = levelNumber < TotalLevels,
            NextLevelNumber = levelNumber < TotalLevels ? levelNumber + 1 : levelNumber
        };

        if (levelNumber >= 1 && levelNumber <= 31)
        {
            int mask = PlayerPrefs.GetInt(CompletedLevelsMaskKey, 0);
            int bit = 1 << (levelNumber - 1);
            result.CompletedForFirstTime = (mask & bit) == 0;
            mask |= bit;
            PlayerPrefs.SetInt(CompletedLevelsMaskKey, mask);
        }

        if (result.HasNextLevel && HighestUnlockedLevel < result.NextLevelNumber)
        {
            PlayerPrefs.SetInt(HighestUnlockedLevelKey, result.NextLevelNumber);
            result.UnlockedNextLevel = true;
        }

        int resumeLevel = result.HasNextLevel
            ? Mathf.Max(result.NextLevelNumber, HighestUnlockedLevel)
            : levelNumber;
        PlayerPrefs.SetInt(SelectedLevelKey, Mathf.Clamp(resumeLevel, 1, Mathf.Max(1, TotalLevels)));

        PlayerPrefs.Save();
        return result;
    }

    public static void ReturnToMainMenu()
    {
        activeLevelNumber = -1;
        Time.timeScale = 1f;

        MultiplayerSessionCoordinator coordinator = MultiplayerSessionCoordinator.Instance;
        if (coordinator != null && coordinator.IsNetworkSessionActive)
        {
            coordinator.LeaveToMainMenu(MainMenuSceneName);
            return;
        }

        SceneManager.LoadScene(MainMenuSceneName);
    }
}
