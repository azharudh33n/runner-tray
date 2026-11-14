# Product

Design context for RunnerTray's website and app. Source: [docs/brand/README.md](docs/brand/README.md).

## Register

brand

## Users

Developers, DevOps and platform engineers on Windows 10 and 11. They live in dark IDEs, terminals and
GitHub, run GitHub Actions (often with self-hosted or Docker runners), and notice when a tool feels
generic or noisy. On the website they decide in under a minute whether RunnerTray is worth
installing: what it shows, what it can access, whether it is safe, and how to get it.

## Product Purpose

RunnerTray is a free, open-source Windows tray app that shows GitHub Actions workflow runs,
self-hosted runners and local Docker runners, and notifies the user when a run fails. It exists
because CI status otherwise lives in a browser tab nobody watches. Success on the website: a visitor
understands the problem and the answer from the first screen, trusts the read-only access, and
downloads the installer; contributors find how to suggest features and send code.

## Brand Personality

Calm, precise, technical, trustworthy, with one warm signal (the orange runner light). Voice is
short, plain and calm: say what happened and what to do next, use the user's words (run, workflow,
branch, runner), no exclamation marks, no blame, honest about access and limits.

## Anti-references

- Alarmist status pages and red-everywhere dashboards.
- Decorative SaaS landing pages: gradient blobs, hero metrics, identical icon-card grids, stock
  illustration, "supercharge your workflow" copy.
- Jargon for its own sake ("pipeline execution entity").
- Anything that overpromises or hides what the app can access.

## Design Principles

1. Show the real thing: the panel, the tray icon and the notification, not abstract illustrations.
2. Problem first, then the answer, in plain words.
3. Honest about access: read-only, what is stored where, no telemetry.
4. Quiet by default; colour (orange, status colours) only where it carries meaning.
5. Practice what we preach: the site is as accessible as the app claims to be.

## Accessibility & Inclusion

WCAG 2.2 AA for the site and the app. Status always uses shape and words, never colour alone.
Full keyboard access, visible focus, 4.5:1 text contrast, respects reduced motion, works without
JavaScript for the core path (read, download).
