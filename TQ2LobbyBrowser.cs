// Titan Quest II Lobby Browser
// Lists public Steam lobbies for Titan Quest II (AppID 1154030) and joins them via steam://joinlobby.
// Uses Valve's steam_api64.dll from the player's own Titan Quest II install (nothing redistributed).
// Build: csc /target:winexe /platform:x64 /optimize+ /win32icon:icon.ico TQ2LobbyBrowser.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

// Shown in the exe's Properties > Details in Windows.
[assembly: AssemblyTitle("TQ2 Lobby Browser")]
[assembly: AssemblyProduct("TQ2 Lobby Browser")]
[assembly: AssemblyDescription("Lists public Titan Quest II Steam lobbies")]
[assembly: AssemblyCopyright("MIT License - github.com/iioaia/TQ2LobbyBrowser")]
[assembly: AssemblyVersion(Program.Version + ".0")]
[assembly: AssemblyFileVersion(Program.Version)]

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
    // ELobbyDistanceFilter: Close (same region), Default (nearby regions), Far (~half the globe), Worldwide.
    public const int DistanceClose = 0;
    public const int DistanceWorldwide = 3;
}

class Lobby
{
    public ulong Id;
    public int Players;
    public int Limit;
    public int Distance = Steam.DistanceWorldwide;  // closest Steam distance filter that returned this lobby
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

    // Indexed by Steam distance filter (Lobby.Distance).
    public static readonly string[] Distances = { "Near", "Regional", "Far", "Very far" };

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
    readonly ComboBox distanceFilter = new ComboBox();
    readonly LinkLabel updatesLink = new LinkLabel();
    readonly Timer autoTimer = new Timer();

    bool busy;            // a Steam worker process is running
    bool refreshQueued;   // refresh again when the current one finishes
    bool publishClicked;  // report "host a game first" if Publish didn't apply
    List<Lobby> lobbies = new List<Lobby>();
    ulong myLobby;
    bool iAmHost;
    bool infoPublished;  // once the host clicks Publish, their info is re-applied to every game they host

