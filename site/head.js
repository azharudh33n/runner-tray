// Runs in <head> before first paint. Kept in a file (not inline) so the Content-Security-Policy
// can stay at script-src 'self'.
(function () {
  var root = document.documentElement;
  root.classList.add('m-enter');
  setTimeout(function () { root.classList.add('m-enter-done'); }, 700);
  root.classList.add('mo-js');
  setTimeout(function () { root.classList.add('mo-safe'); }, 2500);
})();
