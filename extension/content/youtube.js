// Stub adapter; Task 8 replaces it with the real youtube implementation.
globalThis.MediaTraySite = {
  site: "youtube",
  next: () => false,
  prev: () => false,
  canNext: () => false,
  canPrev: () => false,
  getPlaylist: () => null,
  jump: () => false,
  getLoopPlaylist: () => null,
  setLoopPlaylist: () => false,
};
