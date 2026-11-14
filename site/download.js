// Shows the version and size of the newest release under the download button.
// The links work without this script: /download always redirects to the newest installer.
const label = document.querySelector('[data-latest]');

if (label) {
  fetch('/api/latest', { headers: { Accept: 'application/json' } })
    .then(res => (res.ok ? res.json() : null))
    .then(latest => {
      if (!latest || !latest.version) return;
      const parts = [`Version ${latest.version}`];
      if (latest.prerelease) parts[0] += ' (beta)';
      if (latest.setupSize) parts.push(`${Math.round(latest.setupSize / 1048576)} MB`);
      if (latest.published) {
        const date = new Date(latest.published);
        if (!Number.isNaN(date.getTime())) {
          parts.push(date.toLocaleDateString('en', { day: 'numeric', month: 'short', year: 'numeric' }));
        }
      }
      parts.push('no admin rights needed');
      label.textContent = parts.join(' · ');
    })
    .catch(() => {});
}
