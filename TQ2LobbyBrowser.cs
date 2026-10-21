// Titan Quest II Lobby Browser
// Lists public Steam lobbies for Titan Quest II (AppID 1154030) and joins them via steam://joinlobby.
// Uses Valve's steam_api64.dll from the player's own Titan Quest II install (nothing redistributed).
// Build: csc /target:winexe /platform:x64 /optimize+ TQ2LobbyBrowser.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

static class Steam
{
    const string Dll = "steam_api64.dll";

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string path);

    [DllImport(Dll)] public static extern int SteamAPI_InitFlat(byte[] errMsg);
    [DllImport(Dll)] public static extern void SteamAPI_Shutdown();
    [DllImport(Dll)] public static extern void SteamAPI_RunCallbacks();
    [DllImport(Dll)] public static extern IntPtr SteamAPI_SteamMatchmaking_v009();
    [DllImport(Dll)] public static extern IntPtr SteamAPI_SteamUtils_v010();
    [DllImport(Dll)] public static extern IntPtr SteamAPI_SteamUser_v023();
    [DllImport(Dll)] public static extern IntPtr SteamAPI_SteamFriends_v017();
    [DllImport(Dll)] public static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong steamId, out FriendGameInfo info);
    [DllImport(Dll)] public static extern void SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter(IntPtr self, int filter);
    [DllImport(Dll)] public static extern void SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter(IntPtr self, int max);
    [DllImport(Dll)] public static extern ulong SteamAPI_ISteamMatchmaking_RequestLobbyList(IntPtr self);
    [DllImport(Dll)] public static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyByIndex(IntPtr self, int index);
    [DllImport(Dll)] public static extern int SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(IntPtr self, ulong lobby);
    [DllImport(Dll)] public static extern int SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit(IntPtr self, ulong lobby);
    [DllImport(Dll)] public static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyOwner(IntPtr self, ulong lobby);
    [DllImport(Dll)] public static extern IntPtr SteamAPI_ISteamMatchmaking_GetLobbyData(IntPtr self, ulong lobby, string key);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamMatchmaking_SetLobbyData(IntPtr self, ulong lobby, string key, string value);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamMatchmaking_DeleteLobbyData(IntPtr self, ulong lobby, string key);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(IntPtr self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUtils_GetAPICallResult(IntPtr self, ulong call, ref uint result, int size, int callbackId, [MarshalAs(UnmanagedType.I1)] out bool failed);

    [StructLayout(LayoutKind.Sequential)]
    public struct FriendGameInfo
    {
        public ulong GameId;
        public uint GameIp;
        public ushort GamePort;
        public ushort QueryPort;
        public ulong LobbyId;
    }

    public const int LobbyMatchListCallback = 510;
    public const int DistanceWorldwide = 3;
}

class Lobby
{
    public ulong Id;
    public int Players;
    public int Limit;
    // Host-published info; -1 = not set (or invalid, which is treated the same).
    public int Difficulty = -1;
    public int Level = -1;
    public int Playstyle = -1;
}

// Game info hosts can attach to their lobby. Stored as small numeric codes and validated on read,
// so nothing but these exact choices can ever be displayed.
static class GameInfo
{
    public static readonly string[] Difficulties = { "Neophyte", "Normal", "Hardcore" };
    public static readonly string[] Playstyles = { "Story", "Farming", "Questing", "Help Wanted" };
    public const int MaxLevel = 100;

    public const string DifficultyKey = "tq2lb_diff";
    public const string LevelKey = "tq2lb_lvl";
    public const string PlaystyleKey = "tq2lb_style";

    // Returns the value if it's a whole number in [min, max], otherwise -1.
    public static int Read(IntPtr matchmaking, ulong lobby, string key, int min, int max)
    {
        string s = Marshal.PtrToStringAnsi(Steam.SteamAPI_ISteamMatchmaking_GetLobbyData(matchmaking, lobby, key));
        int v;
        return s != null && s.Length <= 3 && int.TryParse(s, out v) && v >= min && v <= max ? v : -1;
    }

    public static string Name(string[] names, int index) { return index >= 0 && index < names.Length ? names[index] : ""; }
}

class MainForm : Form
{
    const string AppId = "1154030";

