// Stub adapter; Task 8 replaces it with the real ytmusic implementation.
globalThis.MediaTraySite = {
  site: "ytmusic",
  next: () => false,
  prev: () => false,
  canNext: () => false,
  canPrev: () => false,
  getPlaylist: () => null,
  jump: () => false,
  getLoopPlaylist: () => null,
  setLoopPlaylist: () => false,
};
