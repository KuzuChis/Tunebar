// Pure helpers shared by content scripts, the service worker (importScripts) and node tests.
// Classic script: no browser APIs, no modules.
(function () {
  function nextDelay(attempt) {
    return Math.min(1000 * 2 ** attempt, 15000);
  }

  function clampVolume(v) {
    const n = Number(v);
    if (v === null || v === undefined || Number.isNaN(n)) return 1;
    return Math.min(1.5, Math.max(0, n));
  }

  function finiteOrNull(x) {
    return typeof x === "number" && Number.isFinite(x) ? x : null;
  }

  function canAttachGain({ hasBeenActive, contextState }) {
    return hasBeenActive === true && contextState === "running";
  }

  function createGainTracker() {
    const seen = new WeakSet();
    return {
      isAttached: (el) => seen.has(el),
      markAttached: (el) => { seen.add(el); },
    };
  }

  function buildSnapshot(tabsById) {
    const tabs = Object.keys(tabsById)
      .map(Number)
      .sort((a, b) => a - b)
      .map((tabId) => ({ tabId, ...tabsById[tabId] }));
    return { type: "tabs", tabs };
  }

  globalThis.MediaTrayCore = {
    nextDelay, clampVolume, finiteOrNull, canAttachGain, createGainTracker, buildSnapshot,
  };
})();
