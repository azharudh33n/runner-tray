// Cloudflare Worker for https://runnertray.wx8.org/
//
//   /download           302 to the newest RunnerTray-win-Setup.exe on GitHub Releases
//   /download/portable  302 to the newest RunnerTray-win-Portable.zip
//   /api/latest         JSON with the newest version, its date, file size and links
//   everything else     the static site in _site (env.ASSETS)
//
// "Newest" is the most recent release that is not a draft and has the file, so a beta counts
// too. GitHub answers are cached for 10 minutes. If GitHub cannot be reached, the download
// routes still redirect (to GitHub's own "latest" link, then to the Releases page), so a
// download button never ends on an error page.

const REPO = 'azharudh33n/runner-tray';
const RELEASES_PAGE = `https://github.com/${REPO}/releases`;
const ASSETS = { setup: 'RunnerTray-win-Setup.exe', portable: 'RunnerTray-win-Portable.zip' };
const CACHE_SECONDS = 600;

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    // `wrangler dev` rewrites the URL to http://runnertray.wx8.org, so local runs pass --var DEV:1.
    if (url.protocol === 'http:' && !env.DEV) {
      url.protocol = 'https:';
      return Response.redirect(url.toString(), 301);
    }

    const path = url.pathname.replace(/\/+$/, '') || '/';
    if (path === '/download') return download('setup', env, ctx);
    if (path === '/download/portable') return download('portable', env, ctx);
    if (path === '/api/latest') return latestJson(env, ctx);

    return env.ASSETS.fetch(request);
  },
};

async function download(kind, env, ctx) {
  const latest = await getLatest(env, ctx);
  const file = latest?.[kind];
  if (file) return redirect(file.url);
  // GitHub API unreachable or no release has the file yet.
  const fallback = await headOk(`${RELEASES_PAGE}/latest/download/${ASSETS[kind]}`);
  return redirect(fallback ?? RELEASES_PAGE);
}

async function latestJson(env, ctx) {
  const latest = await getLatest(env, ctx);
  const body = latest
    ? {
        version: latest.version,
        published: latest.published,
        prerelease: latest.prerelease,
        notesUrl: latest.notesUrl,
        setupUrl: latest.setup?.url ?? null,
        setupSize: latest.setup?.size ?? null,
        portableUrl: latest.portable?.url ?? null,
        portableSize: latest.portable?.size ?? null,
      }
    : { version: null, releasesUrl: RELEASES_PAGE };
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': `public, max-age=${latest ? 300 : 30}`,
      'Access-Control-Allow-Origin': '*',
    },
  });
}

function redirect(location) {
  return new Response(null, {
    status: 302,
    headers: { Location: location, 'Cache-Control': 'public, max-age=300' },
  });
}

async function headOk(url) {
  try {
    const res = await fetch(url, { method: 'HEAD', redirect: 'manual' });
    return res.status >= 200 && res.status < 400 ? url : null;
  } catch {
    return null;
  }
}

// Newest release that has the setup file, from the GitHub API, cached in the Cache API.
async function getLatest(env, ctx) {
  const cache = caches.default;
  const key = new Request(`https://cache.runnertray.internal/latest-release`);
  const hit = await cache.match(key);
  if (hit) return hit.json();

  let latest = null;
  try {
    const headers = { 'User-Agent': 'runnertray-site', Accept: 'application/vnd.github+json' };
    if (env.GITHUB_TOKEN) headers.Authorization = `Bearer ${env.GITHUB_TOKEN}`;
    const res = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=15`, { headers });
    if (res.ok) latest = pick(await res.json());
  } catch {
    latest = null;
  }
  if (!latest) return null;

  const copy = new Response(JSON.stringify(latest), {
    headers: { 'Content-Type': 'application/json', 'Cache-Control': `public, max-age=${CACHE_SECONDS}` },
  });
  ctx.waitUntil(cache.put(key, copy));
  return latest;
}

function pick(releases) {
  if (!Array.isArray(releases)) return null;
  const candidates = releases
    .filter(r => !r.draft && r.assets?.some(a => a.name === ASSETS.setup))
    .sort((a, b) => Date.parse(b.published_at) - Date.parse(a.published_at));
  const r = candidates[0];
  if (!r) return null;
  const asset = name => {
    const a = r.assets.find(x => x.name === name);
    return a ? { url: a.browser_download_url, size: a.size } : null;
  };
  return {
    version: r.tag_name.replace(/^v/, ''),
    published: r.published_at,
    prerelease: r.prerelease,
    notesUrl: r.html_url,
    setup: asset(ASSETS.setup),
    portable: asset(ASSETS.portable),
  };
}
