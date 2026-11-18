import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const code = readFileSync(new URL("../lib/core.js", import.meta.url), "utf8");
const sandbox = {};
vm.runInNewContext(code, sandbox);
const core = sandbox.TunebarCore;

test("nextDelay backs off to a cap", () => {
  assert.equal(core.nextDelay(0), 1000);
  assert.equal(core.nextDelay(1), 2000);
  assert.equal(core.nextDelay(3), 8000);
  assert.equal(core.nextDelay(10), 15000);
});

test("clampVolume", () => {
  assert.equal(core.clampVolume(NaN), 1);
  assert.equal(core.clampVolume(undefined), 1);
  assert.equal(core.clampVolume(-1), 0);
  assert.equal(core.clampVolume(2), 1.5);
  assert.equal(core.clampVolume(0.7), 0.7);
});

test("finiteOrNull", () => {
  assert.equal(core.finiteOrNull(Infinity), null);
  assert.equal(core.finiteOrNull(NaN), null);
  assert.equal(core.finiteOrNull(3), 3);
});

test("canAttachGain", () => {
  assert.equal(core.canAttachGain({ contextState: "suspended" }), false);
  assert.equal(core.canAttachGain({ contextState: "closed" }), false);
  assert.equal(core.canAttachGain({ contextState: "running" }), true);
});

test("pruneStale drops entries older than maxAge", () => {
  const entries = { 1: { at: 1000, state: {} }, 2: { at: 9000, state: {} } };
  const removed = core.pruneStale(entries, 14000, 12000);
  assert.deepEqual(JSON.parse(JSON.stringify(removed)), [1]);
  assert.deepEqual(Object.keys(entries), ["2"]);
  assert.deepEqual(JSON.parse(JSON.stringify(core.pruneStale(entries, 14000, 12000))), []);
});

test("createGainTracker", () => {
  const t = core.createGainTracker();
  const a = {};
  const b = {};
  assert.equal(t.isAttached(a), false);
  t.markAttached(a);
  assert.equal(t.isAttached(a), true);
  assert.equal(t.isAttached(b), false);
});

test("buildSnapshot sorts by tabId", () => {
  const snap = core.buildSnapshot({ 5: { title: "five" }, 2: { title: "two" } });
  assert.equal(snap.type, "tabs");
  assert.deepEqual(JSON.parse(JSON.stringify(snap.tabs.map((t) => t.tabId))), [2, 5]);
  assert.equal(snap.tabs[0].title, "two");
});
