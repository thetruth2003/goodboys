using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
    private enum MenuPage
    {
        Root = 0,
        Multiplayer = 1
    }

    [SerializeField] private string gameSceneName = "level1";
    [SerializeField] private string gameTitle = "GOODBOYS";
    [SerializeField] private string gameSubtitle = "Aracini kur, koltuga gec ve parkuru bitir.";
    [SerializeField] private string multiplayerButtonLabel = "Cok Oyuncu";
    [SerializeField] private string startButtonLabel = "Tek Oyuncu";
    [SerializeField] private string quitButtonLabel = "Cikis";

    private Texture2D solidTexture;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle sectionStyle;
    private GUIStyle bodyStyle;
    private GUIStyle hintStyle;
    private GUIStyle buttonStyle;
    private GUIStyle secondaryButtonStyle;
    private GUIStyle disabledButtonStyle;
    private GUIStyle levelButtonStyle;
    private GUIStyle selectedLevelButtonStyle;
    private GUIStyle completedLevelButtonStyle;
    private GUIStyle lockedLevelButtonStyle;
    private GUIStyle panelStyle;
    private GUIStyle chipStyle;
    private GUIStyle textFieldStyle;

    private MultiplayerSessionCoordinator multiplayerCoordinator;
    private MenuPage currentPage;
    private string joinCodeInput = string.Empty;

    private void Awake()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        multiplayerCoordinator = MultiplayerSessionCoordinator.GetOrCreate();
        LevelProgressionManager.EnsureInitialized();
    }

    private void Update()
    {
        if (currentPage == MenuPage.Root &&
            (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
        {
            StartGame();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (currentPage == MenuPage.Multiplayer)
            {
                currentPage = MenuPage.Root;
            }
            else
            {
                QuitGame();
            }
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1600f, Screen.height / 900f), 0.72f, 1.2f);
        float contentWidth = Mathf.Min(Screen.width - 80f, 1120f * scale);
        float contentHeight = Mathf.Min(Screen.height - 80f, 620f * scale);
        Rect contentRect = new Rect(
            (Screen.width - contentWidth) * 0.5f,
            (Screen.height - contentHeight) * 0.5f,
            contentWidth,
            contentHeight);

        ApplyResponsiveSizing(scale);
        DrawBackground(scale);
        DrawBackdropGlow(contentRect, scale);

        float leftWidth = contentRect.width * 0.58f;
        float gap = 24f * scale;
        Rect leftPanelRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
        Rect rightPanelRect = new Rect(contentRect.x + leftWidth + gap, contentRect.y + 34f * scale, contentRect.width - leftWidth - gap, contentRect.height - 68f * scale);

        DrawHeroPanel(leftPanelRect, scale);
        DrawInfoPanel(rightPanelRect, scale);
    }

    private void StartGame()
    {
        LevelProgressionManager.StartSelectedSingleplayerLevel(gameSceneName);
    }

    private void QuitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void EnsureStyles()
    {
        if (solidTexture == null)
        {
            solidTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            solidTexture.SetPixel(0, 0, Color.white);
            solidTexture.Apply();
        }

        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { textColor = Color.white }
        };

        subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { textColor = new Color(0.85f, 0.9f, 0.95f, 0.92f) }
        };

        sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
            wordWrap = false,
            normal = { textColor = Color.white }
        };

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { textColor = new Color(0.82f, 0.88f, 0.94f, 0.94f) }
        };

        hintStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            normal = { textColor = new Color(0.68f, 0.78f, 0.92f, 0.9f) }
        };

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            border = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(28, 28, 24, 24),
            normal = { background = solidTexture, textColor = Color.white }
        };

        chipStyle = new GUIStyle(GUI.skin.box)
        {
            border = new RectOffset(0, 0, 0, 0),
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(14, 14, 8, 8),
            normal = { background = solidTexture, textColor = Color.white }
        };

        buttonStyle = BuildButtonStyle(
            new Color(0.18f, 0.75f, 0.46f, 0.98f),
            new Color(0.24f, 0.82f, 0.52f, 1f),
            new Color(0.13f, 0.58f, 0.35f, 1f),
            Color.white);

        secondaryButtonStyle = BuildButtonStyle(
            new Color(0.17f, 0.45f, 0.72f, 0.98f),
            new Color(0.21f, 0.52f, 0.82f, 1f),
            new Color(0.13f, 0.35f, 0.58f, 1f),
            Color.white);

        disabledButtonStyle = BuildButtonStyle(
            new Color(0.22f, 0.26f, 0.33f, 0.92f),
            new Color(0.22f, 0.26f, 0.33f, 0.92f),
            new Color(0.22f, 0.26f, 0.33f, 0.92f),
            new Color(0.72f, 0.77f, 0.85f, 0.82f));

        levelButtonStyle = BuildButtonStyle(
            new Color(0.17f, 0.2f, 0.28f, 0.96f),
            new Color(0.22f, 0.27f, 0.36f, 0.98f),
            new Color(0.15f, 0.18f, 0.26f, 1f),
            Color.white);

        selectedLevelButtonStyle = BuildButtonStyle(
            new Color(0.93f, 0.62f, 0.14f, 0.98f),
            new Color(0.97f, 0.7f, 0.18f, 1f),
            new Color(0.84f, 0.53f, 0.1f, 1f),
            Color.white);

        completedLevelButtonStyle = BuildButtonStyle(
            new Color(0.15f, 0.58f, 0.34f, 0.98f),
            new Color(0.2f, 0.68f, 0.4f, 1f),
            new Color(0.12f, 0.49f, 0.28f, 1f),
            Color.white);

        lockedLevelButtonStyle = BuildButtonStyle(
            new Color(0.18f, 0.19f, 0.23f, 0.92f),
            new Color(0.18f, 0.19f, 0.23f, 0.92f),
            new Color(0.18f, 0.19f, 0.23f, 0.92f),
            new Color(0.58f, 0.62f, 0.7f, 0.82f));

        textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(16, 16, 12, 12),
            normal = { background = MakeTintTexture(new Color(0.1f, 0.13f, 0.18f, 0.95f)), textColor = Color.white },
            focused = { background = MakeTintTexture(new Color(0.12f, 0.16f, 0.22f, 0.98f)), textColor = Color.white }
        };
    }

    private GUIStyle BuildButtonStyle(Color normalColor, Color hoverColor, Color activeColor, Color textColor)
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            border = new RectOffset(0, 0, 0, 0),
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(18, 18, 14, 14),
            normal = { background = solidTexture, textColor = textColor },
            hover = { background = solidTexture, textColor = textColor },
            active = { background = solidTexture, textColor = textColor },
            focused = { background = solidTexture, textColor = textColor }
        };

        style.normal.background = MakeTintTexture(normalColor);
        style.hover.background = MakeTintTexture(hoverColor);
        style.active.background = MakeTintTexture(activeColor);
        style.focused.background = MakeTintTexture(hoverColor);
        return style;
    }

    private Texture2D MakeTintTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void ApplyResponsiveSizing(float scale)
    {
        titleStyle.fontSize = Mathf.RoundToInt(72f * scale);
        subtitleStyle.fontSize = Mathf.RoundToInt(21f * scale);
        sectionStyle.fontSize = Mathf.RoundToInt(24f * scale);
        bodyStyle.fontSize = Mathf.RoundToInt(18f * scale);
        hintStyle.fontSize = Mathf.RoundToInt(15f * scale);
        chipStyle.fontSize = Mathf.RoundToInt(14f * scale);
        buttonStyle.fontSize = Mathf.RoundToInt(22f * scale);
        secondaryButtonStyle.fontSize = Mathf.RoundToInt(21f * scale);
        disabledButtonStyle.fontSize = Mathf.RoundToInt(20f * scale);
        levelButtonStyle.fontSize = Mathf.RoundToInt(18f * scale);
        selectedLevelButtonStyle.fontSize = Mathf.RoundToInt(18f * scale);
        completedLevelButtonStyle.fontSize = Mathf.RoundToInt(18f * scale);
        lockedLevelButtonStyle.fontSize = Mathf.RoundToInt(17f * scale);
        textFieldStyle.fontSize = Mathf.RoundToInt(19f * scale);
    }

    private void DrawBackground(float scale)
    {
        DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.05f, 0.07f, 0.1f, 1f));

        DrawRotatedBand(
            new Rect(Screen.width * 0.56f, -120f * scale, 520f * scale, Screen.height + 240f * scale),
            12f,
            new Color(0.1f, 0.55f, 0.35f, 0.12f));

        DrawRotatedBand(
            new Rect(Screen.width * 0.72f, -160f * scale, 280f * scale, Screen.height + 280f * scale),
            12f,
            new Color(0.24f, 0.7f, 0.95f, 0.08f));

        DrawRotatedBand(
            new Rect(Screen.width * 0.08f, Screen.height * 0.24f, 240f * scale, 520f * scale),
            -18f,
            new Color(0.22f, 0.9f, 0.58f, 0.08f));
    }

    private void DrawBackdropGlow(Rect contentRect, float scale)
    {
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.35f);
        DrawSolidRect(
            new Rect(contentRect.x - 18f * scale, contentRect.y - 18f * scale, contentRect.width + 36f * scale, contentRect.height + 36f * scale),
            new Color(0.06f, 0.1f, 0.15f, 0.72f));
        DrawSolidRect(
            new Rect(contentRect.x + contentRect.width - 180f * scale, contentRect.y + 28f * scale, 150f * scale, 150f * scale),
            new Color(0.18f, 0.85f, 0.56f, Mathf.Lerp(0.06f, 0.14f, pulse)));
    }

    private void DrawHeroPanel(Rect rect, float scale)
    {
        DrawPanel(rect, new Color(0.08f, 0.1f, 0.14f, 0.9f));

        float padding = 34f * scale;
        float chipHeight = 34f * scale;
        Rect chipRect = new Rect(rect.x + padding, rect.y + padding, 156f * scale, chipHeight);
        DrawChip(chipRect, currentPage == MenuPage.Root ? "BUILD. DRIVE. WIN." : "CO-OP LOBBY");

        Rect titleRect = new Rect(rect.x + padding, chipRect.yMax + 26f * scale, rect.width - padding * 2f, 120f * scale);
        GUI.Label(titleRect, currentPage == MenuPage.Root ? gameTitle : "COK OYUNCU", titleStyle);

        Rect subtitleRect = new Rect(rect.x + padding, titleRect.yMax + 8f * scale, rect.width - padding * 2f, 88f * scale);
        int selectedLevel = LevelProgressionManager.GetSelectedLevelNumber();
        string selectedLevelLabel = selectedLevel > 0
            ? LevelProgressionManager.GetLevelDisplayName(selectedLevel)
            : "Level Secilmedi";
        GUI.Label(
            subtitleRect,
            currentPage == MenuPage.Root
                ? $"{gameSubtitle}\nSecili gorev: {selectedLevelLabel}"
                : "Oda kur, tek seferlik kodu paylas ya da Steam uzerinden direkt davet gonder.",
            subtitleStyle);

        Rect blurbRect = new Rect(rect.x + padding, subtitleRect.yMax + 18f * scale, rect.width - padding * 2f, 78f * scale);
        GUI.Label(
            blurbRect,
            currentPage == MenuPage.Root
                ? "Kendi aracini parcalardan kur, motorlarini bagla, koltuga gec ve bitis cizgisine ulas."
                : $"Host olacaksan once level sec. Room ID olusunca arkadas Steam davetiyle ya da kod yazarak gelir. Secili gorev: {selectedLevelLabel}",
            bodyStyle);

        float buttonWidth = rect.width - padding * 2f;
        float buttonHeight = 66f * scale;
        Rect firstRect = new Rect(rect.x + padding, rect.yMax - (198f * scale), buttonWidth, buttonHeight);
        Rect secondRect = new Rect(rect.x + padding, firstRect.yMax + 14f * scale, buttonWidth, buttonHeight * 0.9f);
        Rect thirdRect = new Rect(rect.x + padding, secondRect.yMax + 14f * scale, buttonWidth, buttonHeight * 0.84f);

        Color previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 1f);

        if (currentPage == MenuPage.Root)
        {
            if (GUI.Button(firstRect, $"{startButtonLabel}  ({selectedLevelLabel})", buttonStyle))
            {
                StartGame();
            }

            if (GUI.Button(secondRect, multiplayerButtonLabel, secondaryButtonStyle))
            {
                currentPage = MenuPage.Multiplayer;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            if (GUI.Button(thirdRect, quitButtonLabel, disabledButtonStyle))
            {
                QuitGame();
            }
        }
        else
        {
            bool isBusy = multiplayerCoordinator != null && multiplayerCoordinator.IsBusy;
            string roomCode = multiplayerCoordinator != null ? multiplayerCoordinator.CurrentRoomCode : string.Empty;

            GUI.enabled = !isBusy;
            if (GUI.Button(firstRect, "Oda Kur", buttonStyle))
            {
                multiplayerCoordinator?.StartHostingRoom();
            }

            GUI.enabled = !isBusy && !string.IsNullOrEmpty(roomCode);
            if (GUI.Button(secondRect, "Invite Friend", secondaryButtonStyle))
            {
                multiplayerCoordinator?.InviteFriendViaSteam();
            }

            GUI.enabled = true;
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            if (GUI.Button(thirdRect, "Geri", disabledButtonStyle))
            {
                currentPage = MenuPage.Root;
            }
        }

        GUI.enabled = true;
        GUI.color = previousColor;

        GUI.Label(
            new Rect(rect.x + padding, rect.yMax - 34f * scale, rect.width - padding * 2f, 22f * scale),
            currentPage == MenuPage.Root
                ? "Enter: baslat  |  Esc: cikis"
                : "Esc: ana menuye don",
            hintStyle);
    }

    private void DrawInfoPanel(Rect rect, float scale)
    {
        DrawPanel(rect, new Color(0.07f, 0.09f, 0.12f, 0.84f));

        float padding = 26f * scale;

        if (currentPage == MenuPage.Root)
        {
            GUI.Label(new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2f, 30f * scale), "Leveller", sectionStyle);

            Rect levelGridRect = new Rect(rect.x + padding, rect.y + 66f * scale, rect.width - padding * 2f, 204f * scale);
            DrawSolidRect(levelGridRect, new Color(1f, 1f, 1f, 0.04f));
            DrawLevelGrid(levelGridRect, scale);

            Rect featuresRect = new Rect(rect.x + padding, levelGridRect.yMax + 18f * scale, rect.width - padding * 2f, 120f * scale);
            DrawSolidRect(featuresRect, new Color(1f, 1f, 1f, 0.04f));
            int selectedLevel = LevelProgressionManager.GetSelectedLevelNumber();
            GUI.Label(
                new Rect(featuresRect.x + 16f * scale, featuresRect.y + 16f * scale, featuresRect.width - 32f * scale, featuresRect.height - 32f * scale),
                $"Secili: {LevelProgressionManager.GetLevelDisplayName(selectedLevel)}\n{LevelProgressionManager.GetLevelDescription(selectedLevel)}\nKilitli gorevler onceki bolum bitince acilir.",
                bodyStyle);

            GUI.Label(new Rect(rect.x + padding, featuresRect.yMax + 20f * scale, rect.width - padding * 2f, 30f * scale), "Kontroller", sectionStyle);

            Rect controlsRect = new Rect(rect.x + padding, featuresRect.yMax + 54f * scale, rect.width - padding * 2f, 144f * scale);
            DrawSolidRect(controlsRect, new Color(1f, 1f, 1f, 0.04f));
            GUI.Label(
                new Rect(controlsRect.x + 16f * scale, controlsRect.y + 16f * scale, controlsRect.width - 32f * scale, controlsRect.height - 32f * scale),
                "TAB  Build modu\nE    Koltuga otur\n1/2/X  Vites\n3    Pervane Ac/Kapa\nF    Dikey pervane\nR    Reset",
                bodyStyle);

            GUI.Label(
                new Rect(rect.x + padding, rect.yMax - 78f * scale, rect.width - padding * 2f, 54f * scale),
                $"Acilanlar: {LevelProgressionManager.HighestUnlockedLevel}/{Mathf.Max(1, LevelProgressionManager.TotalLevels)}  |  Tamamlananlar yesil gozukur.",
                hintStyle);
            return;
        }

        string roomCode = multiplayerCoordinator != null ? multiplayerCoordinator.CurrentRoomCode : string.Empty;
        string status = multiplayerCoordinator != null ? multiplayerCoordinator.StatusMessage : "Multiplayer runtime hazirlaniyor.";
        bool isBusy = multiplayerCoordinator != null && multiplayerCoordinator.IsBusy;

        GUI.Label(new Rect(rect.x + padding, rect.y + padding, rect.width - padding * 2f, 30f * scale), "Level Sec", sectionStyle);

        Rect multiplayerLevelRect = new Rect(rect.x + padding, rect.y + 66f * scale, rect.width - padding * 2f, 172f * scale);
        DrawSolidRect(multiplayerLevelRect, new Color(1f, 1f, 1f, 0.04f));
        DrawLevelGrid(multiplayerLevelRect, scale * 0.9f);

        GUI.Label(new Rect(rect.x + padding, multiplayerLevelRect.yMax + 16f * scale, rect.width - padding * 2f, 30f * scale), "Oturum", sectionStyle);

        Rect roomRect = new Rect(rect.x + padding, multiplayerLevelRect.yMax + 50f * scale, rect.width - padding * 2f, 72f * scale);
        DrawSolidRect(roomRect, new Color(1f, 1f, 1f, 0.04f));
        GUI.Label(
            new Rect(roomRect.x + 16f * scale, roomRect.y + 14f * scale, roomRect.width - 32f * scale, 24f * scale),
            string.IsNullOrEmpty(roomCode) ? "Room ID: --" : $"Room ID: {roomCode}",
            sectionStyle);
        GUI.Label(
            new Rect(roomRect.x + 16f * scale, roomRect.y + 40f * scale, roomRect.width - 32f * scale, 20f * scale),
            string.IsNullOrEmpty(roomCode)
                ? "Host secili level ile oda acar."
                : "Kodla ya da Steam davetiyle ayni goreve gelinir.",
            hintStyle);

        GUI.Label(new Rect(rect.x + padding, roomRect.yMax + 18f * scale, rect.width - padding * 2f, 30f * scale), "Kod Ile Katil", sectionStyle);

        Rect inputRect = new Rect(rect.x + padding, roomRect.yMax + 52f * scale, rect.width - padding * 2f, 48f * scale);
        GUI.enabled = !isBusy;
        joinCodeInput = GUI.TextField(inputRect, joinCodeInput ?? string.Empty, 24, textFieldStyle).ToUpperInvariant();

        Rect joinButtonRect = new Rect(rect.x + padding, inputRect.yMax + 12f * scale, rect.width - padding * 2f, 52f * scale);
        if (GUI.Button(joinButtonRect, "Odaya Katil", secondaryButtonStyle))
        {
            multiplayerCoordinator?.JoinRoomByCode(joinCodeInput);
        }
        GUI.enabled = true;

        GUI.Label(new Rect(rect.x + padding, joinButtonRect.yMax + 18f * scale, rect.width - padding * 2f, 30f * scale), "Durum", sectionStyle);

        Rect statusRect = new Rect(rect.x + padding, joinButtonRect.yMax + 52f * scale, rect.width - padding * 2f, 82f * scale);
        DrawSolidRect(statusRect, new Color(1f, 1f, 1f, 0.04f));
        GUI.Label(
            new Rect(statusRect.x + 16f * scale, statusRect.y + 16f * scale, statusRect.width - 32f * scale, statusRect.height - 32f * scale),
            status,
            bodyStyle);

        GUI.Label(
            new Rect(rect.x + padding, rect.yMax - 76f * scale, rect.width - padding * 2f, 54f * scale),
            "Kilitli level: Siradaki gorev tamamlanmadi. Join olan oyuncu host'un sectigi levelde acilir.",
            hintStyle);
    }

    private void DrawLevelGrid(Rect rect, float scale)
    {
        int totalLevels = Mathf.Max(0, LevelProgressionManager.TotalLevels);
        if (totalLevels <= 0)
        {
            GUI.Label(new Rect(rect.x + 12f, rect.y + 12f, rect.width - 24f, rect.height - 24f), "Level katalogu bulunamadi.", bodyStyle);
            return;
        }

        float spacing = 8f * scale;
        int columns = 5;
        int rows = Mathf.CeilToInt(totalLevels / (float)columns);
        float buttonWidth = (rect.width - spacing * (columns + 1)) / columns;
        float buttonHeight = Mathf.Min(52f * scale, (rect.height - spacing * (rows + 1)) / Mathf.Max(1, rows));
        int selectedLevel = LevelProgressionManager.GetSelectedLevelNumber();

        for (int levelNumber = 1; levelNumber <= totalLevels; levelNumber++)
        {
            int index = levelNumber - 1;
            int row = index / columns;
            int column = index % columns;
            Rect buttonRect = new Rect(
                rect.x + spacing + column * (buttonWidth + spacing),
                rect.y + spacing + row * (buttonHeight + spacing),
                buttonWidth,
                buttonHeight);

            bool unlocked = LevelProgressionManager.IsUnlocked(levelNumber);
            bool completed = LevelProgressionManager.IsCompleted(levelNumber);
            GUIStyle style = lockedLevelButtonStyle;
            string label = unlocked ? levelNumber.ToString() : $"{levelNumber}\nKilitli";

            if (selectedLevel == levelNumber && unlocked)
            {
                style = selectedLevelButtonStyle;
            }
            else if (completed)
            {
                style = completedLevelButtonStyle;
            }
            else if (unlocked)
            {
                style = levelButtonStyle;
            }

            bool previousEnabled = GUI.enabled;
            GUI.enabled = unlocked;
            if (GUI.Button(buttonRect, label, style))
            {
                LevelProgressionManager.SelectLevel(levelNumber);
            }

            GUI.enabled = previousEnabled;
        }
    }

    private void DrawPanel(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.Box(rect, GUIContent.none, panelStyle);
        GUI.color = previousColor;
    }

    private void DrawChip(Rect rect, string label)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0.16f, 0.66f, 0.42f, 0.95f);
        GUI.Box(rect, label, chipStyle);
        GUI.color = previousColor;
    }

    private void DrawSolidRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, solidTexture, ScaleMode.StretchToFill);
        GUI.color = previousColor;
    }

    private void DrawRotatedBand(Rect rect, float angle, Color color)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        Vector2 pivot = rect.center;
        GUIUtility.RotateAroundPivot(angle, pivot);
        DrawSolidRect(rect, color);
        GUI.matrix = previousMatrix;
    }
}
