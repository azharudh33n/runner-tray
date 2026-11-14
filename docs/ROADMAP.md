# RunnerTray roadmap

Last updated: 14 November 2026

This roadmap compares RunnerTray with similar build-status tools, lists what a production desktop
app needs, and orders the work into releases. Items marked ✔ are done.

## 1. Comparable products

| Capability | RunnerTray 1.0 | CatLight | CCMenu | BuildNotify |
|---|---|---|---|---|
| Platform | Windows | Windows, macOS, Linux | macOS | Windows, macOS, Linux (Python) |
| GitHub Actions | ✔ | ✔ | ✔ (since 2024) | ✔ (3.0) |
| Other CI systems | — | Azure DevOps, GitLab, Jenkins, TeamCity, more | GitLab, cctray feeds | cctray feeds |
| Tray/menu bar status | ✔ shape + colour | ✔ colour | ✔ | ✔ optional shapes |
| Failure notifications | ✔ | ✔ | ✔ start and finish | ✔ fixed, broken, still failing |
| Estimated time left | — | ✔ | ✔ | — |
| Who broke the build | — | ✔ | — | — |
| "I'm investigating" for teams | — | ✔ | — | — |
| Self-hosted runner status | ✔ | — | — | — |
| Runners in local Docker | ✔ | — | — | — |
| Read-only sign-in | ✔ GitHub App, 3 read permissions | — | ✔ read operations | token |
| Accessibility tested (WCAG 2.2 AA) | ✔ | — | — | — |
| Open source | ✔ MIT | — | ✔ | ✔ |