    public MainForm()
    {
        Text = "Titan Quest II - Lobby Browser v" + Program.Version;
        ClientSize = new Size(780, 600);
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
        // Opens the releases page in the browser; the tool itself never goes online.
        updatesLink.Text = "v" + Program.Version + " · Updates";
        updatesLink.AutoSize = true;
        updatesLink.Margin = new Padding(24, 7, 3, 3);
        updatesLink.LinkClicked += delegate { OpenUrl(Program.ReleasesUrl); };
        top.Controls.AddRange(new Control[] { refreshButton, joinButton, hideFull, autoRefresh, updatesLink });

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
        // Item index - 1 is the farthest distance shown (Near only = Close, ...).
        SetupCombo(distanceFilter, "Any distance", new[] { "Near only", "Up to regional", "Up to far" }, 110);
        difficultyFilter.SelectedIndexChanged += delegate { Populate(); };
        playstyleFilter.SelectedIndexChanged += delegate { Populate(); };
        distanceFilter.SelectedIndexChanged += delegate { Populate(); };
        filterRow.Controls.AddRange(new Control[] { RowLabel("Show:"), distanceFilter, difficultyFilter, playstyleFilter });

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
        list.Columns.Add("Distance", 75);
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

        autoTimer.Interval = 30000;
        autoTimer.Tick += delegate { if (!busy) StartRefresh(); };

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

    // Steam treats any process that connected as Titan Quest II as the running game until that
    // process exits, so the window never connects itself: each refresh runs in a short-lived copy
    // of this exe (see SteamWorker) that prints the results and exits.
    void StartRefresh()
    {
        if (busy) { refreshQueued = true; return; }
        busy = true;
        refreshQueued = false;
        refreshButton.Enabled = false;
        SetStatus("Searching for games...");
        string args = "--steam-worker";
        if (infoPublished)
            args += " " + (myDifficulty.SelectedIndex - 1) + " " + myLevel.SelectedIndex + " " + (myPlaystyle.SelectedIndex - 1);
        var thread = new System.Threading.Thread(() =>
        {
            string output = null, error = null;
            try { output = RunWorker(args); }
            catch (Exception ex) { error = ex.Message; }
            BeginInvoke((MethodInvoker)delegate { OnWorkerDone(output, error); });
        });
        thread.IsBackground = true;
        thread.Start();
    }

    static string RunWorker(string args)
    {
        var psi = new ProcessStartInfo(Application.ExecutablePath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        psi.EnvironmentVariables["SteamAppId"] = AppId;
        psi.EnvironmentVariables["SteamGameId"] = AppId;
        using (var p = Process.Start(psi))
        {
            var read = p.StandardOutput.ReadToEndAsync();
            if (!read.Wait(60000))  // up to four Steam searches, one per distance
            {
                try { p.Kill(); } catch { }
                throw new Exception("Steam did not answer in time. Try Refresh again.");
            }
            return read.Result;
        }
    }

    void OnWorkerDone(string output, string error)
    {
        busy = false;
        refreshButton.Enabled = true;
        ulong lobby = 0;
        bool host = false;
        var found = new List<Lobby>();
        // Steam also prints its own messages to the worker's output; only our prefixed lines count.
        foreach (string line in (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = line.Split(' ');
            if (f[0] == "ERR") error = line.Substring(4);
            else if (f[0] == "ME" && f.Length == 3) { ulong.TryParse(f[1], out lobby); host = f[2] == "1"; }
            else if (f[0] == "L" && f.Length == 8)
                found.Add(new Lobby
                {
                    Id = ulong.Parse(f[1]), Players = int.Parse(f[2]), Limit = int.Parse(f[3]), Distance = int.Parse(f[4]),
                    Difficulty = int.Parse(f[5]), Level = int.Parse(f[6]), Playstyle = int.Parse(f[7])
                });
        }
        if (error != null)
        {
            SetStatus(error);
            if (refreshQueued) StartRefresh();
            return;
        }
        myLobby = lobby;
        iAmHost = host;
        UpdateMyGame();
        // Own game first, then nearest, then fullest.
        found.Sort((a, b) => (a.Id == myLobby) != (b.Id == myLobby) ? (a.Id == myLobby ? -1 : 1)
                           : a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
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
        Populate();
        if (publishClicked && !iAmHost)
            SetStatus(myLobby == 0 ? "Host a multiplayer game first, then click Publish." : "Only the host can set the game info.");
        publishClicked = false;
        if (refreshQueued) StartRefresh();
    }

    void UpdateMyGame()
    {
        myGameLabel.Text = myLobby == 0 ? "Your game's ID: (host or join a game, then Refresh)"
                         : "Your game's ID: " + myLobby + (iAmHost ? "  (you're the host)" : "");
        copyMyIdButton.Enabled = myLobby != 0;
    }

    // The worker writes the info (host only) during the refresh, so the list shows it straight away.
    void Publish()
    {
        infoPublished = true;
        publishClicked = true;
        StartRefresh();
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

    void Populate()
    {
        ulong selected = list.SelectedItems.Count > 0 ? (ulong)list.SelectedItems[0].Tag : 0;
        list.BeginUpdate();
        list.Items.Clear();
        int shown = 0, totalPlayers = 0;
        int wantDifficulty = difficultyFilter.SelectedIndex - 1, wantPlaystyle = playstyleFilter.SelectedIndex - 1;
        int maxDistance = distanceFilter.SelectedIndex - 1;
        foreach (var l in lobbies)
        {
            totalPlayers += l.Players;
            int open = Math.Max(0, l.Limit - l.Players);
            if (l.Id != myLobby)
            {
                if (hideFull.Checked && open == 0) continue;
                if (wantDifficulty >= 0 && l.Difficulty != wantDifficulty) continue;
                if (wantPlaystyle >= 0 && l.Playstyle != wantPlaystyle) continue;
                if (maxDistance >= 0 && l.Distance > maxDistance) continue;
            }
            var item = new ListViewItem(new[] {
                l.Players + " / " + l.Limit, open.ToString(), GameInfo.Name(GameInfo.Distances, l.Distance),
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
        // With the game closed, Steam has nothing to hand the join to.
        if (Process.GetProcessesByName("TQ2-Win64-Shipping").Length == 0)
        {
            MessageBox.Show(this, "Titan Quest II isn't running.\n\nStart the game first, then click Join again.",
                "TQ2 Lobby Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (OpenUrl("steam://joinlobby/" + AppId + "/" + id))
            SetStatus("Joining lobby " + id + "... In the game, pick your character on the character select screen and click Join.");
    }

    bool OpenUrl(string url)
    {
        try { Process.Start(url); return true; }
        catch (Exception ex) { SetStatus("Could not open " + url + ": " + ex.Message); return false; }
    }

    void SetStatus(string text) { status.Text = text; }
}

// Runs in a short-lived child process (TQ2LobbyBrowser.exe --steam-worker [difficulty level playstyle]):
// connects to Steam, finds the player's own lobby, writes their game info if given (host only),
// prints the lobby list and exits. Exiting is what tells Steam the "game" has stopped.
// Output lines: "ME <lobby> <host 0/1>", "L <id> <players> <limit> <difficulty> <level> <playstyle>", "ERR <message>".
static class SteamWorker
{
    const ulong AppId = 1154030;

    public static int Run(string[] args)
    {
        var err = new byte[1024];
        if (Steam.SteamAPI_InitFlat(err) != 0)
        {
            Console.WriteLine("ERR Could not connect to Steam (" + Encoding.UTF8.GetString(err).TrimEnd('\0') +
                              "). Make sure Steam is running and you own Titan Quest II.");
            return 1;
        }
        try { return Search(args); }
        finally { Steam.SteamAPI_Shutdown(); }
    }

    static int Search(string[] args)
    {
        IntPtr mm = Steam.SteamAPI_SteamMatchmaking_v009();
        IntPtr utils = Steam.SteamAPI_SteamUtils_v010();

        // Steam knows which lobby the player's running game is in (the same info friends' "Join Game" uses).
        ulong me = Steam.SteamAPI_ISteamUser_GetSteamID(Steam.SteamAPI_SteamUser_v023());
        ulong myLobby = 0;
        Steam.FriendGameInfo info;
        if (Steam.SteamAPI_ISteamFriends_GetFriendGamePlayed(Steam.SteamAPI_SteamFriends_v017(), me, out info) && info.GameId == AppId)
            myLobby = info.LobbyId;
        bool host = myLobby != 0 && Steam.SteamAPI_ISteamMatchmaking_GetLobbyOwner(mm, myLobby) == me;
        Console.WriteLine("ME " + myLobby + " " + (host ? 1 : 0));

        int difficulty, level, playstyle;
        if (host && args.Length == 4 && int.TryParse(args[1], out difficulty) &&
            int.TryParse(args[2], out level) && int.TryParse(args[3], out playstyle))
        {
            WriteField(mm, myLobby, GameInfo.DifficultyKey, difficulty, 0);
            WriteField(mm, myLobby, GameInfo.LevelKey, level, 1);
            WriteField(mm, myLobby, GameInfo.PlaystyleKey, playstyle, 0);
        }

        // Steam has no ping for lobbies, but it can filter by region distance (based on each player's location).
        // Searching from nearest to worldwide tags every lobby with the closest filter that finds it.
        var distance = new Dictionary<ulong, int>();
        var order = new List<ulong>();
        for (int filter = Steam.DistanceClose; filter <= Steam.DistanceWorldwide; filter++)
        {
            Steam.SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter(mm, filter);
            Steam.SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter(mm, 500);
            ulong call = Steam.SteamAPI_ISteamMatchmaking_RequestLobbyList(mm);
            DateTime deadline = DateTime.Now.AddSeconds(15);
            bool failed;
            while (!Steam.SteamAPI_ISteamUtils_IsAPICallCompleted(utils, call, out failed))
            {
                Steam.SteamAPI_RunCallbacks();
                if (DateTime.Now > deadline)
                {
                    Console.WriteLine("ERR Steam did not answer in time. Try Refresh again.");
                    return 1;
                }
                System.Threading.Thread.Sleep(50);
            }
            uint count = 0;
            if (!Steam.SteamAPI_ISteamUtils_GetAPICallResult(utils, call, ref count, 4, Steam.LobbyMatchListCallback, out failed) || failed)
            {
                Console.WriteLine("ERR The lobby search failed. Try Refresh again.");
                return 1;
            }
            for (int i = 0; i < count; i++)
            {
                ulong id = Steam.SteamAPI_ISteamMatchmaking_GetLobbyByIndex(mm, i);
                if (!distance.ContainsKey(id)) { distance[id] = filter; order.Add(id); }
            }
        }
        // Read lobby details after the last (worldwide) search, which returned every lobby.
        foreach (ulong id in order)
        {
            Console.WriteLine("L " + id + " " +
                Steam.SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(mm, id) + " " +
                Steam.SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit(mm, id) + " " +
                distance[id] + " " +
                GameInfo.Read(mm, id, GameInfo.DifficultyKey, 0, GameInfo.Difficulties.Length - 1) + " " +
                GameInfo.Read(mm, id, GameInfo.LevelKey, 1, GameInfo.MaxLevel) + " " +
                GameInfo.Read(mm, id, GameInfo.PlaystyleKey, 0, GameInfo.Playstyles.Length - 1));
        }
        return 0;
    }

    // Writes one field of the host's game info, only if it changed. A value below min clears the field.
    static void WriteField(IntPtr mm, ulong lobby, string key, int value, int min)
    {
        string current = Marshal.PtrToStringAnsi(Steam.SteamAPI_ISteamMatchmaking_GetLobbyData(mm, lobby, key)) ?? "";
        string wanted = value >= min ? value.ToString() : "";
        if (current == wanted) return;
        if (wanted != "") Steam.SteamAPI_ISteamMatchmaking_SetLobbyData(mm, lobby, key, wanted);
        else Steam.SteamAPI_ISteamMatchmaking_DeleteLobbyData(mm, lobby, key);
    }
}

static class Program
{
    // Release workflow checks that the version it's given matches this.
    public const string Version = "1.4.0";
    public const string ReleasesUrl = "https://github.com/iioaia/TQ2LobbyBrowser/releases/latest";
    const string AppId = "1154030";
    const string DllSubPath = @"TQ2\Binaries\Win64\wrapper\THQNOnline\steam\steam_api64.dll";

    [STAThread]
    static int Main(string[] args)
    {
        bool worker = args.Length > 0 && args[0] == "--steam-worker";
        string dll = FindSteamApiDll();
        if (dll == null || Steam.LoadLibrary(dll) == IntPtr.Zero)
        {
            const string msg = "Could not find your Titan Quest II installation. Make sure the game is installed through Steam.";
            if (worker) Console.WriteLine("ERR " + msg);
            else MessageBox.Show(msg, "TQ2 Lobby Browser", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        if (worker) return SteamWorker.Run(args);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
        return 0;
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
