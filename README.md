# RunnerTray

GitHub Actions runs and self-hosted runners in your Windows system tray.

![RunnerTray popup above the Windows taskbar](docs/hero.png)

RunnerTray is a small tray app for Windows 10 and 11. The tray icon shows the
state of your CI at a glance. Click it to open a panel with the details. It does
not open a console window or a browser tab.

The screenshots use made-up demo data (`RunnerTray.exe --demo`).

## Features

- **Tray icon that shows the state with a shape and a colour.** Check: all
  passing. Play: jobs running. Cross: the latest run of a workflow failed.
  Exclamation mark: GitHub cannot be reached.

  ![Tray icon states](docs/tray-states.png)

- **Recent runs** across all your repositories (or a list you choose), with
  repository, workflow, branch, trigger, author, and age. Running jobs show a
  live timer. Click a run to open it on GitHub.
- **Self-hosted runners** of an organization: online, busy, or offline, with
  their labels.
- **This machine:** if your runners run in Docker containers on this PC,
  RunnerTray shows which repository and job each container is running, with a
  live timer.
- **Notifications** when a run fails (optional: when a run passes). Click the
  notification to open the run.

  ![Notification for a failed run](docs/notification.png)

- **Start with Windows** from the right-click menu.

<img src="docs/popup.png" alt="The RunnerTray panel: summary, jobs on this machine, and recent runs" width="400">

## Accessibility

