# Changelog

## 1.0.0

First stable release.

**Sign in**
- **Sign in with GitHub** button: copy a code, approve on github.com, done. No terminal, no token to create.
- Uses the RunnerTray GitHub App with read-only permissions (Actions, Metadata, Self-hosted runners).
- Tokens are kept in Windows Credential Manager and refreshed silently for up to 6 months.
- The GitHub CLI login and `GITHUB_TOKEN` still work.

**Reliable and light on the GitHub API**
- One request at a time, as GitHub recommends (0.1.0 sent them in parallel).
- Conditional requests with ETags: unchanged data costs no rate limit.
- Waits for GitHub's rate limits and backs off on errors instead of retrying at once.
- Refreshes every 15 seconds while runs are active, less often when quiet, and pauses while the screen is locked.
- Refreshes at once after unlock, wake from sleep, and when the network comes back.
- Shows the last good data, marked with its time, when GitHub cannot be reached.
- Clear messages with an action for every problem: sign in, install, offline, rate limit, GitHub outage, broken settings.
- Errors never close the app; details go to a log (`%LOCALAPPDATA%\RunnerTray-data\logs`).

**New windows**
- Settings window (no more editing JSON by hand; the file still works and reloads automatically).
- Diagnostics window: token source, rate limit, requests, cache hits, recent errors, copy for bug reports.

**Install and updates**
- `RunnerTray-win-Setup.exe` installs per user (no administrator rights) and updates itself from GitHub Releases.
- `RunnerTray-win-Portable.zip` for use without installing; it updates itself too.
- Uninstalling removes the saved sign-in, the Start with Windows entry, and the cache.

**Accessibility**
- Checked with Axe.Windows (0 errors) and Windows UI Automation tests on every build.
- Screen readers announce banners and sign-in progress.

## 0.1.0

First preview.
