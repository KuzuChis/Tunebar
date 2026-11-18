import test from "node:test";
import assert from "node:assert/strict";
import vm from "node:vm";
import fs from "node:fs";

const dir = new URL("../content/", import.meta.url);

// Minimal fake element: attrs, text, children keyed by selector, click hook.
function el({ attrs = {}, text = "", kids = {}, onClick } = {}) {
  return {
    offsetParent: {},
    textContent: text,
    clicks: 0,
    hasAttribute: (k) => k in attrs,
    getAttribute: (k) => (k in attrs ? attrs[k] : null),
    querySelector: (s) => kids[s] || null,
    querySelectorAll: () => [],
    click() { this.clicks++; if (onClick) onClick(); },
  };
}

function load(file, map) {
  const document = {
    querySelector: (s) => { const v = map[s]; return Array.isArray(v) ? v[0] || null : v || null; },
    querySelectorAll: (s) => { const v = map[s]; return Array.isArray(v) ? v : v ? [v] : []; },
  };
  const ctx = { document, getComputedStyle: () => ({ display: "block" }) };
  ctx.globalThis = ctx;
  vm.runInNewContext(fs.readFileSync(new URL(file, dir), "utf8"), ctx);
  return ctx.TunebarSite;
}

const YT_ITEMS = "ytd-watch-flexy ytd-playlist-panel-renderer#playlist ytd-playlist-panel-video-renderer";
const ytItem = (title, by, sel) =>
  el({
    attrs: sel ? { selected: "" } : {},
    kids: { "#video-title": el({ text: ` ${title} ` }), "#byline": el({ text: by }), "a#wc-endpoint": el() },
  });

test("youtube: no panel gives null playlist and loop", () => {
  const s = load("youtube.js", {});
  assert.equal(s.getPlaylist(), null);
  assert.equal(s.getLoopPlaylist(), null);
  assert.equal(s.next(), false);
  assert.equal(s.canPrev(), false);
});

test("youtube: 3-item panel, 2nd selected; jump bounds", () => {
  const s = load("youtube.js", { [YT_ITEMS]: [ytItem("A", "x"), ytItem("B", "y", true), ytItem("C", "z")] });
  const p = s.getPlaylist();
  assert.equal(p.index, 1);
  assert.equal(p.items.map((i) => i.title).join(","), "A,B,C");
  assert.equal(s.jump(99), false);
  assert.equal(s.jump(1.5), false);
  assert.equal(s.jump(2), true);
});

test("ytmusic: repeat-mode ALL, loop cycling, queue", async () => {
  const bar = el();
  let mode = "ALL";
  bar.getAttribute = (k) => (k === "repeat-mode" ? mode : null);
  assert.equal(load("ytmusic.js", { "ytmusic-player-bar": bar }).getLoopPlaylist(), true);

  mode = "NONE";
  const next = { NONE: "ALL", ALL: "ONE", ONE: "NONE" };
  const repeat = el({ onClick: () => { mode = next[mode]; } });
  const item = (t, sel) =>
    el({
      attrs: sel ? { selected: "" } : {},
      kids: { ".song-title": el({ text: t }), ".byline": el({ text: "a" }), "ytmusic-play-button-renderer": el() },
    });
  const q = "ytmusic-player-queue#queue #contents > ytmusic-player-queue-item";
  const s = load("ytmusic.js", { "ytmusic-player-bar": bar, "ytmusic-player-bar .repeat": repeat, [q]: [item("A"), item("B", true)] });
  assert.equal(await s.setLoopPlaylist(true), true);
  assert.equal(repeat.clicks, 1);
  assert.equal(s.getPlaylist().index, 1);
  assert.equal(s.jump(99), false);
  assert.equal(load("ytmusic.js", {}).getLoopPlaylist(), null);
  assert.equal(load("ytmusic.js", {}).getPlaylist(), null);
});
