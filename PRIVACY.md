# Privacy

Last updated: 14 November 2026

RunnerTray is a desktop app. It has no server and no account of its own.

## What RunnerTray collects

**Nothing.** RunnerTray sends no telemetry, analytics, crash reports or usage data to anyone.

## What it reads, and where it goes

| Data | Where it comes from | Where it is kept | Who else gets it |
|---|---|---|---|
| Workflow runs, repository names, self-hosted runner status | GitHub API (read-only) | Memory, and a cache on your PC | Nobody |
| Your GitHub sign-in (token) | GitHub, when you sign in | Windows Credential Manager on your PC | Nobody |
| Settings | You | `%APPDATA%\RunnerTray\config.json` | Nobody |
| Cache, last data, logs | RunnerTray | `%LOCALAPPDATA%\RunnerTray-data` (logs kept 7 days, never contain tokens) | Nobody |
| Runner containers on your PC (optional) | Docker on your PC | Memory | Nobody |

RunnerTray connects only to `github.com` and `api.github.com`, plus Docker on your PC if you turn it
on, and GitHub Releases to check for updates.

## The RunnerTray GitHub App

When you sign in, GitHub shows the permissions RunnerTray asks for: Actions (read), Metadata (read),
and organization Self-hosted runners (read). GitHub's own
[privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)
applies to your use of GitHub.

## The website

The website (https://runnertray.wx8.org/) is hosted on Cloudflare. It uses
[Cloudflare Web Analytics](https://www.cloudflare.com/web-analytics/), which counts page views
without cookies and without tracking you across sites. The site sets no cookies and loads no
third-party fonts or scripts other than that counter. The download links redirect to GitHub
Releases, where GitHub's privacy statement applies.

## Removing your data

- **Uninstall RunnerTray** (tray menu or Settings > Apps): removes the sign-in, cache and logs.
  Choose "Keep my settings" off to also remove your settings.
- **Revoke GitHub access:** [GitHub > Settings > Applications](https://github.com/settings/apps/authorizations).

## Contact

Questions: open an issue at https://github.com/azharudh33n/runner-tray/issues.
