using UnityEngine;

// Full-screen menu and lobby presentation. Gameplay HUD remains in Game.cs.
public partial class Game
{
    enum MenuPage { Home, Online, Solo, Settings }

    const float MenuW = 1280f, MenuH = 720f;
    static readonly Color MenuBase = new Color(0.075f, 0.09f, 0.088f, 1f);
    static readonly Color MenuPanel = new Color(0.105f, 0.13f, 0.125f, 0.98f);
    static readonly Color MenuPanelHot = new Color(0.15f, 0.18f, 0.17f, 0.99f);
    static readonly Color MenuInk = new Color(0.96f, 0.96f, 0.93f, 1f);
    static readonly Color MenuMuted = new Color(0.66f, 0.69f, 0.67f, 1f);
    static readonly Color MenuAccent = new Color(0.90f, 0.72f, 0.47f, 1f);
    static readonly Color MenuLine = new Color(1f, 1f, 1f, 0.13f);

    MenuPage menuPage;
    bool menuPageReady, advancedConnection;
    GUIStyle menuText, menuTitle, menuHeading, menuSmall, menuMono, menuButton;

    void EnsureMenuPage()
    {
        if (menuPageReady) return;
        menuPageReady = true;
        string requested = ParseArg("-menupage");
        if (showJoin || requested == "online" || requested == "lobby") OpenMenuPage(MenuPage.Online);
        else if (requested == "solo") OpenMenuPage(MenuPage.Solo);
        else if (requested == "settings") OpenMenuPage(MenuPage.Settings);
    }

    void OpenMenuPage(MenuPage page)
    {
        menuPage = page;
        showJoin = page == MenuPage.Online;
        if (page == MenuPage.Online)
        {
            LobbyClient.LobbyUrl = lobbyUrl.Trim();
            lobbyPollT = 0f;
        }
    }

