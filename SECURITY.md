# Security policy

## Supported versions

Only the latest release gets security fixes. The installed version updates itself automatically.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through
[GitHub's private vulnerability reporting](https://github.com/azharudh33n/runner-tray/security/advisories/new).

Include what you found, how to reproduce it, and the impact. You will get an answer within 7 days.
Once a fix is released, the advisory is published and you are credited, unless you prefer not to be.

## How RunnerTray protects your account

- **Read-only access.** The RunnerTray GitHub App has three permissions, all read-only: Actions,
  Metadata, and organization Self-hosted runners. It cannot change code, settings or runs.
- **No secret in the app.** Sign-in uses GitHub's device flow, which needs only the public client ID.
- **Tokens stay on your PC.** They are stored in Windows Credential Manager, encrypted for your Windows
  user, and refreshed automatically. Logs never contain tokens.
- **Only GitHub.** RunnerTray talks to `github.com` and `api.github.com` (and to Docker on your PC if
  you turn that on). It sends no telemetry.
- **Verifiable builds.** Releases are built from this repository by GitHub Actions. Each release has
  SHA-256 checksums (`SHA256SUMS.txt`) and a
  [build provenance attestation](https://docs.github.com/en/actions/security-for-github-actions/using-artifact-attestations).
  To verify a download:

  ```bash
  gh attestation verify RunnerTray-win-Setup.exe --repo azharudh33n/runner-tray
  ```

- **Checked code.** CodeQL and Dependabot run on every change.

To remove RunnerTray's access, revoke it in
[GitHub > Settings > Applications](https://github.com/settings/apps/authorizations).
