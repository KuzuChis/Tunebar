// Media Tray bridge service worker: collects tab state, relays it to the tray app over
// a local WebSocket, and forwards tray commands to tabs.
importScripts("lib/core.js");
const { nextDelay, buildSnapshot } = globalThis.MediaTrayCore;

const URL_WS = "ws://localhost:47821";
const SEND_THROTTLE_MS = 250;
const HEARTBEAT_MS = 20000;

// Rebuilt from the content scripts' periodic state messages if the worker restarts.
const tabsById = {};
let socket = null;
let attempt = 0;
let reconnectTimer = null;
let throttleTimer = null;
let lastSend = 0;

function sendSnapshot() {
  lastSend = Date.now();
  if (socket && socket.readyState === WebSocket.OPEN) {
    socket.send(JSON.stringify(buildSnapshot(tabsById)));
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
    if (wasOpen) console.info("Media Tray: tray connection lost, will retry");
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
    tabsById[sender.tab.id] = msg.state;
    scheduleSnapshot();
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  if (tabId in tabsById) {
    delete tabsById[tabId];
    scheduleSnapshot();
  }
});

chrome.action.onClicked.addListener((tab) => {
  if (tab && typeof tab.id === "number") {
    sendToTab(tab.id, { type: "command", action: "volume", value: 1 });
  }
});

setInterval(sendSnapshot, HEARTBEAT_MS);
connect();