    readonly ListView list = new ListView();
    readonly Button refreshButton = new Button();
    readonly Button joinButton = new Button();
    readonly CheckBox hideFull = new CheckBox();
    readonly CheckBox autoRefresh = new CheckBox();
    readonly Label status = new Label();
    readonly TextBox joinIdBox = new TextBox();
    readonly Button joinIdButton = new Button();
    readonly Label myGameLabel = new Label();
    readonly Button copyMyIdButton = new Button();
    readonly ContextMenuStrip listMenu = new ContextMenuStrip();
    readonly ComboBox myDifficulty = new ComboBox();
    readonly ComboBox myLevel = new ComboBox();
    readonly ComboBox myPlaystyle = new ComboBox();
    readonly Button publishButton = new Button();
    readonly ComboBox difficultyFilter = new ComboBox();
    readonly ComboBox playstyleFilter = new ComboBox();
    readonly Timer pollTimer = new Timer();
    readonly Timer autoTimer = new Timer();

    IntPtr matchmaking, utils;
    ulong pendingCall;
    DateTime pendingDeadline;
    bool steamUp;
    List<Lobby> lobbies = new List<Lobby>();
    ulong myLobby;
    bool iAmHost;
    bool infoPublished;  // once the host clicks Publish, their info is re-applied to every game they host

    public MainForm()
    {
        Text = "Titan Quest II - Lobby Browser";
        ClientSize = new Size(700, 600);
        MinimumSize = new Size(420, 300);
        Font = new Font("Segoe UI", 9f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6), WrapContents = false };
        refreshButton.Text = "Refresh";
        refreshButton.AutoSize = true;
        refreshButton.Click += delegate { StartRefresh(); };
        joinButton.Text = "Join selected";
        joinButton.AutoSize = true;
        joinButton.Enabled = false;
        joinButton.Click += delegate { JoinSelected(); };
        hideFull.Text = "Hide full games";
        hideFull.AutoSize = true;
        hideFull.Checked = true;
        hideFull.Margin = new Padding(12, 6, 3, 3);
        hideFull.CheckedChanged += delegate { Populate(); };
        autoRefresh.Text = "Auto-refresh (30s)";
        autoRefresh.AutoSize = true;
        autoRefresh.Margin = new Padding(12, 6, 3, 3);
        autoRefresh.CheckedChanged += delegate { autoTimer.Enabled = autoRefresh.Checked; };
        top.Controls.AddRange(new Control[] { refreshButton, joinButton, hideFull, autoRefresh });

