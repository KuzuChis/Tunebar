// YouTube Music adapter. Selectors from live-page recon 2026-11-18; update here when YT Music changes.
// Loop state comes from the player bar's repeat-mode attribute (NONE | ALL | ONE), not labels.
(() => {
  const BAR = "ytmusic-player-bar";
  const QUEUE = "ytmusic-player-queue#queue #contents";
  // Direct children only: automix suggestions live under #automix-contents and are excluded.
  const ITEM_SELS = [
    QUEUE + " > ytmusic-player-queue-item",
    QUEUE + " > ytmusic-playlist-panel-video-wrapper-renderer ytmusic-player-queue-item",
  ];

  const q = (sel) => document.querySelector(sel);
  const text = (el, sel) => {
    const n = el.querySelector(sel);
    return n ? (n.textContent || "").trim() : "";
  };
  const items = () => {
    for (const s of ITEM_SELS) {
      const found = Array.from(document.querySelectorAll(s));
      if (found.length) return found;
    }
    return [];
  };

  function visible(el) {
    if (el.offsetParent != null) return true;
    try { return getComputedStyle(el).display !== "none"; } catch { return false; }
  }
  function usable(el) {
    return !!el && visible(el) && el.getAttribute("aria-disabled") !== "true" && !el.hasAttribute("disabled");
  }
  function click(sel) {
    const el = q(sel);
    if (!usable(el)) return false;
    el.click();
    return true;
  }
  // Resolves true once check() passes, false after timeoutMs.
  async function waitFor(check, timeoutMs) {
    for (let t = 0; t < timeoutMs; t += 50) {
      if (check()) return true;
      await new Promise((r) => setTimeout(r, 50));
    }
    return check();
  }

  const repeatMode = () => {
    const bar = q(BAR);
    return bar ? bar.getAttribute("repeat-mode") : null;
  };

  globalThis.MediaTraySite = {
    site: "ytmusic",
    next: () => click(BAR + " .next-button"),
    prev: () => click(BAR + " .previous-button"),
    canNext: () => usable(q(BAR + " .next-button")),
    canPrev: () => usable(q(BAR + " .previous-button")),
    getPlaylist() {
      const list = items();
      if (!list.length) return null;
      const index = list.findIndex((el) => el.hasAttribute("selected"));
      if (index < 0) return null;
      return {
        index,
        items: list.map((el) => ({
          title: text(el, ".song-title"),
          artist: text(el, ".byline") || null,
        })),
      };
    },
    jump(i) {
      if (!Number.isInteger(i) || i < 0) return false;
      const el = items()[i];
      const btn = el && el.querySelector("ytmusic-play-button-renderer");
      if (!btn) return false;
      btn.click();
      return true;
    },
    getLoopPlaylist() {
      const m = repeatMode();
      return m === null ? null : m === "ALL";
    },
    // Wait for each change before clicking again so a slow update cannot make us overshoot.
    async setLoopPlaylist(on) {
      if (repeatMode() === null) return false;
      const want = on ? "ALL" : "NONE";
      for (let n = 0; n < 3 && repeatMode() !== want; n++) {
        const b = q(BAR + " .repeat");
        if (!b) return false;
        const before = repeatMode();
        b.click();
        await waitFor(() => repeatMode() !== before, 1500);
      }
      return repeatMode() === want;
    },
  };
})();