RunnerTray follows [WCAG 2.2](https://www.w3.org/TR/WCAG22/) level AA where it
applies to a desktop app.

| WCAG | What RunnerTray does |
|---|---|
| 1.4.1 Use of colour | Every status has a shape (check, cross, play, clock, dash, exclamation mark) and words, not only a colour. |
| 1.4.3 Contrast (minimum) | All text has a contrast ratio of at least 4.5:1. Body text is 16:1, secondary text 6.1:1. |
| 1.4.11 Non-text contrast | Status badges and the focus ring have at least 3:1 against the background. |
| 1.4.4 Resize text | Follows **Settings > Accessibility > Text size** (up to 225%) and display scaling. |
| 1.4.10 Reflow | The summary line wraps when the text does not fit. |
| Windows high contrast | In high contrast mode (contrast themes), RunnerTray uses your system colours. |
| 2.1.1 Keyboard | Everything works with the keyboard (see [Keyboard](#keyboard)). |
| 2.4.3, 2.4.7 Focus order and focus visible | Focus moves top to bottom and has a clear ring. |
| 4.1.2 Name, role, value | Each row tells screen readers its status, repository, workflow, branch, and time, for example "Failed. atlas-mobile, Android release. release/3.2, push, jlee. 8m ago". |
| 2.2.2 Pause, stop, hide | Nothing flashes or animates. Only the timers count up. |

### Keyboard

| Key | Action |
|---|---|
| Win+B, then arrow keys | Move to the RunnerTray icon in the tray |
| Shift+F10 or the Menu key | Open the menu, then select **Open** |
| Tab, Shift+Tab, Up, Down | Move between runs |
| Enter or Space | Open the selected run on GitHub |
| F5 | Refresh now |
| Esc | Close the panel |

## Install

1. Download the latest release from the
   [Releases page](../../releases):
   - `RunnerTray-win-x64.zip`: one `.exe`, nothing else to install.
   - `RunnerTray-win-x64-small.zip`: smaller, but needs the
     [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Extract the zip to a folder, for example `C:\Tools\RunnerTray`.
3. Sign in to GitHub. RunnerTray uses the [GitHub CLI](https://cli.github.com/)
   token, so run this once:

   ```bash
   gh auth login
   ```

   To see an organization's self-hosted runners, the token also needs the
   `admin:org` scope:

   ```bash
   gh auth refresh -h github.com -s admin:org
   ```

   If you do not use the GitHub CLI, set the `GITHUB_TOKEN` environment variable
   to a token instead.
4. Start `RunnerTray.exe`. The icon appears in the tray. Windows can hide new
   tray icons behind the `^` arrow; drag the icon onto the taskbar to keep it
   visible.
5. Right-click the icon and select **Start with Windows** to start it when you
   sign in.

Try it without a GitHub account:

```bash
RunnerTray.exe --demo
```

## Use

| Action | Result |
|---|---|
| Left-click the tray icon | Open or close the panel (keyboard: see [Keyboard](#keyboard)) |
| Click a run | Open the run on GitHub |
| Click a busy runner on this machine | Open the repository's Actions page |
| **Refresh** in the panel, or F5 | Refresh now |
| Esc, or click elsewhere | Close the panel |
| Right-click the tray icon | Menu: refresh, open in GitHub, settings, start with Windows, exit |

## Configuration

The first start creates `%APPDATA%\RunnerTray\config.json`. Open it from
**right-click → Settings**, save your changes, then restart RunnerTray.

```json
{
  "owner": "my-org",
  "repos": [],
  "lookbackDays": 2,
  "runCount": 15,
  "refreshSeconds": 60,
  "showSelfHostedRunners": true,
  "runnerNameFilter": "",
  "dockerJobLabel": "",
  "dockerWorkDir": "/actions-runner/_work",
  "notifyOnFailure": true,
  "notifyOnSuccess": false
}
```

| Setting | Default | Meaning |
|---|---|---|
| `owner` | your user | GitHub user or organization to watch. |
| `repos` | all | Repositories to watch, for example `["web-app", "api"]`. Empty: every repository with a push in the last `lookbackDays` days. |
| `lookbackDays` | `2` | How far back to look for active repositories. |
| `runCount` | `15` | Number of runs in the panel. |
| `refreshSeconds` | `60` | How often to ask GitHub for new data (minimum 15). |
| `showSelfHostedRunners` | `true` | Show the organization's self-hosted runners. Needs `admin:org`. |
| `runnerNameFilter` | all | Only show runners whose name contains this text. |
| `dockerJobLabel` | off | Docker label of runner containers on this PC, for example `ci-job=build-box`. Turns on the **This machine** section. |
| `dockerWorkDir` | `/actions-runner/_work` | Runner work folder inside those containers. |
| `notifyOnFailure` | `true` | Notification when a run fails. |
| `notifyOnSuccess` | `false` | Notification when a run passes. |

GitHub allows 5,000 API requests per hour. RunnerTray makes one request per
watched repository per refresh, so 20 repositories at 60 seconds use about
1,200 requests per hour.

### Runner containers on this PC

If you run GitHub Actions runners in Docker on the same PC (for example with
[myoung34/docker-github-actions-runner](https://github.com/myoung34/docker-github-actions-runner)
or your own launcher), give the containers a label and set `dockerJobLabel` to
it. RunnerTray reads the runner log (`Running job: …`) and the checked-out
folder in `dockerWorkDir` to show the repository and job. It refreshes this
every 5 seconds. Docker must be on your `PATH`.

## Privacy

RunnerTray talks only to `api.github.com`, and to your local Docker engine if
you turn that on. It reads your token from the GitHub CLI or `GITHUB_TOKEN` at
start and keeps it in memory only. It stores nothing except `config.json`.

## Build from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```bash
git clone https://github.com/azharudh33n/runner-tray.git
```

```bash
cd runner-tray
```

```bash
dotnet run --project src/RunnerTray -- --demo
```

Publish a single `.exe`:

```bash
dotnet publish src/RunnerTray -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Render the README images from demo data:

```bash
dotnet run --project src/RunnerTray -- --screenshot docs
```

Recreate the app icon: `pwsh tools/make-icon.ps1`.

## Project layout

| Path | Contents |
|---|---|
| `src/RunnerTray/TrayApp.cs` | Tray icon, timers, notifications, menu |
| `src/RunnerTray/PopupForm.cs` | The panel |
| `src/RunnerTray/GitHubSource.cs` | GitHub REST API client |
| `src/RunnerTray/LocalDocker.cs` | Runner containers on this PC |
| `src/RunnerTray/DemoSource.cs` | Demo data for `--demo` and screenshots |
| `src/RunnerTray/AppConfig.cs` | `config.json` |

## License

[MIT](LICENSE)
