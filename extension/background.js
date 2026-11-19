// Tunebar bridge service worker: collects tab state, relays it to the tray app over
// a local WebSocket, and forwards tray commands to tabs.
importScripts("lib/core.js");
const { nextDelay, buildSnapshot, pruneStale, tabsToPoll } = globalThis.TunebarCore;

const URL_WS = "ws://localhost:47821";
const SEND_THROTTLE_MS = 250;
const HEARTBEAT_MS = 20000;
const POLL_MS = 3000; // ask tabs that went quiet (background throttling) to report
const STALE_MS = 12000; // content scripts report every <= 5 s; older entries are ghosts

// tabId -> { at: arrival time, state }. Rebuilt from the content scripts' periodic state
// messages if the worker restarts.
const tabsById = {};
let socket = null;
let attempt = 0;
let reconnectTimer = null;
let throttleTimer = null;
let lastSend = 0;

function currentSnapshot() {
  pruneStale(tabsById, Date.now(), STALE_MS);
  const states = {};
  for (const id of Object.keys(tabsById)) states[id] = tabsById[id].state;
  return buildSnapshot(states);
}

function sendSnapshot() {
  lastSend = Date.now();
  if (socket && socket.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify(currentSnapshot()));
  }
}

// Trailing throttle: at most one snapshot per SEND_THROTTLE_MS.
function scheduleSnapshot() {
  if (throttleTimer) return;
  const wait = Math.max(0, SEND_THROTTLE_MS - (Date.now() - lastSend));
  throttleTimer = setTimeout(() => {
    throttleTimer = null;
    sendSnapshot();
  }, wait);
}

function scheduleReconnect() {
  if (reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = null;
    connect();
  }, nextDelay(attempt));
  attempt += 1;
}

function connect() {
  let ws;
  try {
    ws = new WebSocket(URL_WS);
  } catch (e) {
    scheduleReconnect();
    return;
  }
  let wasOpen = false;
  socket = ws;
  ws.onopen = () => {
    wasOpen = true;
    attempt = 0;
    sendSnapshot();
  };
  ws.onmessage = (ev) => {
    let msg;
    try { msg = JSON.parse(ev.data); } catch (e) { return; }
    if (msg && msg.type === "command" && typeof msg.tabId === "number") {
      sendToTab(msg.tabId, { type: "command", action: msg.action, value: msg.value });
    }
  };
  const onEnd = () => {
    if (socket !== ws) return;
    socket = null;
    if (wasOpen) console.info("Tunebar: tray connection lost, will retry");
    wasOpen = false;
    scheduleReconnect();
  };
  ws.onclose = onEnd;
  ws.onerror = onEnd;
}

function sendToTab(tabId, message) {
  try {
    chrome.tabs.sendMessage(tabId, message).catch(() => {});
  } catch (e) { /* tab gone */ }
}

chrome.runtime.onMessage.addListener((msg, sender) => {
  if (msg && msg.type === "state" && sender.tab && typeof sender.tab.id === "number") {
    tabsById[sender.tab.id] = { at: Date.now(), state: msg.state };
    scheduleSnapshot();
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  if (tabId in tabsById) {
    delete tabsById[tabId];
    scheduleSnapshot();
  }
});

// A tab that navigated away from YouTube is no longer a source (url is only visible for
// host-permitted pages, so the staleness pruning above is the main safety net).
chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
  if (!changeInfo.url || !(tabId in tabsById)) return;
  let host = "";
  try { host = new URL(changeInfo.url).hostname; } catch (e) { /* ignore */ }
  if (host !== "www.youtube.com" && host !== "music.youtube.com") {
    delete tabsById[tabId];
    scheduleSnapshot();
  }
});

chrome.action.onClicked.addListener((tab) => {
  if (tab && typeof tab.id === "number") {
    sendToTab(tab.id, { type: "command", action: "volume", value: 1 });
  }
});

// Throttled background tabs stop their own timers but still receive messages, so ask them.
function pollQuietTabs() {
  if (!socket || socket.readyState !== WebSocket.OPEN) return; // nobody to report to
  for (const tabId of tabsToPoll(tabsById, Date.now(), POLL_MS)) {
    let sent;
    try {
      sent = chrome.tabs.sendMessage(tabId, { type: "poll" });
    } catch (e) {
      sent = Promise.reject(e);
    }
    sent.catch(() => dropIfGone(tabId));
  }
}

function forget(tabId) {
  if (tabId in tabsById) {
    delete tabsById[tabId];
    scheduleSnapshot();
  }
}

// A failed poll can just mean the page is briefly unreachable (navigating, reloading), so only
// drop the tab when it is gone or left YouTube; staleness pruning is the backstop for the rest.
function dropIfGone(tabId) {
  let got;
  try {
    got = chrome.tabs.get(tabId);
  } catch (e) {
    got = Promise.reject(e);
  }
  got.then(
    (tab) => {
      let host = "";
      try { host = new URL(tab.url).hostname; } catch (e) { /* url hidden: keep */ return; }
      if (host !== "www.youtube.com" && host !== "music.youtube.com") forget(tabId);
    },
    () => forget(tabId),
  );
}

setInterval(sendSnapshot, HEARTBEAT_MS);
setInterval(pollQuietTabs, POLL_MS);
connect();