        var joinRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 0, 6, 0), WrapContents = false };
        var joinLabel = new Label { Text = "Join by ID:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        joinIdBox.Width = 230;
        joinIdBox.Margin = new Padding(3, 4, 3, 3);
        joinIdBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; JoinById(); } };
        joinIdButton.Text = "Join";
        joinIdButton.AutoSize = true;
        joinIdButton.Click += delegate { JoinById(); };
        joinRow.Controls.AddRange(new Control[] { joinLabel, joinIdBox, joinIdButton });

        var myRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 0, 6, 0), WrapContents = false };
        myGameLabel.AutoSize = true;
        myGameLabel.Margin = new Padding(3, 7, 3, 3);
        copyMyIdButton.Text = "Copy my ID";
        copyMyIdButton.AutoSize = true;
        copyMyIdButton.Click += delegate { CopyId(myLobby); };
        myRow.Controls.AddRange(new Control[] { myGameLabel, copyMyIdButton });

        var infoRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 0, 6, 0), WrapContents = false };
        var levels = new List<string>();
        for (int i = 1; i <= GameInfo.MaxLevel; i++) levels.Add(i.ToString());
        SetupCombo(myDifficulty, "–", GameInfo.Difficulties, 90);
        SetupCombo(myLevel, "–", levels.ToArray(), 50);
        SetupCombo(myPlaystyle, "–", GameInfo.Playstyles, 100);
        publishButton.Text = "Publish";
        publishButton.AutoSize = true;
        publishButton.Click += delegate { Publish(); };
        infoRow.Controls.AddRange(new Control[] {
            RowLabel("Your game info:  Difficulty"), myDifficulty, RowLabel("Level"), myLevel,
            RowLabel("Playstyle"), myPlaystyle, publishButton });
        UpdateMyGame();

        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 0, 6, 0), WrapContents = false };
        SetupCombo(difficultyFilter, "Any difficulty", GameInfo.Difficulties, 110);
        SetupCombo(playstyleFilter, "Any playstyle", GameInfo.Playstyles, 110);
        difficultyFilter.SelectedIndexChanged += delegate { Populate(); };
        playstyleFilter.SelectedIndexChanged += delegate { Populate(); };
        filterRow.Controls.AddRange(new Control[] { RowLabel("Show:"), difficultyFilter, playstyleFilter });

        listMenu.Items.Add("Copy lobby ID", null, delegate { if (list.SelectedItems.Count > 0) CopyId((ulong)list.SelectedItems[0].Tag); });
        listMenu.Items.Add("Join", null, delegate { JoinSelected(); });

        list.Dock = DockStyle.Fill;
        list.ContextMenuStrip = listMenu;
        list.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C && list.SelectedItems.Count > 0) CopyId((ulong)list.SelectedItems[0].Tag); };
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.Columns.Add("Players", 60);
        list.Columns.Add("Open", 50);
        list.Columns.Add("Difficulty", 80);
        list.Columns.Add("Level", 50);
        list.Columns.Add("Playstyle", 90);
        list.Columns.Add("Lobby ID", 160);
        list.Columns.Add("", 80);
        list.SelectedIndexChanged += delegate { joinButton.Enabled = list.SelectedItems.Count > 0; };
        list.DoubleClick += delegate { JoinSelected(); };

        status.Dock = DockStyle.Bottom;
        status.Height = 44;
        status.Padding = new Padding(6, 4, 6, 4);
        status.Text = "Start Titan Quest II first, then pick a game and click Join (or double-click it).";

        Controls.Add(list);
        Controls.Add(filterRow);
        Controls.Add(infoRow);
        Controls.Add(myRow);
        Controls.Add(joinRow);
        Controls.Add(top);
        Controls.Add(status);

        pollTimer.Interval = 50;
        pollTimer.Tick += delegate { Poll(); };
        autoTimer.Interval = 30000;
        autoTimer.Tick += delegate { if (!pollTimer.Enabled) StartRefresh(); };

        Shown += delegate { StartRefresh(); };
    }

    static void SetupCombo(ComboBox combo, string first, string[] items, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = width;
        combo.Margin = new Padding(3, 4, 3, 3);
        combo.Items.Add(first);
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
    }

    static Label RowLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
    }

    bool InitSteam()
    {
        if (steamUp) return true;
        Environment.SetEnvironmentVariable("SteamAppId", AppId);
        Environment.SetEnvironmentVariable("SteamGameId", AppId);
        var err = new byte[1024];
        int rc = Steam.SteamAPI_InitFlat(err);
        if (rc != 0)
        {
            string msg = Encoding.UTF8.GetString(err).TrimEnd('\0');
            SetStatus("Could not connect to Steam (" + msg + "). Make sure Steam is running and you own Titan Quest II.");
            return false;
        }
        steamUp = true;
        matchmaking = Steam.SteamAPI_SteamMatchmaking_v009();
        utils = Steam.SteamAPI_SteamUtils_v010();
        return true;
    }

    void StartRefresh()
    {
        if (pollTimer.Enabled || !InitSteam()) return;
        FindMyLobby();
        if (infoPublished) WriteMyInfo();
        Steam.SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter(matchmaking, Steam.DistanceWorldwide);
        Steam.SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter(matchmaking, 500);
        pendingCall = Steam.SteamAPI_ISteamMatchmaking_RequestLobbyList(matchmaking);
        pendingDeadline = DateTime.Now.AddSeconds(15);
        refreshButton.Enabled = false;
        SetStatus("Searching for games...");
        pollTimer.Start();
    }

    void Poll()
    {
        Steam.SteamAPI_RunCallbacks();
        bool failed;
        if (!Steam.SteamAPI_ISteamUtils_IsAPICallCompleted(utils, pendingCall, out failed))
        {
            if (DateTime.Now > pendingDeadline) Finish("Steam did not answer in time. Try Refresh again.");
            return;
        }
        uint count = 0;
        if (!Steam.SteamAPI_ISteamUtils_GetAPICallResult(utils, pendingCall, ref count, 4, Steam.LobbyMatchListCallback, out failed) || failed)
        {
            Finish("The lobby search failed. Try Refresh again.");
            return;
        }
        var found = new List<Lobby>();
        for (int i = 0; i < count; i++)
        {
            ulong id = Steam.SteamAPI_ISteamMatchmaking_GetLobbyByIndex(matchmaking, i);
            found.Add(new Lobby
            {
                Id = id,
                Players = Steam.SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(matchmaking, id),
                Limit = Steam.SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit(matchmaking, id),
                Difficulty = GameInfo.Read(matchmaking, id, GameInfo.DifficultyKey, 0, GameInfo.Difficulties.Length - 1),
                Level = GameInfo.Read(matchmaking, id, GameInfo.LevelKey, 1, GameInfo.MaxLevel),
                Playstyle = GameInfo.Read(matchmaking, id, GameInfo.PlaystyleKey, 0, GameInfo.Playstyles.Length - 1)
            });
        }
        // Own game first, then fullest games first.
        found.Sort((a, b) => (a.Id == myLobby) != (b.Id == myLobby) ? (a.Id == myLobby ? -1 : 1)
                           : b.Players != a.Players ? b.Players.CompareTo(a.Players) : a.Id.CompareTo(b.Id));
        lobbies = found;
        // Until the host publishes from this window, show what their lobby already has
        // (e.g. published from an earlier session) so a Publish doesn't wipe it.
        if (iAmHost && !infoPublished)
        {
            var mine = found.Find(l => l.Id == myLobby);
            if (mine != null)
            {
                myDifficulty.SelectedIndex = mine.Difficulty + 1;
                myLevel.SelectedIndex = Math.Max(0, mine.Level);
                myPlaystyle.SelectedIndex = mine.Playstyle + 1;
            }
        }
        Finish(null);
    }

    // Steam knows which lobby the player's running game is in (the same info friends' "Join Game" uses).
    void FindMyLobby()
    {
        myLobby = 0;
        iAmHost = false;
        ulong me = Steam.SteamAPI_ISteamUser_GetSteamID(Steam.SteamAPI_SteamUser_v023());
        Steam.FriendGameInfo info;
        if (Steam.SteamAPI_ISteamFriends_GetFriendGamePlayed(Steam.SteamAPI_SteamFriends_v017(), me, out info) &&
            info.GameId == ulong.Parse(AppId))
            myLobby = info.LobbyId;
        if (myLobby != 0)
            iAmHost = Steam.SteamAPI_ISteamMatchmaking_GetLobbyOwner(matchmaking, myLobby) == me;
        UpdateMyGame();
    }

    void UpdateMyGame()
    {
        myGameLabel.Text = myLobby == 0 ? "Your game's ID: (host or join a game, then Refresh)"
                         : "Your game's ID: " + myLobby + (iAmHost ? "  (you're the host)" : "");
        copyMyIdButton.Enabled = myLobby != 0;
    }

    void Publish()
    {
        infoPublished = true;
        if (pollTimer.Enabled) return;  // the search in progress will apply it
        if (!InitSteam()) return;
        FindMyLobby();
        if (!iAmHost)
        {
            Finish(myLobby == 0 ? "Host a game first, then click Publish." : "Only the host can set the game info.");
            return;
        }
        // Refresh straight away so the list shows the published info (this also writes it).
        StartRefresh();
    }

    // Writes the host's chosen info to their own lobby, only touching fields that changed.
    void WriteMyInfo()
    {
        if (!iAmHost) return;
        WriteField(GameInfo.DifficultyKey, myDifficulty.SelectedIndex - 1);
        WriteField(GameInfo.LevelKey, myLevel.SelectedIndex);  // index 1 = level 1
        WriteField(GameInfo.PlaystyleKey, myPlaystyle.SelectedIndex - 1);
    }

    void WriteField(string key, int value)
    {
        bool set = key == GameInfo.LevelKey ? value >= 1 : value >= 0;
        string current = Marshal.PtrToStringAnsi(Steam.SteamAPI_ISteamMatchmaking_GetLobbyData(matchmaking, myLobby, key)) ?? "";
        string wanted = set ? value.ToString() : "";
        if (current == wanted) return;
        if (set) Steam.SteamAPI_ISteamMatchmaking_SetLobbyData(matchmaking, myLobby, key, wanted);
        else Steam.SteamAPI_ISteamMatchmaking_DeleteLobbyData(matchmaking, myLobby, key);
    }

    void CopyId(ulong id)
    {
        if (id == 0) return;
        try { Clipboard.SetText(id.ToString()); SetStatus("Copied lobby ID " + id + " to the clipboard. Share it so others can use Join by ID."); }
        catch (Exception ex) { SetStatus("Could not copy to the clipboard: " + ex.Message); }
    }

    // Accepts a bare lobby ID or a full steam://joinlobby/1154030/<id>/... link.
    void JoinById()
    {
        Match m = Regex.Match(joinIdBox.Text, @"\d{15,20}");
        ulong id;
        if (!m.Success || !ulong.TryParse(m.Value, out id))
        {
            SetStatus("That doesn't look like a lobby ID. It should be a long number like 109775241759430410.");
            return;
        }
        Join(id);
    }

    // Shut Steam down after every search so Steam doesn't treat this tool as the running game.
    void Finish(string error)
    {
        pollTimer.Stop();
        if (steamUp) { Steam.SteamAPI_Shutdown(); steamUp = false; }
        refreshButton.Enabled = true;
        if (error != null) { SetStatus(error); return; }
        Populate();
    }

    void Populate()
    {
        ulong selected = list.SelectedItems.Count > 0 ? (ulong)list.SelectedItems[0].Tag : 0;
        list.BeginUpdate();
        list.Items.Clear();
        int shown = 0, totalPlayers = 0;
        int wantDifficulty = difficultyFilter.SelectedIndex - 1, wantPlaystyle = playstyleFilter.SelectedIndex - 1;
        foreach (var l in lobbies)
        {
            totalPlayers += l.Players;
            int open = Math.Max(0, l.Limit - l.Players);
            if (l.Id != myLobby)
            {
                if (hideFull.Checked && open == 0) continue;
                if (wantDifficulty >= 0 && l.Difficulty != wantDifficulty) continue;
                if (wantPlaystyle >= 0 && l.Playstyle != wantPlaystyle) continue;
            }
            var item = new ListViewItem(new[] {
                l.Players + " / " + l.Limit, open.ToString(),
                GameInfo.Name(GameInfo.Difficulties, l.Difficulty), l.Level > 0 ? l.Level.ToString() : "",
                GameInfo.Name(GameInfo.Playstyles, l.Playstyle),
                l.Id.ToString(), l.Id == myLobby ? "Your game" : "" });
            if (l.Id == myLobby) item.Font = new Font(list.Font, FontStyle.Bold);
            item.Tag = l.Id;
            list.Items.Add(item);
            if (l.Id == selected) item.Selected = true;
            shown++;
        }
        list.EndUpdate();
        joinButton.Enabled = list.SelectedItems.Count > 0;
        SetStatus(lobbies.Count + " public games online (" + totalPlayers + " players), " + shown + " shown. Updated " +
                  DateTime.Now.ToString("HH:mm:ss") + ".\nStart Titan Quest II first, then pick a game and click Join (or double-click it).");
    }

    void JoinSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        Join((ulong)list.SelectedItems[0].Tag);
    }

    void Join(ulong id)
    {
        try
        {
            Process.Start("steam://joinlobby/" + AppId + "/" + id);
            SetStatus("Asked Steam to join lobby " + id + ". Titan Quest II must already be running.");
        }
        catch (Exception ex) { SetStatus("Could not open the Steam join link: " + ex.Message); }
    }

    void SetStatus(string text) { status.Text = text; }
}

static class Program
{
    const string AppId = "1154030";
    const string DllSubPath = @"TQ2\Binaries\Win64\wrapper\THQNOnline\steam\steam_api64.dll";

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string dll = FindSteamApiDll();
        if (dll == null || Steam.LoadLibrary(dll) == IntPtr.Zero)
        {
            MessageBox.Show("Could not find your Titan Quest II installation.\n\nMake sure the game is installed through Steam.",
                "TQ2 Lobby Browser", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new MainForm());
    }

    // Looks through every Steam library folder for the Titan Quest II install.
    static string FindSteamApiDll()
    {
        var libraries = new List<string>();
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steamPath != null)
        {
            steamPath = steamPath.Replace('/', '\\');
            libraries.Add(steamPath);
            string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        foreach (string lib in libraries)
        {
            string manifest = Path.Combine(lib, @"steamapps\appmanifest_" + AppId + ".acf");
            string installDir = "Titan Quest II";
            if (File.Exists(manifest))
            {
                Match m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (m.Success) installDir = m.Groups[1].Value;
            }
            string candidate = Path.Combine(Path.Combine(lib, @"steamapps\common\" + installDir), DllSubPath);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
