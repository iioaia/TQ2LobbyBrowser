# Titan Quest II Lobby Browser

A tiny, unofficial server browser for **Titan Quest II** on Steam. It shows every public multiplayer game that's open right now and lets you join one with a double-click.

![Screenshot](screenshot.png)

## Download

Get **`TQ2LobbyBrowser.exe`** from the [latest release](../../releases/latest). That's the only file you need.

## Requirements

- Windows 10 or 11
- Steam running and logged in
- Titan Quest II installed through Steam

You don't need to install anything else. No Python or extra runtime is required, and the browser doesn't need to be in the game folder.

## How to use

1. **Start Titan Quest II** and go to the main menu.
2. Run `TQ2LobbyBrowser.exe`. It searches for open games right away.
3. Pick a game and click **Join selected**, or double-click it. Steam hands the join to the game.
4. The game loads and takes you to the **character select** screen. **Pick your character and click Join** there to enter the host's game.

| Option | What it does |
| --- | --- |
| **Refresh** | Searches again for open games worldwide |
| **Hide full games** | Hides games that have no free slots (on by default) |
| **Auto-refresh (30s)** | Refreshes the list every 30 seconds |
| **Join by ID** | Paste a lobby ID (or a `steam://joinlobby/...` link) and click **Join** |
| **Copy my ID** | Copies the ID of the game you're hosting or playing in |
| **Right-click / Ctrl+C** | Copies the ID of the selected game in the list |
| **Your game info** | Hosts can set Difficulty, Level and Playstyle, then click **Publish** |
| **Show:** | Only show games with a certain difficulty or playstyle |

### Game info (difficulty, level, playstyle)

If you're hosting a multiplayer game, you can tell other browser users what kind of game it is:

1. Host a **multiplayer** game (not solo) and click **Refresh**. The browser should say **(you're the host)**.
2. Pick any of **Difficulty** (Neophyte / Normal / Hardcore), **Level** and **Playstyle** (Story / Farming / Questing / Help Wanted). Every field is optional.
3. Click **Publish**. Your info shows in everyone's list, and they can filter by it.

While the browser stays open, your info is re-applied automatically when you host a new game. Set a field back to **–** and click Publish to clear it.

There are no free-text fields, only fixed choices. Values the browser doesn't recognize are never shown, so the info can't be used for spam. The info is attached to your Steam lobby and disappears when the game closes. Only people using this browser can see it; the game itself ignores it.

### Playing with people who aren't on your Steam friends list

You don't need to add anyone as a Steam friend:

1. **Host:** start a multiplayer game set to **Public**, then click **Refresh** in the browser. Your game shows at the top of the list as **Your game**. Click **Copy my ID** and paste the ID into Discord, a forum or a chat.
2. **Everyone else:** start Titan Quest II, paste the ID into **Join by ID** and click **Join**. When the game shows character select, pick your character and click **Join**.

The host's game needs to be **Public**. Steam doesn't let strangers into friends-only or private lobbies, even if they have the ID.

### "Windows protected your PC"

The exe isn't code-signed, so Windows SmartScreen may warn you the first time you run it. Click **More info**, then **Run anyway**. If you'd rather not, you can build it yourself from the source (see below).

## What it can and can't show

The game only publishes a lobby ID and the player count for each game. That means the browser can't show the host's name, difficulty, level or quest progress. Friends-only and private games are hidden by Steam, so only public games appear.

## How it works

- It finds your Titan Quest II install through Steam and loads Valve's `steam_api64.dll` from the game's own folder. No game or Steam files are included or modified.
- It asks Steam for the game's public lobbies (AppID `1154030`), worldwide.
- **Join** opens a `steam://joinlobby/1154030/<lobby id>` link, which is the same thing that happens when you accept a friend's invite.
- It only connects to Steam while searching and disconnects afterwards. That way Steam won't think the game is already running when you click Play.

## Safety

The browser is a small, open-source program. Here's exactly what it does and doesn't do:

- **No admin rights needed.** It runs as a normal program and never asks for administrator access.
- **Doesn't touch the game.** It doesn't modify, patch or inject anything into Titan Quest II, and it doesn't change any game files. It loads Valve's own `steam_api64.dll` from your game folder, the same official Steam library the game uses, to talk to Steam.
- **Writes nothing to your PC.** No files, no settings and no registry changes. The only thing it reads is Steam's install location from the registry and Steam's library list, to find your game folder.
- **No internet connections of its own.** It only talks to the Steam client already running on your PC. There are no servers, no telemetry and no auto-updates.
- **Nothing to install or uninstall.** To remove it, delete the exe.
- **VirusTotal:** [65 of 70 engines report it clean](https://www.virustotal.com/gui/file/2db32ea0ac8c33c1b2e813e3f7eea892ac6b138873c0e72165297c8fe0572c56) (v1.2.1). The 5 detections come from reputation and machine-learning scanners that flag new, unsigned programs, not from anything the tool does. Microsoft Defender, Kaspersky, ESET, Bitdefender and Norton report it clean. The McAfee detection has been reported to McAfee as a false positive.
- **All the code is here.** It's one readable file, [`TQ2LobbyBrowser.cs`](TQ2LobbyBrowser.cs), and you can [build the exe yourself](#building-from-source) with the compiler that comes with Windows instead of downloading it.

## Privacy

The browser doesn't collect or share any personal information.

- **No servers, no telemetry.** It only talks to the Steam client running on your PC, using Steam's normal lobby system (the same one the game uses). It has no internet code of its own.
- **What hosts share:** only the optional Difficulty, Level and Playstyle, and only after clicking **Publish**. They're stored as small numbers on the host's Steam lobby and disappear when the game closes. There's no free text.
- **Lobby IDs** are random numbers Steam gives each game session. They don't identify you, and they stop working when the game closes.
- **Your Steam ID** is read only on your own PC to find which lobby is yours. It's never shown, copied or sent anywhere.
- **Other players' names and Steam IDs** aren't shown in the browser.

Good to know (this is how the game and Steam work, not the browser):

- **Joining a game is a normal Titan Quest II multiplayer session.** The people in it see your character and Steam name, the same as joining through a friend invite. The game connects through Steam's relay network rather than direct IP connections.
- **Public means public.** Anyone with the browser, or with an ID you post online, can join a Public game. To play only with people you know, use the game's Friends or Private setting. Those games don't appear in the browser.

## Verifying a download

Starting with the first release after v1.2.1, release exes are built by GitHub Actions directly from the source code in this repository, not on anyone's PC. Each one comes with a [build attestation](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations): a record signed by GitHub that ties the exact file to the commit and workflow ([`build.yml`](.github/workflows/build.yml)) that produced it. To check a download with the [GitHub CLI](https://cli.github.com/):

```bat
gh attestation verify TQ2LobbyBrowser.exe --repo iioaia/TQ2LobbyBrowser
```

Each release also lists the exe's SHA-256 checksum. Windows' built-in compiler stamps every build with the time it was built, so an exe you build yourself won't have the same checksum. The attestation is the proof that a release matches the code.

## Building from source

There's one source file, and the compiler that ships with Windows can build it:

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /platform:x64 /optimize+ /out:TQ2LobbyBrowser.exe TQ2LobbyBrowser.cs
```

## Disclaimer

This is a fan-made tool. It isn't affiliated with or endorsed by THQ Nordic, Grimlore Games or Valve. A game update could change how multiplayer lobbies work and stop it from working.

## License

[MIT](LICENSE). You're free to use, change and share it.