    void MenuStyles()
    {
        if (menuText != null) return;
        menuText = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = MenuInk } };
        menuTitle = new GUIStyle(menuText) { fontSize = 56, fontStyle = FontStyle.Normal };
        menuHeading = new GUIStyle(menuText) { fontSize = 31, fontStyle = FontStyle.Normal };
        menuSmall = new GUIStyle(menuText) { fontSize = 14, normal = { textColor = MenuMuted } };
        menuMono = new GUIStyle(menuSmall) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = MenuAccent } };
        menuButton = new GUIStyle(GUI.skin.button)
        {
            fontSize = 16,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = MenuInk, background = Texture2D.whiteTexture },
            hover = { textColor = MenuInk, background = Texture2D.whiteTexture },
            active = { textColor = MenuInk, background = Texture2D.whiteTexture }
        };
    }

    void Menu(float screenW, float screenH)
    {
        EnsureMenuPage();
        MenuStyles();
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(screenW / MenuW, screenH / MenuH, 1f));
        Fill(new Rect(0, 0, MenuW, MenuH), MenuBase);
        MenuChrome();
        if (menuPage == MenuPage.Home) MenuHome();
        else if (menuPage == MenuPage.Online) MenuLobby();
        else if (menuPage == MenuPage.Solo) MenuSolo();
        else MenuSettings();
        MenuFooter();
        GUI.matrix = previous;
    }

    void MenuChrome()
    {
        Fill(new Rect(0, 0, MenuW, 84), new Color(0.067f, 0.08f, 0.078f, 1f));
        Fill(new Rect(0, 83, MenuW, 1), MenuLine);
        MText(new Rect(40, 18, 200, 32), "沙 场", 24, MenuInk);
        MText(new Rect(42, 48, 200, 18), "S H A C H A N G", 10, MenuMuted);

        NavButton(new Rect(504, 20, 76, 52), "主菜单", MenuPage.Home);
        NavButton(new Rect(604, 20, 92, 52), "联机大厅", MenuPage.Online);
        NavButton(new Rect(720, 20, 92, 52), "单人游戏", MenuPage.Solo);

        MText(new Rect(1095, 23, 145, 18), "UI CONCEPT / 01", 11, MenuMuted, TextAnchor.MiddleRight);
        MText(new Rect(1095, 43, 145, 18), "界面设计实现", 11, MenuAccent, TextAnchor.MiddleRight);
    }

    void NavButton(Rect rect, string text, MenuPage page)
    {
        bool active = menuPage == page;
        bool hot = rect.Contains(Event.current.mousePosition);
        MText(rect, text, 16, active || hot ? MenuInk : MenuMuted, TextAnchor.MiddleCenter);
        if (active) Fill(new Rect(rect.x + 10, rect.yMax - 2, rect.width - 20, 2), MenuAccent);
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) OpenMenuPage(page);
    }

    void MenuHome()
    {
        Texture2D background = UiTex(ref uiBg, "menu_bg");
        if (background)
        {
            GUI.color = new Color(0.62f, 0.64f, 0.60f, 1f);
            GUI.DrawTexture(new Rect(0, 84, MenuW, 580), background, ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
        }
        Fill(new Rect(0, 84, MenuW, 580), new Color(0.035f, 0.06f, 0.055f, 0.68f));
        Fill(new Rect(0, 520, MenuW, 144), new Color(MenuBase.r, MenuBase.g, MenuBase.b, 0.96f));

        MText(new Rect(40, 122, 460, 24), "DESERT THEATER  /  沙漠战区", 12, MenuAccent);
        MText(new Rect(40, 158, 760, 74), "选择你的战场。", 56, MenuInk);
        MText(new Rect(40, 236, 520, 52), "与其他玩家交锋，或独自深入沙漠。\n每一次出发，都由你决定。", 17, new Color(0.82f, 0.84f, 0.81f));

        Gate(new Rect(40, 330, 650, 225), UiTex(ref uiEndless, "card_endless"),
             "01  /  MULTIPLAYER", "联机对战", "团队死斗 · 公网房间", "进入联机大厅", MenuPage.Online, true);
        Gate(new Rect(714, 330, 526, 225), UiTex(ref uiExtract, "card_extract"),
             "02  /  SINGLE PLAYER", "单人游戏", "无尽生存 · 战场撤离 · 据点占领", "选择单人模式", MenuPage.Solo, false);
    }

    void Gate(Rect rect, Texture2D art, string number, string title, string detail, string action, MenuPage page, bool primary)
    {
        bool hot = rect.Contains(Event.current.mousePosition);
        Fill(rect, hot ? MenuPanelHot : MenuPanel);
        if (art)
        {
            GUI.color = hot ? new Color(0.75f, 0.75f, 0.72f, 1f) : new Color(0.56f, 0.57f, 0.54f, 1f);
            GUI.DrawTexture(rect, art, ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
        }
        Fill(rect, new Color(0.04f, 0.065f, 0.06f, hot ? 0.50f : 0.64f));
        Outline(rect, hot ? MenuAccent : MenuLine, primary ? 2f : 1f);
        MText(new Rect(rect.x + 28, rect.y + 22, rect.width - 56, 22), number, 12, MenuInk);
        MText(new Rect(rect.x + 28, rect.yMax - 112, rect.width - 56, 43), title, 31, MenuInk);
        MText(new Rect(rect.x + 28, rect.yMax - 70, rect.width - 56, 24), detail, 15, new Color(0.82f, 0.84f, 0.81f));
        Fill(new Rect(rect.x + 28, rect.yMax - 40, rect.width - 56, 1), new Color(1, 1, 1, 0.20f));
        MText(new Rect(rect.x + 28, rect.yMax - 34, rect.width - 56, 26), action + "  ↗", 15, MenuAccent);
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) OpenMenuPage(page);
    }

    void MenuLobby()
    {
        var lobby = LobbyClient.Create();
        if (!lobby.busy && Time.unscaledTime > lobbyPollT)
        {
            lobbyPollT = Time.unscaledTime + 5f;
            LobbyClient.LobbyUrl = lobbyUrl.Trim();
            lobby.Refresh();
        }

        PageIntro("MULTIPLAYER  /  联机对战", "选择房间，即刻加入。", "点击可用房间进入对局。", true);
        Rect refresh = new Rect(1124, 160, 116, 46);
        MButton(refresh, lobby.busy ? "刷新中…" : "刷新", false, () =>
        {
            LobbyClient.LobbyUrl = lobbyUrl.Trim();
            lobbyPollT = Time.unscaledTime + 5f;
            lobby.Refresh();
        }, lobby.busy);

        float top = 274f;
        MText(new Rect(64, top, 290, 20), "房间", 12, MenuMuted);
        MText(new Rect(398, top, 140, 20), "模式", 12, MenuMuted);
        MText(new Rect(590, top, 140, 20), "地图", 12, MenuMuted);
        MText(new Rect(770, top, 100, 20), "人数", 12, MenuMuted);
        MText(new Rect(902, top, 110, 20), "状态", 12, MenuMuted);

        float rowY = top + 27;
        if (lobby.rooms.Count == 0)
        {
            Rect empty = new Rect(40, rowY, 1200, 112);
            Fill(empty, MenuPanel);
            Outline(empty, MenuLine, 1);
            MText(new Rect(empty.x + 24, empty.y + 30, empty.width - 48, 28), lobby.busy ? "正在读取房间列表…" : "暂时没有可加入的房间", 18, MenuInk);
            MText(new Rect(empty.x + 24, empty.y + 63, empty.width - 48, 24), lobby.status, 13, MenuMuted);
            rowY += 124;
        }
        else
        {
            int shown = Mathf.Min(3, lobby.rooms.Count);
            for (int i = 0; i < shown; i++)
            {
                RoomRow(new Rect(40, rowY, 1200, 94), lobby.rooms[i]);
                rowY += 104;
            }
        }

        string summary = lobby.busy ? "正在刷新房间…" : lobby.status;
        MText(new Rect(40, 607, 650, 24), summary, 13, MenuMuted);
        Rect advanced = new Rect(1010, 597, 230, 34);
        MText(advanced, (advancedConnection ? "▼" : "▶") + " 高级连接", 14, MenuInk, TextAnchor.MiddleRight);
        if (GUI.Button(advanced, GUIContent.none, GUIStyle.none)) advancedConnection = !advancedConnection;
        if (advancedConnection) AdvancedConnection();
    }

    void RoomRow(Rect rect, RoomInfo room)
    {
        bool full = room.players >= room.max;
        bool joinable = !full && room.state != "offline";
        bool hot = joinable && rect.Contains(Event.current.mousePosition);
        Fill(rect, hot ? MenuPanelHot : MenuPanel);
        Outline(rect, hot ? MenuAccent : MenuLine, 1);
        MText(new Rect(rect.x + 24, rect.y + 24, 300, 26), room.name, 17, joinable ? MenuInk : MenuMuted);
        MText(new Rect(rect.x + 24, rect.y + 53, 300, 20), "公开房间", 12, MenuMuted);
        MText(new Rect(rect.x + 358, rect.y + 34, 160, 24), room.mode == "tdm" ? "团队死斗" : room.mode, 15, MenuInk);
        MText(new Rect(rect.x + 550, rect.y + 34, 160, 24), "沙漠战场", 15, MenuInk);
        MText(new Rect(rect.x + 730, rect.y + 34, 110, 24), room.players + " / " + room.max, 15, MenuInk);
        string state = full ? "已满员" : room.state == "playing" ? "进行中" : "可加入";
        MText(new Rect(rect.x + 862, rect.y + 34, 120, 24), state, 15, joinable ? new Color(0.70f, 0.82f, 0.66f) : MenuMuted);
        MText(new Rect(rect.x + 1045, rect.y + 34, 120, 24), joinable ? "进入  →" : "不可加入", 15, joinable ? MenuAccent : MenuMuted, TextAnchor.MiddleRight);
        if (joinable && GUI.Button(rect, GUIContent.none, GUIStyle.none)) StartOnline(room.host, (ushort)room.port);
    }

    void AdvancedConnection()
    {
        Rect box = new Rect(650, 516, 590, 74);
        Fill(box, new Color(0.10f, 0.125f, 0.12f, 1f));
        Outline(box, MenuLine, 1);
        MText(new Rect(box.x + 16, box.y + 8, 210, 18), "服务器地址", 11, MenuMuted);
        MText(new Rect(box.x + 280, box.y + 8, 72, 18), "端口", 11, MenuMuted);
        joinIp = GUI.TextField(new Rect(box.x + 16, box.y + 29, 248, 31), joinIp, 40);
        joinPort = GUI.TextField(new Rect(box.x + 280, box.y + 29, 80, 31), joinPort, 6);
        MButton(new Rect(box.x + 377, box.y + 22, 195, 40), "直接连接", true, () =>
        {
            ushort.TryParse(joinPort, out ushort port);
            StartOnline(joinIp.Trim(), port == 0 ? (ushort)8443 : port);
        });
    }

    void MenuSolo()
    {
        PageIntro("SINGLE PLAYER  /  单人游戏", "选择任务，进入战场。", "三个模式均使用同一片动态沙漠战区。", false);
        float y = 276, gap = 18, width = (1200 - gap * 2) / 3f;
        SoloCard(new Rect(40, y, width, 287), UiTex(ref uiEndless, "card_endless"), "无尽模式",
                 "抵御一波波敌军，挑战 Boss，利用商店和补给坚持更久。", "最高得分 " + PlayerPrefs.GetInt("best_endless", 0), Mode.Endless);
        SoloCard(new Rect(40 + width + gap, y, width, 287), UiTex(ref uiExtract, "card_extract"), "撤离模式",
                 "搜集高价值物资，在时限内抵达撤离点；阵亡会失去本局物资。", "最高带出 $" + PlayerPrefs.GetInt("best_extract", 0), Mode.Extraction);
        SoloCard(new Rect(40 + (width + gap) * 2, y, width, 287), UiTex(ref uiConquest, "card_conquest"), "据点占领",
                 "争夺并守住五个据点，以持续控制积累分数，率先达到目标。", "最高得分 " + PlayerPrefs.GetInt("best_conquest", 0), Mode.Conquest);
        MText(new Rect(40, 582, 900, 24), "所有模式均可随时按 H 查看操作说明，按 F3 切换画质。", 13, MenuMuted);
    }

    void SoloCard(Rect rect, Texture2D art, string title, string description, string record, Mode target)
    {
        bool hot = rect.Contains(Event.current.mousePosition);
        Fill(rect, hot ? MenuPanelHot : MenuPanel);
        Outline(rect, hot ? MenuAccent : MenuLine, 1);
        Rect picture = new Rect(rect.x, rect.y, rect.width, 132);
        if (art)
        {
            GUI.color = hot ? Color.white : new Color(0.78f, 0.78f, 0.75f);
            GUI.DrawTexture(picture, art, ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
        }
        Fill(new Rect(picture.x, picture.yMax - 35, picture.width, 35), new Color(0.06f, 0.08f, 0.075f, 0.72f));
        MText(new Rect(rect.x + 20, rect.y + 147, rect.width - 40, 31), title, 24, MenuInk);
        MText(new Rect(rect.x + 20, rect.y + 188, rect.width - 40, 53), description, 13, MenuMuted);
        Fill(new Rect(rect.x + 20, rect.yMax - 41, rect.width - 40, 1), MenuLine);
        MText(new Rect(rect.x + 20, rect.yMax - 34, rect.width - 40, 24), record, 13, MenuMuted);
        MText(new Rect(rect.x + 20, rect.yMax - 34, rect.width - 40, 24), "开始  →", 14, MenuAccent, TextAnchor.MiddleRight);
        if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) StartMode(target);
    }

    void MenuSettings()
    {
        PageIntro("SETTINGS", "作战准备。", "设置会立即应用并保存到本机。", false);
        Rect panel = new Rect(310, 268, 660, 280);
        Fill(panel, MenuPanel);
        Outline(panel, MenuLine, 1);

        MText(new Rect(panel.x + 30, panel.y + 30, 180, 28), "画面质量", 17, MenuInk);
        string[] qualities = { "流畅", "均衡", "高清" };
        for (int i = 0; i < qualities.Length; i++)
        {
            int choice = i;
            MButton(new Rect(panel.x + 270 + i * 108, panel.y + 22, 96, 40), qualities[i], quality == i, () =>
            {
                quality = choice; ApplyQuality(); PlayerPrefs.SetInt("quality_v2", quality);
            });
        }
        Fill(new Rect(panel.x + 30, panel.y + 84, panel.width - 60, 1), MenuLine);
        MText(new Rect(panel.x + 30, panel.y + 111, 210, 28), "主音量  " + Mathf.RoundToInt(Sfx.volume * 100) + "%", 17, MenuInk);
        Sfx.volume = GUI.HorizontalSlider(new Rect(panel.x + 270, panel.y + 119, 324, 20), Sfx.volume, 0f, 1f);
        Fill(new Rect(panel.x + 30, panel.y + 164, panel.width - 60, 1), MenuLine);
        MText(new Rect(panel.x + 30, panel.y + 192, 220, 28), "鼠标灵敏度  " + Tune.I.mouseSens.ToString("0.0"), 17, MenuInk);
        Tune.I.mouseSens = GUI.HorizontalSlider(new Rect(panel.x + 270, panel.y + 200, 324, 20), Tune.I.mouseSens, 0.5f, 5f);
        if (Event.current.type == EventType.MouseUp)
        {
            PlayerPrefs.SetFloat("volume", Sfx.volume);
            PlayerPrefs.SetFloat("sens", Tune.I.mouseSens);
        }
        MButton(new Rect(530, 574, 220, 44), "返回主菜单", false, () => OpenMenuPage(MenuPage.Home));
    }

    void PageIntro(string eyebrow, string title, string detail, bool lobby)
    {
        if (menuPage != MenuPage.Home)
        {
            Rect back = new Rect(40, 105, 110, 30);
            MText(back, "←  主菜单", 14, MenuMuted);
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) OpenMenuPage(MenuPage.Home);
        }
        MText(new Rect(40, 151, 500, 20), eyebrow, 12, MenuAccent);
        MText(new Rect(40, 185, 760, 46), title, 33, MenuInk);
        MText(new Rect(40, 233, 660, 24), detail, 14, MenuMuted);
    }

    void MenuFooter()
    {
        Fill(new Rect(0, 664, MenuW, 56), new Color(0.065f, 0.078f, 0.075f, 1f));
        Fill(new Rect(0, 664, MenuW, 1), MenuLine);
        MText(new Rect(40, 681, 700, 20), "沙场 · 公网大厅 " + lobbyUrl.Replace("http://", "") + " · 版本 0.2", 12, MenuMuted);
        Rect settings = new Rect(1120, 674, 120, 34);
        MText(settings, "设置  ⚙", 14, menuPage == MenuPage.Settings ? MenuAccent : MenuInk, TextAnchor.MiddleRight);
        if (GUI.Button(settings, GUIContent.none, GUIStyle.none)) OpenMenuPage(MenuPage.Settings);
    }

    void MButton(Rect rect, string text, bool primary, System.Action action, bool disabled = false)
    {
        bool hot = !disabled && rect.Contains(Event.current.mousePosition);
        Fill(rect, primary ? MenuAccent : hot ? MenuPanelHot : new Color(0.14f, 0.17f, 0.165f, 1f));
        Outline(rect, primary ? MenuAccent : hot ? new Color(1, 1, 1, 0.28f) : MenuLine, 1);
        MText(rect, text, 15, disabled ? MenuMuted : primary ? MenuBase : MenuInk, TextAnchor.MiddleCenter);
        if (!disabled && GUI.Button(rect, GUIContent.none, GUIStyle.none)) action();
    }

    void MText(Rect rect, string text, int size, Color color, TextAnchor align = TextAnchor.UpperLeft)
    {
        GUI.Label(rect, text, new GUIStyle(menuText) { fontSize = size, alignment = align, normal = { textColor = color }, wordWrap = text.Contains("\n") });
    }

    void Outline(Rect rect, Color color, float thickness)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
        Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }
}
