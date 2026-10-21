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
    [DllImport(Dll)] public static extern void SteamAPI_ISteamMatchmaking_AddRequestLobbyListDistanceFilter(IntPtr self, int filter);
    [DllImport(Dll)] public static extern void SteamAPI_ISteamMatchmaking_AddRequestLobbyListResultCountFilter(IntPtr self, int max);
    [DllImport(Dll)] public static extern ulong SteamAPI_ISteamMatchmaking_RequestLobbyList(IntPtr self);
    [DllImport(Dll)] public static extern ulong SteamAPI_ISteamMatchmaking_GetLobbyByIndex(IntPtr self, int index);
    [DllImport(Dll)] public static extern int SteamAPI_ISteamMatchmaking_GetNumLobbyMembers(IntPtr self, ulong lobby);
    [DllImport(Dll)] public static extern int SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit(IntPtr self, ulong lobby);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUtils_IsAPICallCompleted(IntPtr self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);

    [DllImport(Dll)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool SteamAPI_ISteamUtils_GetAPICallResult(IntPtr self, ulong call, ref uint result, int size, int callbackId, [MarshalAs(UnmanagedType.I1)] out bool failed);

    public const int LobbyMatchListCallback = 510;
    public const int DistanceWorldwide = 3;
}

class Lobby
{
    public ulong Id;
    public int Players;
    public int Limit;
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
    readonly Timer pollTimer = new Timer();
    readonly Timer autoTimer = new Timer();

    IntPtr matchmaking, utils;
    ulong pendingCall;
    DateTime pendingDeadline;
    bool steamUp;
    List<Lobby> lobbies = new List<Lobby>();

    public MainForm()
    {
        Text = "Titan Quest II - Lobby Browser";
        ClientSize = new Size(520, 480);
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

        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.Columns.Add("Players", 80);
        list.Columns.Add("Open slots", 90);
        list.Columns.Add("Lobby ID", 220);
        list.SelectedIndexChanged += delegate { joinButton.Enabled = list.SelectedItems.Count > 0; };
        list.DoubleClick += delegate { JoinSelected(); };

        status.Dock = DockStyle.Bottom;
        status.Height = 44;
        status.Padding = new Padding(6, 4, 6, 4);
        status.Text = "Start Titan Quest II first, then pick a game and click Join (or double-click it).";

        Controls.Add(list);
        Controls.Add(top);
        Controls.Add(status);

        pollTimer.Interval = 50;
        pollTimer.Tick += delegate { Poll(); };
        autoTimer.Interval = 30000;
        autoTimer.Tick += delegate { if (!pollTimer.Enabled) StartRefresh(); };

        Shown += delegate { StartRefresh(); };
    }

    void StartRefresh()
    {
        Environment.SetEnvironmentVariable("SteamAppId", AppId);
        Environment.SetEnvironmentVariable("SteamGameId", AppId);
        var err = new byte[1024];
        int rc = Steam.SteamAPI_InitFlat(err);
        if (rc != 0)
        {
            string msg = Encoding.UTF8.GetString(err).TrimEnd('\0');
            SetStatus("Could not connect to Steam (" + msg + "). Make sure Steam is running and you own Titan Quest II.");
            return;
        }
        steamUp = true;
        matchmaking = Steam.SteamAPI_SteamMatchmaking_v009();
        utils = Steam.SteamAPI_SteamUtils_v010();
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
                Limit = Steam.SteamAPI_ISteamMatchmaking_GetLobbyMemberLimit(matchmaking, id)
            });
        }
        found.Sort((a, b) => b.Players != a.Players ? b.Players.CompareTo(a.Players) : a.Id.CompareTo(b.Id));
        lobbies = found;
        Finish(null);
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
        foreach (var l in lobbies)
        {
            totalPlayers += l.Players;
            int open = Math.Max(0, l.Limit - l.Players);
            if (hideFull.Checked && open == 0) continue;
            var item = new ListViewItem(new[] { l.Players + " / " + l.Limit, open.ToString(), l.Id.ToString() });
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
        ulong id = (ulong)list.SelectedItems[0].Tag;
        try { Process.Start("steam://joinlobby/" + AppId + "/" + id); }
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
