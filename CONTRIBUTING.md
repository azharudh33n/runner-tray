# Contributing to RunnerTray

Thank you for helping. Bug reports, ideas, documentation fixes and code are all welcome.

## Before you start

- **Bugs:** open an [issue](https://github.com/azharudh33n/runner-tray/issues/new/choose) with the
  bug template. Attach the report from **right-click the tray icon → Diagnostics → Copy**. It
  contains no tokens.
- **Ideas:** check the [roadmap](docs/ROADMAP.md) first, then open a feature request.
- **Security problems:** do not open an issue. See [SECURITY.md](SECURITY.md).
- **Larger changes:** open an issue first so we can agree on the approach.

## Set up

You need Windows 10 or 11 and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/azharudh33n/runner-tray.git
```

```bash
dotnet build RunnerTray.sln -c Release
```

```bash
dotnet run --project src/RunnerTray -- --demo
```

`--demo` shows made-up data, so you need no GitHub account.

## Rules for code

- Build with no warnings: analyzers run with warnings as errors.
- Add or update tests in `tests/RunnerTray.Tests` for logic, and `tests/RunnerTray.UiTests` for
  windows and accessibility.
- Keep RunnerTray **read-only** on GitHub. Do not add permissions to the GitHub App without an issue
  that explains why.
- Follow GitHub's API rules: one request at a time through `ApiClient`, conditional requests, and
  respect rate limits.
- Accessibility is required: keyboard access, screen reader names, status shown with shape and words,
  contrast of at least 4.5:1. The Axe.Windows test must pass.
- Write user-facing text in plain, short sentences. See the voice section in the
  [brand guidelines](docs/brand/README.md#5-voice).

Run everything before you open a pull request:

```bash
dotnet test RunnerTray.sln -c Release
```

If you change the UI, regenerate the screenshots:

```bash
dotnet run --project src/RunnerTray -c Release -- --screenshot docs
```

## Pull requests

- One topic per pull request, with a clear title and description.
- Update `CHANGELOG.md` under an "Unreleased" heading.
- By contributing, you agree that your contribution is licensed under the [MIT license](LICENSE).
- The RunnerTray name and logo are not covered by the MIT license; see [TRADEMARKS.md](TRADEMARKS.md).

## Conduct

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).
