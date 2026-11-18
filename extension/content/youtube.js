// YouTube adapter. Selectors from live-page recon 2026-11-18; update here when YouTube changes.
// State is read from structure (icon path markers), never from localized labels.
(() => {
  const PANEL = "ytd-watch-flexy ytd-playlist-panel-renderer#playlist";
  const ITEMS = PANEL + " ytd-playlist-panel-video-renderer";
  const LOOP_BTN = PANEL + " ytd-playlist-loop-button-renderer button";
  const ICON_PLAYLIST = "M12 10a2 2";
  const ICON_ONE = "M13 15V8";

  const q = (sel) => document.querySelector(sel);
  const text = (el, sel) => {
    const n = el.querySelector(sel);
    return n ? (n.textContent || "").trim() : "";
  };
  const items = () => Array.from(document.querySelectorAll(ITEMS));

  function visible(el) {
    if (el.offsetParent != null) return true;
    try { return getComputedStyle(el).display !== "none"; } catch { return false; }
  }
  function usable(el) {
    return !!el && visible(el) && el.getAttribute("aria-disabled") !== "true";
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

  // "playlist" | "one" | "off" | null (no button)
  function loopState() {
    const btn = q(LOOP_BTN);
    if (!btn) return null;
    const ds = Array.from(btn.querySelectorAll("path")).map((p) => p.getAttribute("d") || "");
    if (ds.some((d) => d.includes(ICON_PLAYLIST))) return "playlist";
    if (ds.some((d) => d.includes(ICON_ONE))) return "one";
    return "off";
  }

  globalThis.TunebarSite = {
    site: "youtube",
    next: () => click(".ytp-next-button"),
    prev: () => click(".ytp-prev-button"),
    canNext: () => usable(q(".ytp-next-button")),
    canPrev: () => usable(q(".ytp-prev-button")),
    getPlaylist() {
      const list = items();
      if (!list.length) return null;
      const index = list.findIndex((el) => el.hasAttribute("selected"));
      if (index < 0) return null;
      return {
        index,
        items: list.map((el) => ({
          title: text(el, "#video-title"),
          artist: text(el, "#byline") || null,
        })),
      };
    },
    jump(i) {
      if (!Number.isInteger(i) || i < 0) return false;
      const el = items()[i];
      const a = el && el.querySelector("a#wc-endpoint");
      if (!a) return false;
      a.click();
      return true;
    },
    getLoopPlaylist() {
      const s = loopState();
      return s === null ? null : s === "playlist";
    },
    // The icon updates asynchronously after a click (verified live), so wait for each change
    // before clicking again; clicking 3 times in a row would wrap back to the start.
    async setLoopPlaylist(on) {
      if (loopState() === null) return false;
      const want = on ? "playlist" : "off";
      for (let n = 0; n < 3 && loopState() !== want; n++) {
        const before = loopState();
        q(LOOP_BTN).click();
        await waitFor(() => loopState() !== before, 1500);
      }
      return loopState() === want;
    },
  };
})();
