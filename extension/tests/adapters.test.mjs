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
  const ctx = { document, getComputedStyle: (e) => ({ display: e && e.hidden ? "none" : "block" }) };
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

const YM_BAR = "ytmusic-player-bar";
const YM_QUEUE = "ytmusic-player-queue#queue #contents";
const ymItem = (t, sel, visible = true) => {
  const e = el({
    attrs: sel ? { selected: "" } : {},
    kids: { ".song-title": el({ text: t }), ".byline": el({ text: "a" }), "ytmusic-play-button-renderer": el() },
  });
  if (!visible) { e.offsetParent = null; e.hidden = true; }
  return e;
};
const ymWrap = (...inner) => {
  const w = el();
  w.tagName = "YTMUSIC-PLAYLIST-PANEL-VIDEO-WRAPPER-RENDERER";
  w.querySelectorAll = () => inner;
  return w;
};
const ymDirect = (e) => { e.tagName = "YTMUSIC-PLAYER-QUEUE-ITEM"; return e; };
const box = (...children) => ({ children });

test("ytmusic: loop mode from repeat-mode attr, cycling with setLoopMode", async () => {
  const bar = el();
  let mode = "NONE";
  bar.getAttribute = (k) => (k === "repeat-mode" ? mode : null);
  const next = { NONE: "ALL", ALL: "ONE", ONE: "NONE" };
  const repeat = el({ onClick: () => { mode = next[mode]; } });
  const s = load("ytmusic.js", { [YM_BAR]: bar, [YM_BAR + " .repeat"]: repeat });
  assert.equal(s.getLoopMode(), "off");
  assert.equal(await s.setLoopMode("one"), true);
  assert.equal(repeat.clicks, 2);
  assert.equal(s.getLoopMode(), "one");
  assert.equal(await s.setLoopMode("playlist"), true);
  assert.equal(repeat.clicks, 4); // one -> off -> playlist
  assert.equal(load("ytmusic.js", {}).getLoopMode(), null);
  assert.equal(await load("ytmusic.js", {}).setLoopMode("one"), false);
});

test("ytmusic: loop mode falls back to the repeat button's aria-label", async () => {
  let label = "Repeat off";
  const labels = { "Repeat off": "Repeat all", "Repeat all": "Repeat one", "Repeat one": "Repeat off" };
  const btn = el({ onClick: () => { label = labels[label]; } });
  btn.getAttribute = (k) => (k === "aria-label" ? label : null);
  const s = load("ytmusic.js", { [YM_BAR]: el(), [YM_BAR + ' [aria-label^="Repeat" i]']: btn });
  assert.equal(s.getLoopMode(), "off");
  assert.equal(await s.setLoopMode("playlist"), true);
  assert.equal(s.getLoopMode(), "playlist");
});

test("ytmusic: getTime from the progress slider, then from time-info text", () => {
  const slider = el({ attrs: { "aria-valuenow": "83", "aria-valuemax": "262" } });
  assert.deepEqual({ ...load("ytmusic.js", { [YM_BAR + " #progress-bar"]: slider }).getTime() }, { position: 83, duration: 262 });
  const alt = el({ attrs: { value: "5", max: "200" } });
  assert.deepEqual({ ...load("ytmusic.js", { [YM_BAR + " #progress-bar"]: alt }).getTime() }, { position: 5, duration: 200 });
  const info = el({ text: " 1:23 / 4:22 " });
  assert.deepEqual({ ...load("ytmusic.js", { [YM_BAR + " .time-info"]: info }).getTime() }, { position: 83, duration: 262 });
  const long = el({ text: "1:02:03 / 1:10:00" });
  assert.deepEqual({ ...load("ytmusic.js", { [YM_BAR + " .time-info"]: long }).getTime() }, { position: 3723, duration: 4200 });
  assert.equal(load("ytmusic.js", {}).getTime(), null);
  assert.equal(load("ytmusic.js", { [YM_BAR + " .time-info"]: el({ text: "-- / --" }) }).getTime(), null);
  const bad = el({ attrs: { "aria-valuenow": "x", "aria-valuemax": "0" } });
  assert.equal(load("ytmusic.js", { [YM_BAR + " #progress-bar"]: bad }).getTime(), null);
});

test("ytmusic: queue mixes direct and wrapped items in order; selected index is right", () => {
  const c = ymItem("C", true);
  const hiddenPrimary = ymItem("B-primary", false, false);
  const visibleCounterpart = ymItem("B-video");
  const s = load("ytmusic.js", {
    [YM_QUEUE]: box(ymDirect(ymItem("A")), ymWrap(hiddenPrimary, visibleCounterpart), ymDirect(c), ymWrap(ymItem("D"))),
  });
  const p = s.getPlaylist();
  assert.equal(p.items.map((i) => i.title).join(","), "A,B-video,C,D");
  assert.equal(p.index, 2);
  assert.equal(s.jump(1), true);
  assert.equal(s.jump(4), false);
  assert.equal(load("ytmusic.js", { [YM_QUEUE]: box() }).getPlaylist(), null);
  assert.equal(load("ytmusic.js", {}).getPlaylist(), null);
});
