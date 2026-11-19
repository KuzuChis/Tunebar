// YouTube Music adapter. Selectors from live-page recon 2026-11-18; update here when YT Music changes.
// Loop state comes from the player bar's repeat-mode attribute (NONE | ALL | ONE), else the repeat button's label.
(() => {
  const BAR = "ytmusic-player-bar";
  const QUEUE = "ytmusic-player-queue#queue #contents";
  const QUEUE_ITEM = "ytmusic-player-queue-item";
  const REPEAT_MODES = { NONE: "off", ALL: "playlist", ONE: "one" };
  const REPEAT_LABELS = [["repeat off", "off"], ["repeat all", "playlist"], ["repeat one", "one"]];

  const q = (sel) => document.querySelector(sel);
  const text = (el, sel) => {
    const n = el.querySelector(sel);
    return n ? (n.textContent || "").trim() : "";
  };
  // Songs that have a video counterpart are wrapped; the wrapper shows one of two inner items.
  const innerItem = (wrapper) => {
    const inner = Array.from(wrapper.querySelectorAll(QUEUE_ITEM));
    return inner.find(visible) || inner[0] || null;
  };
  // Direct children of #contents in document order (automix lives elsewhere and is excluded).
  const items = () => {
    const box = q(QUEUE);
    if (!box || !box.children) return [];
    const out = [];
    for (const child of Array.from(box.children)) {
      const tag = String(child.tagName || "").toLowerCase();
      if (tag === QUEUE_ITEM) out.push(child);
      else if (tag === "ytmusic-playlist-panel-video-wrapper-renderer") {
        const inner = innerItem(child);
        if (inner) out.push(inner);
      }
    }
    return out;
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

  const repeatButton = () => q(BAR + " .repeat") || q(BAR + ' [aria-label^="Repeat" i]');
  const modeFromLabel = (label) => {
    const l = String(label || "").trim().toLowerCase();
    const hit = REPEAT_LABELS.find(([prefix]) => l.startsWith(prefix));
    return hit ? hit[1] : null;
  };
  const loopMode = () => {
    const bar = q(BAR);
    const attr = bar ? bar.getAttribute("repeat-mode") : null;
    if (attr && REPEAT_MODES[attr]) return REPEAT_MODES[attr];
    const b = repeatButton();
    return b ? modeFromLabel(b.getAttribute("aria-label")) : null;
  };

  const num = (v) => (v === null || v === undefined || v === "" ? NaN : Number(v));
  // "1:23" or "1:02:03" -> seconds.
  function parseClock(s) {
    const parts = String(s).trim().split(":");
    if (parts.length < 2 || parts.length > 3 || !parts.every((p) => /^\d+$/.test(p))) return NaN;
    return parts.reduce((acc, p) => acc * 60 + Number(p), 0);
  }
  function timeFromText(text) {
    const m = /(\d+(?::\d+){1,2})\s*\/\s*(\d+(?::\d+){1,2})/.exec(text || "");
    return m ? { position: parseClock(m[1]), duration: parseClock(m[2]) } : null;
  }
  // Gapless playback: the <video> clock spans many songs, so read the current song from the bar.
  function barTime() {
    const slider = q(BAR + " #progress-bar");
    if (slider) {
      const position = num(slider.getAttribute("aria-valuenow") ?? slider.getAttribute("value"));
      const duration = num(slider.getAttribute("aria-valuemax") ?? slider.getAttribute("max"));
      if (Number.isFinite(position) && Number.isFinite(duration) && duration > 0) return { position, duration };
    }
    const info = q(BAR + " .time-info");
    const t = info ? timeFromText(info.textContent) : null;
    return t && Number.isFinite(t.position) && Number.isFinite(t.duration) && t.duration > 0 ? t : null;
  }

  globalThis.TunebarSite = {
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
    getTime: barTime,
    getLoopMode: loopMode,
    // Wait for each change before clicking again so a slow update cannot make us overshoot.
    async setLoopMode(mode) {
      if (loopMode() === null) return false;
      for (let n = 0; n < 3 && loopMode() !== mode; n++) {
        const b = repeatButton();
        if (!b) return false;
        const before = loopMode();
        b.click();
        await waitFor(() => loopMode() !== before, 1500);
      }
      return loopMode() === mode;
    },
  };
})();