Sources: [CatLight GitHub Actions](https://catlight.io/a/github-actions-desktop-notifications),
[CatLight features](https://catlight.io/a/features), [CCMenu](https://ccmenu.org/),
[BuildNotify](https://pypi.org/project/BuildNotify/3.0.0/). "—" means not found in their public
material.

**Where RunnerTray leads:** self-hosted and local runners, read-only GitHub App sign-in,
accessibility, open source on Windows.
**Gaps to close:** time estimates, who broke it, start/fixed/still-failing notifications, filters,
job-level detail.

## 2. What a production desktop app needs

| Area | Needed | Status |
|---|---|---|
| **Install** | Per-user installer, no admin; portable option | ✔ Velopack |
| | Silent install and uninstall for IT (`--silent`) | ✔ (document it) |
| | winget, Scoop, Chocolatey | Planned 1.0–1.1 |
| | Code signing (no SmartScreen warning) | Planned: SignPath Foundation |
| **Update** | Automatic updates, delta packages | ✔ |
| | Stable and beta channels, "What's new" after updating | 1.1 |
| **Uninstall** | Removes app, sign-in, startup entry, data; optional settings | Partly ✔; tray menu entry and tests in 1.0 |
| **Sign-in** | Device flow, no secret, token refresh, sign out, revoke link | ✔ (live test pending) |
| | Several accounts, GitHub Enterprise Server | 1.2 |
| **Reliability** | Serial requests, ETags, rate limits, backoff, offline, sleep/resume | ✔ |
| | 24-hour soak test (memory, handles, CPU) | 1.0 |
| **Performance budget** | Start < 1 s, memory < 80 MB, ~0 % CPU when idle | Measure in 1.0 |
| **Accessibility** | Automated (Axe.Windows, UI Automation) | ✔ |
| | Manual: Narrator, high contrast, 200 % text, keyboard only | 1.0 |
| **Security** | CodeQL, Dependabot, secret scanning | ✔ |
| | SECURITY.md, SBOM, release checksums, build provenance attestation | 1.0 |
| **Privacy** | No telemetry; privacy page | Privacy page in 1.0 |
| | Opt-in crash reports | Later, only if needed |
| **Support** | Diagnostics window, log folder, issue templates | Diagnostics ✔; templates in 1.0 |
| **Docs** | README, website, brand guidelines, trademark policy | ✔ |
| | Troubleshooting and FAQ pages, admin guide | 1.1 |
| **Community** | CONTRIBUTING, Code of Conduct, issue and PR templates, Discussions | 1.0 |
| **Legal** | MIT licence, trademark policy, third-party notices | Licence and trademarks ✔; notices in 1.0 |
| **Brand** | Logo system, guidelines, app/tray/web icons, splash | ✔ |
| **Reach** | SEO site, social image, topics | ✔ |
| | Search Console, Bing, GitHub Marketplace, launch posts, comparison page | 1.0–1.1 |

## 3. Releases

### 1.0: ship it (now)

Blocking, in this order:

1. ✔ Brand: Track logo, guidelines, trademark policy, icons, splash, website, screenshots.
2. Uninstall done properly: "Uninstall RunnerTray…" in the tray menu (asks, offers to keep
   settings); the portable version's "Remove RunnerTray"; Start with Windows uses the stable
   launcher; README and website explain it.
3. CI test on a clean Windows runner: install → update → uninstall, then check nothing is left
   (folders, registry, Credential Manager, startup entry).
4. Re-apply the CodeQL fixes that did not land; mark v0.1.0 as a pre-release.
5. Live sign-in test from the tray menu with a real account (owner, 2 minutes).
6. Community and legal files: CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, issue and PR templates,
   THIRD-PARTY-NOTICES, privacy page.
7. Release checks: SHA-256 checksums and GitHub build provenance attestation for every file.
8. Manual accessibility pass and a 24-hour soak test.
9. Tag v1.0.0; verify downloads, website and README links; install the real 1.0.0.
10. winget manifest (submitted with the owner's OK); Search Console and Bing (owner).

### 1.1: daily driver

- Notifications like the best tools: started, failed, fixed, still failing; per repository and
  workflow; pause for 1 hour or until tomorrow; respect Windows Do Not Disturb.
- Modern Windows notifications with buttons: "Open run", "Open logs", "Mute this workflow".
- Estimated time left for running workflows (median of recent successful runs).
- "Who broke it": the actor and commit of the first failing run after a success.
- Run details: expand a run to see its jobs and the failed step; commit message or PR title.
- Filters: pin repositories, hide passing, filter by branch, "my runs only".
- Global hotkey to open the panel (configurable).
- Stable and beta update channels; "What's new" after updating.
- winget, Scoop and Chocolatey packages; GitHub Marketplace listing.
- Comparison page on the website (RunnerTray vs. other build monitors) and a short demo video.

### 1.2: teams and scale

- Several accounts and organizations at once; GitHub Enterprise Server.
- Pull requests: checks on my PRs, reviews requested from me.
- Runner insights: queue length per label, longest-waiting job, offline runners alert.
- Build screen: a large window for a team monitor.
- Admin guide: silent install, preset configuration file, Group Policy notes.
- ARM64 build.
- Translations (start with the most requested languages).

### Later, if users ask

- Read-only REST endpoint or cctray feed so other tools can read RunnerTray's view.
- Opt-in crash reports.
- Microsoft Store (MSIX) listing.

## 4. Quality bar for every release

- CI: build with analyzers (warnings are errors), unit and scenario tests, UI and accessibility
  tests, self-test, CodeQL; install → update → uninstall test.
- No open CodeQL errors or warnings.
- Manual checklist: Narrator, high contrast, 200 % text, 150 % scaling, two monitors, taskbar on
  the left, sleep and resume, network off and on.
- Changelog entry; screenshots regenerated if the UI changed.

## 5. Owner tasks

| Task | When |
|---|---|
| Sign in from the tray menu once (live test) | Before 1.0 |
| Approve the winget pull request | 1.0 |
| Verify the site in Google Search Console and Bing; submit the sitemap | 1.0 |
| Apply to SignPath Foundation for free code signing | After 1.0 |
| Submit the GitHub Marketplace listing | 1.1 |
| Trademark search before registering the name or logo (optional) | Any time |
