// Content script: reports the page's media state to the service worker and runs commands.
//
// Gain safety rule (volume > 100% / < 100%): createMediaElementSource() permanently moves a
// <video>'s sound into Web Audio, so if the AudioContext is not running the tab goes silent.
// Therefore the graph (source -> GainNode -> destination) is built only when
//   1. a volume other than 1.0 is wanted (1.0 with no graph needs nothing),
//   2. the context is running after `await ctx.resume()` (canAttachGain; the resume is raced
//      against a short timeout so a context blocked by autoplay policy cannot hang us),
// and at most once per <video> element (GainTracker). Otherwise the audio is left untouched
// and the attempt is retried on the next tick (so a reloaded tab regains its stored volume as
// soon as the browser lets the context run).
//
// Reported volume: the gain value when the graph is attached; null only while an attach for a
// volume other than 1.0 is failing (context not running); otherwise 1.
(function () {
  const { clampVolume, finiteOrNull, canAttachGain, createGainTracker } = globalThis.MediaTrayCore;
  const site = globalThis.MediaTraySite || {};
  const VOLUME_KEY = "mediaTray.volume";
  const TITLE_SUFFIXES = [" - YouTube Music", " - YouTube"];

  const tracker = createGainTracker();
  let ctx = null;
  let gain = null;
  let attaching = false;
  let attachFailed = false; // last attach attempt for a non-1.0 volume could not run the context

  // ---- adapter access: any throw means "unsupported" ----
  function adapter(name, fallback, ...args) {
    try {
      const fn = site[name];
      if (typeof fn !== "function") return fallback;
      const r = fn.apply(site, args);
      return r === undefined ? fallback : r;
    } catch (e) {
      return fallback;
    }
  }

  // Prefer the main player over e.g. hover-preview videos.
  const getVideo = () =>
    document.querySelector("#movie_player video") ||
    document.querySelector("ytmusic-player video") ||
    document.querySelector("video");
  const RESUME_TIMEOUT_MS = 300;

  // ---- volume ----
  function storedVolume() {
    try {
      const raw = sessionStorage.getItem(VOLUME_KEY);
      return raw === null ? 1 : clampVolume(parseFloat(raw));
    } catch (e) {
      return 1;
    }
  }

  function storeVolume(v) {
    try { sessionStorage.setItem(VOLUME_KEY, String(v)); } catch (e) { /* ignore */ }
  }

  async function attachGraph(video) {
    if (!ctx) ctx = new AudioContext();
    let timeoutId;
    const timeout = new Promise((resolve) => { timeoutId = setTimeout(resolve, RESUME_TIMEOUT_MS); });
    try {
      await Promise.race([ctx.resume(), timeout]);
    } finally {
      clearTimeout(timeoutId);
    }
    if (!canAttachGain({ contextState: ctx.state })) return false;
    if (tracker.isAttached(video)) return true;
    const source = ctx.createMediaElementSource(video);
    if (!gain) {
      gain = ctx.createGain();
      gain.connect(ctx.destination);
    }
    source.connect(gain);
    tracker.markAttached(video);
    return true;
  }

  // Applies the stored volume if possible; returns silently when it is not (retried per tick).
  async function applyVolume() {
    const video = getVideo();
    if (!video || attaching) return;
    const wanted = storedVolume();
    const attached = tracker.isAttached(video);
    if (wanted === 1 && !gain) { attachFailed = false; return; } // 100% needs no graph
    if (!attached) {
      if (wanted === 1) return;
      attaching = true;
      try {
        attachFailed = !(await attachGraph(video));
        if (attachFailed) return;
      } catch (e) {
        attachFailed = true;
        return; // leave audio untouched
      } finally {
        attaching = false;
      }
    }
    if (gain) gain.gain.value = wanted;
  }

  function reportedVolume(video) {
    if (gain && tracker.isAttached(video)) return gain.gain.value;
    return attachFailed ? null : 1;
  }

  // ---- state ----
  function stripSuffix(title) {
    for (const s of TITLE_SUFFIXES) {
      if (title.endsWith(s)) return title.slice(0, -s.length);
    }
    return title;
  }

  function largestArtwork(artwork) {
    let best = null;
    let bestArea = -1;
    for (const a of artwork || []) {
      const m = /(\d+)x(\d+)/.exec(a.sizes || "");
      const area = m ? Number(m[1]) * Number(m[2]) : 0;
      if (a.src && area > bestArea) { best = a.src; bestArea = area; }
    }
    return best;
  }

  function readMetadata() {
    const md = navigator.mediaSession && navigator.mediaSession.metadata;
    return {
      title: (md && md.title) || stripSuffix(document.title),
      artist: (md && md.artist) || "",
      coverUrl: md ? largestArtwork(md.artwork) : null,
    };
  }

  function loopState(video) {
    if (video.loop) return "one";
    return adapter("getLoopPlaylist", null) === true ? "playlist" : "off";
  }

  function loopModes() {
    const modes = ["off"];
    if (adapter("getLoopPlaylist", null) !== null) modes.push("playlist");
    modes.push("one");
    return modes;
  }

  function buildState(video) {
    return {
      site: site.site || null,
      ...readMetadata(),
      isPlaying: !video.paused && !video.ended,
      position: finiteOrNull(video.currentTime),
      duration: finiteOrNull(video.duration),
      volume: reportedVolume(video),
      loop: loopState(video),
      loopModes: loopModes(),
      canNext: adapter("canNext", false) === true,
      canPrev: adapter("canPrev", false) === true,
      playlist: adapter("getPlaylist", null),
    };
  }

  function report() {
    const video = getVideo();
    if (!video) return;
    try {
      chrome.runtime.sendMessage({ type: "state", state: buildState(video) }).catch(() => {});
    } catch (e) { /* extension context invalidated */ }
  }

  // ---- commands ----
  function setLoop(video, mode) {
    if (mode === "one") {
      video.loop = true;
      adapter("setLoopPlaylist", false, false);
    } else if (mode === "playlist") {
      video.loop = false;
      adapter("setLoopPlaylist", false, true);
    } else {
      video.loop = false;
      adapter("setLoopPlaylist", false, false);
    }
  }

  async function runCommand(action, value) {
    const video = getVideo();
    if (!video) return;
    switch (action) {
      case "toggle":
        if (video.paused) await video.play().catch(() => {}); else video.pause();
        break;
      case "seek":
        if (Number.isFinite(value)) video.currentTime = value;
        break;
      case "next": adapter("next", false); break;
      case "prev": adapter("prev", false); break;
      case "jump": adapter("jump", false, value); break;
      case "loop": setLoop(video, value); break;
      case "volume":
        storeVolume(clampVolume(value));
        await applyVolume();
        break;
    }
    report();
  }

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg && msg.type === "command") runCommand(msg.action, msg.value);
  });

  // ---- scheduling: 1 s while playing, 5 s while paused, immediately on media events ----
  let timer = null;
  function tick() {
    clearTimeout(timer);
    applyVolume()
      .catch(() => {})
      .finally(() => {
        try { report(); } catch (e) { /* ignore */ }
        const video = getVideo();
        timer = setTimeout(tick, video && !video.paused ? 1000 : 5000);
      });
  }

  for (const ev of ["play", "pause", "loadedmetadata", "volumechange"]) {
    document.addEventListener(ev, (e) => { if (e.target instanceof HTMLVideoElement) tick(); }, true);
  }
  tick();
})();
