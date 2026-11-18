# Media Tray — Rencana Implementasi

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ikon tray Windows 11 untuk mengontrol media (per tab YouTube di Opera lewat ekstensi, plus app lain lewat SMTC) dengan lock, volume per sumber, loop, dan list playlist.

**Architecture:** Tray app WPF (.NET 10) berisi logika murni di `Core/` (model sumber, registry + lock, protokol JSON, format) yang dites unit, ditambah adapter tipis ke SMTC, Core Audio (NAudio), dan server WebSocket lokal. Ekstensi Opera MV3: content script per tab YouTube/YT Music melapor ke service worker, yang meneruskan snapshot ke tray lewat `ws://localhost:47821` dan menerima perintah balik.

**Tech Stack:** .NET 10 SDK, WPF, `H.NotifyIcon.Wpf`, `NAudio`, `System.Text.Json`, xUnit; JavaScript MV3 (tanpa bundler), `node --test` (Node 24, sudah terpasang).

**Spec:** `media-tray/docs/superpowers/specs/2026-11-18-media-tray-design.md` — executor wajib membacanya.

## Global Constraints

- Root project: `C:\Users\USER\Documents\project\media-tray\`. Semua path di bawah relatif ke root ini.
- TargetFramework app & tes: `net10.0-windows10.0.19041.0`, `<UseWPF>true</UseWPF>` (tes: tanpa UseWPF kecuali perlu), `<Nullable>enable</Nullable>`.
- Port WebSocket: konstanta `47821`; listener prefix `http://localhost:47821/` (http.sys localhost = loopback saja, tidak butuh admin).
- Origin yang diizinkan: tepat `chrome-extension://<ExtensionIdentity.Id>`; selain itu tolak dengan HTTP 403.
- Volume: tab YouTube 0–1.5 (0–150%); app non-browser 0–1.0; browser lewat SMTC → volume `null` (slider disembunyikan).
- Loop: siklus `Off → Playlist → One → Off`, lewati mode yang tidak didukung; daftar dukungan kosong → tombol disembunyikan.
- Volume tab disimpan di `sessionStorage` kunci `mediaTray.volume`; tidak di-reset saat koneksi putus; klik ikon ekstensi → tab aktif kembali ke 1.0.
- Tidak ada koneksi jaringan selain localhost. Tidak ada telemetri.
- Teks UI dalam Bahasa Indonesia, persis seperti di spec ("Terkunci", "Playlist · 3 dari 12", "N sumber lain memutar", "Jalankan saat Windows mulai", "Keluar").
- Kode untuk pemula C#: nama jelas, satu tanggung jawab per file, komentar `///` singkat di setiap class publik.
- **Git hanya dikerjakan leader.** Langkah "Commit" di bawah dilakukan leader setelah review; executor tidak menjalankan git.

## Review Focus

1. **AudioContext tertahan (suspended) membuat tab bisu.** `createMediaElementSource` memindahkan suara ke Web Audio secara permanen; kalau konteksnya tidak `running`, tab jadi senyap. Harapan: graph gain hanya dipasang bila `navigator.userActivation.hasBeenActive` dan setelah `resume()` statusnya `running`; selain itu volume tetap 100% dan dilaporkan tidak tersedia (`volume: null`). Tes: `canAttachGain` di Task 7.
2. **Navigasi SPA YouTube memakai ulang elemen `<video>` yang sama.** Memanggil `createMediaElementSource` dua kali melempar `InvalidStateError`. Harapan: attach sekali per elemen, judul/durasi ter-update saat lagu ganti, gain tetap. Tes: `GainTracker` di Task 7.
3. **Tray belum jalan / restart.** Harapan: ekstensi reconnect dengan backoff tanpa spam error; begitu tray hidup, snapshot masuk ≤ 20 detik; saat koneksi putus, sumber tab dihapus dari registry (bukan dibekukan). Tes: `nextDelay` (Task 7), `ClearExtensionSources` + event `Disconnected` (Task 3, Task 5).
4. **Sumber yang di-lock hilang** (tab ditutup, Spotify keluar). Harapan: lock lepas otomatis, popup pindah ke aturan tanpa lock. Tes: Task 3.
5. **Duplikat & judul kembar.** Sesi SMTC Opera dengan judul sama dengan salah satu tab disembunyikan; dua tab dengan judul identik tetap tampil sebagai dua sumber (id beda). Durasi tak terhingga (live) → `null`, seek disembunyikan, waktu tampil `--:--`. Tes: Task 2 dan Task 3.

---

## Struktur file

```
media-tray/
  MediaTray.sln
  src/MediaTray/
    MediaTray.csproj
    App.xaml, App.xaml.cs            composition root, single instance, tray icon
    Core/MediaSource.cs              record + enum SourceKind, LoopMode, Playlist
    Core/LoopCycle.cs                siklus tombol loop
    Core/Format.cs                   teks waktu / header playlist / jumlah sumber lain
    Core/Protocol.cs                 JSON ekstensi <-> tray
    Core/SourceRegistry.cs           gabung sumber, dedupe Opera, pilih tampilan, lock
    Core/ISourceCommands.cs          antarmuka perintah + CommandRouter
    Bridge/ExtensionIdentity.cs      const Id ekstensi
    Bridge/OriginValidator.cs
    Bridge/ExtensionServer.cs        WebSocket server (HttpListener)
    Windows/AumidMapper.cs           AUMID -> nama app / proses / browser?
    Windows/AppVolumeController.cs   NAudio per-process volume
    Windows/SmtcProvider.cs          sesi SMTC -> MediaSource + perintah
    Windows/AutoStart.cs             HKCU Run + settings.json
    Ui/PopupViewModel.cs
    Ui/PopupWindow.xaml(.cs)
    Ui/Theme.cs                      baca tema terang/gelap Windows
    Assets/tray.ico
  tests/MediaTray.Tests/
    MediaTray.Tests.csproj
    LoopCycleTests.cs FormatTests.cs ProtocolTests.cs SourceRegistryTests.cs
    AumidMapperTests.cs OriginValidatorTests.cs ExtensionServerTests.cs
    AutoStartTests.cs PopupViewModelTests.cs
  extension/
    manifest.json
    background.js
    lib/core.js                      fungsi murni, dites node
    content/media.js                 kontrol <video> standar + gain + laporan state
    content/youtube.js               adapter situs youtube.com
    content/ytmusic.js               adapter situs music.youtube.com
    tools/gen-key.mjs                buat field "key" + Id ekstensi
    tests/core.test.mjs
  README.md                          cara install .NET, build, load extension
```

## Protokol (dipakai Task 4, 5, 7, 8)

Ekstensi → tray (snapshot penuh setiap ada perubahan dan setiap 20 detik):
```json
{"type":"tabs","tabs":[{"tabId":12,"site":"youtube","title":"…","artist":"…","coverUrl":"https://…",
  "isPlaying":true,"position":84.2,"duration":221.0,"volume":0.7,
  "loop":"off","loopModes":["off","playlist","one"],"canNext":true,"canPrev":true,
  "playlist":{"index":2,"items":[{"title":"…","artist":"…"}]}}]}
```
`site`: `"youtube"|"ytmusic"`. `duration` tak terhingga/NaN → `null`. `volume: null` = gain belum tersedia. `playlist: null` = tidak ada playlist. `index` 0-based.

Tray → ekstensi:
```json
{"type":"command","tabId":12,"action":"toggle|next|prev|seek|volume|loop|jump","value":null}
```
`seek`: detik (number). `volume`: 0–1.5. `loop`: `"off"|"playlist"|"one"`. `jump`: index 0-based.

Content script ↔ service worker: `chrome.runtime.sendMessage({type:"state", state:<TabState tanpa tabId>})`; service worker ambil `sender.tab.id`. Balik: `chrome.tabs.sendMessage(tabId, {type:"command", action, value})`.

---

### Task 1: Setup toolchain, solusi, dan repo

**Files:**
- Create: `MediaTray.sln`, `src/MediaTray/MediaTray.csproj`, `src/MediaTray/App.xaml(.cs)` (template), `tests/MediaTray.Tests/MediaTray.Tests.csproj`, `README.md`, `.gitignore`

- [ ] **Step 1 (leader, butuh izin user): install .NET 10 SDK**
Run: `winget install --id Microsoft.DotNet.SDK.10 -e`
Expected: `dotnet --list-sdks` menampilkan `10.0.x`.
- [ ] **Step 2: buat solusi**
`dotnet new sln -n MediaTray`; `dotnet new wpf -n MediaTray -o src/MediaTray`; `dotnet new xunit -n MediaTray.Tests -o tests/MediaTray.Tests`; tambahkan keduanya ke sln; tes mereferensikan app. Set TargetFramework kedua csproj ke `net10.0-windows10.0.19041.0`, Nullable enable. Tambah paket ke app: `H.NotifyIcon.Wpf`, `NAudio`.
- [ ] **Step 3: verifikasi**
Run: `dotnet build MediaTray.sln` lalu `dotnet test MediaTray.sln`
Expected: build sukses 0 error; tes template lulus (hapus tes template setelahnya).
- [ ] **Step 4: README** — langkah install SDK, `dotnet run --project src/MediaTray`, cara "Load unpacked" folder `extension/` di `opera://extensions` (mode developer).
- [ ] **Step 5 (leader): `git init`, `.gitignore` (bin/ obj/ .vs/ node_modules/), commit spec + plan + skeleton**: `chore: scaffold media tray solution`

### Task 2: Model inti, LoopCycle, Format

**Files:** Create `src/MediaTray/Core/MediaSource.cs`, `Core/LoopCycle.cs`, `Core/Format.cs`; Test `tests/MediaTray.Tests/LoopCycleTests.cs`, `FormatTests.cs`

**Interfaces — Produces:**
```csharp
namespace MediaTray.Core;
public enum SourceKind { ExtensionTab, Smtc }
public enum LoopMode { Off, Playlist, One }
public sealed record PlaylistItem(string Title, string? Artist);
public sealed record Playlist(int Index, IReadOnlyList<PlaylistItem> Items);
public sealed record MediaSource(
    string Id,              // "tab:<tabId>" atau "smtc:<aumid>"
    SourceKind Kind,
    string AppName,         // "Opera", "Spotify", …
    string OriginLabel,     // "Opera · tab YouTube", "Opera · YouTube Music", "Spotify"
    string Title, string? Artist,
    string? CoverUrl, byte[]? CoverBytes,
    bool IsPlaying,
    double? PositionSec, double? DurationSec,
    bool CanNext, bool CanPrev,
    double? Volume, double MaxVolume,   // Volume null = slider disembunyikan
    LoopMode Loop, IReadOnlyList<LoopMode> LoopModes,
    Playlist? Playlist)
{ public bool CanSeek => DurationSec is not null; }
public static class LoopCycle { public static LoopMode Next(LoopMode current, IReadOnlyList<LoopMode> supported); }
public static class Format {
    public static string Time(double? seconds);          // 84.2→"1:24", 3725→"1:02:05", null→"--:--"
    public static string PlaylistHeader(Playlist p);     // Index 2, 12 item → "Playlist · 3 dari 12"
    public static string OthersLabel(int count);         // 2 → "2 sumber lain memutar"
    public static string Percent(double volume);         // 0.7 → "70%", 1.5 → "150%"
}
```

- [ ] **Step 1: tes gagal**
```csharp
[Theory]
[InlineData(LoopMode.Off, LoopMode.Playlist)]
[InlineData(LoopMode.Playlist, LoopMode.One)]
[InlineData(LoopMode.One, LoopMode.Off)]
public void Next_cycles_all_modes(LoopMode cur, LoopMode exp) =>
    Assert.Equal(exp, LoopCycle.Next(cur, [LoopMode.Off, LoopMode.Playlist, LoopMode.One]));
[Fact] public void Next_skips_unsupported() =>
    Assert.Equal(LoopMode.One, LoopCycle.Next(LoopMode.Off, [LoopMode.Off, LoopMode.One]));
[Fact] public void Next_with_empty_support_returns_current() =>
    Assert.Equal(LoopMode.Off, LoopCycle.Next(LoopMode.Off, []));
```
Format: `Time(84.2)=="1:24"`, `Time(3725)=="1:02:05"`, `Time(null)=="--:--"`, `Time(0)=="0:00"`, `PlaylistHeader(new(2, 12 item))=="Playlist · 3 dari 12"`, `OthersLabel(2)=="2 sumber lain memutar"`, `Percent(0.7)=="70%"`, `Percent(1.5)=="150%"`.
- [ ] **Step 2:** `dotnet test --filter "LoopCycleTests|FormatTests"` → FAIL (tipe belum ada)
- [ ] **Step 3:** implementasi sesuai Interfaces.
- [ ] **Step 4:** perintah sama → PASS
- [ ] **Step 5 (leader):** commit `feat(core): media source model, loop cycle, formatting`

### Task 3: SourceRegistry (gabung, dedupe, pilih, lock)

**Files:** Create `src/MediaTray/Core/SourceRegistry.cs`; Test `tests/MediaTray.Tests/SourceRegistryTests.cs`

**Interfaces:**
- Consumes: `MediaSource` (Task 2)
- Produces:
```csharp
public sealed class SourceRegistry {
    public SourceRegistry(Func<DateTimeOffset> now);
    public void SetExtensionSources(IReadOnlyList<MediaSource> sources);  // ganti semua Kind=ExtensionTab
    public void SetSmtcSources(IReadOnlyList<MediaSource> sources);       // ganti semua Kind=Smtc
    public void ClearExtensionSources();
    public MediaSource? Current { get; }
    public IReadOnlyList<MediaSource> Others { get; }   // sumber terlihat selain Current; yang memutar dulu, lalu terbaru
    public string? LockedId { get; }
    public void Lock(string id); public void Unlock();
    public void Select(string id);                      // tampilkan sumber ini sampai ada sumber lain yang mulai memutar
    public event EventHandler? Changed;                 // dipicu bila Current/Others/LockedId berubah
}
```
Aturan (dari spec §7 "Model sumber"): catat `StartedAt` per id saat `IsPlaying` berubah false→true (atau pertama terlihat sedang memutar) dan `PausedAt` saat true→false. Tanpa lock: Current = sumber memutar dengan `StartedAt` terbaru; bila tak ada yang memutar → `PausedAt` terbaru. `Select` = set `StartedAt` id itu ke `now()`. Dedupe: sumber Smtc dengan `AppName=="Opera"` disembunyikan bila ada sumber ExtensionTab dengan `Title` sama (trim, OrdinalIgnoreCase). Lock pada id yang hilang dari daftar terlihat → `LockedId=null` otomatis.

- [ ] **Step 1: tes gagal** (pakai jam palsu yang bisa dimajukan; helper `Src(id, kind, playing, title, app)`):
  - `Current_is_most_recently_started_playing`: A mulai main t=1, B mulai main t=2 → Current B.
  - `Falls_back_to_last_paused_when_none_playing`: A main lalu jeda t=3, B jeda t=2 → Current A.
  - `Lock_keeps_current_when_other_starts`: Lock(A); B mulai main → Current A, LockedId "A".
  - `Lock_released_when_source_disappears`: Lock(tab:1); SetExtensionSources([]) → LockedId null, Current = sumber lain yang memutar.
  - `ClearExtensionSources_removes_tabs_and_releases_lock`.
  - `Opera_smtc_hidden_when_title_matches_tab`: tab "Song X" + smtc Opera " song x " → Others/Current tidak mengandung smtc Opera.
  - `Opera_smtc_shown_when_title_differs`.
  - `Two_tabs_same_title_both_visible`: tab:1 dan tab:2 judul sama → keduanya ada.
  - `Select_shows_paused_source_until_another_starts`.
  - `Changed_raised_on_lock`.
- [ ] **Step 2:** `dotnet test --filter SourceRegistryTests` → FAIL
- [ ] **Step 3:** implementasi.
- [ ] **Step 4:** → PASS
- [ ] **Step 5 (leader):** commit `feat(core): source registry with lock and opera dedupe`

### Task 4: Protokol JSON + antarmuka perintah

**Files:** Create `src/MediaTray/Core/Protocol.cs`, `Core/ISourceCommands.cs`; Test `tests/MediaTray.Tests/ProtocolTests.cs`

**Interfaces:**
- Consumes: Task 2 types.
- Produces:
```csharp
public static class Protocol {
    public static IReadOnlyList<MediaSource>? ParseTabs(string json);   // null bila bukan {"type":"tabs"} atau rusak
    public static string BuildCommand(int tabId, string action, object? value);
    public static int TabIdOf(MediaSource s);                           // "tab:12" → 12
}
public interface ISourceCommands {
    Task TogglePlayAsync(MediaSource s); Task NextAsync(MediaSource s); Task PrevAsync(MediaSource s);
    Task SeekAsync(MediaSource s, double seconds); Task SetVolumeAsync(MediaSource s, double volume);
    Task SetLoopAsync(MediaSource s, LoopMode mode); Task JumpAsync(MediaSource s, int index);
}
public sealed class CommandRouter(ISourceCommands extension, ISourceCommands smtc) : ISourceCommands; // pilih berdasarkan s.Kind
```
Pemetaan tab → MediaSource: `Id="tab:"+tabId`, `Kind=ExtensionTab`, `AppName="Opera"`, `OriginLabel` = site `ytmusic` ? `"Opera · YouTube Music"` : `"Opera · tab YouTube"`, `MaxVolume=1.5`, string loop ↔ `LoopMode` (`"off"/"playlist"/"one"`).

- [ ] **Step 1: tes gagal**
  - `ParseTabs_maps_all_fields` (contoh JSON di bagian Protokol → Id "tab:12", OriginLabel "Opera · tab YouTube", Volume 0.7, MaxVolume 1.5, LoopModes 3 item, Playlist.Index 2).
  - `ParseTabs_ytmusic_label` → "Opera · YouTube Music".
  - `ParseTabs_null_duration_disables_seek` → `CanSeek==false`.
  - `ParseTabs_null_volume_and_playlist`.
  - `ParseTabs_returns_null_for_garbage` (`"not json"`, `{"type":"hello"}`).
  - `BuildCommand_seek` → JSON ter-parse dengan `type=="command"`, `tabId==12`, `action=="seek"`, `value==84.5`.
  - `BuildCommand_loop_uses_lowercase_string` (`"playlist"`).
  - `Router_dispatches_by_kind` (dua fake ISourceCommands).
- [ ] **Step 2:** `dotnet test --filter ProtocolTests` → FAIL
- [ ] **Step 3:** implementasi dengan `System.Text.Json` (`JsonDocument` untuk parse, `JsonSerializer` + camelCase untuk build).
- [ ] **Step 4:** → PASS
- [ ] **Step 5 (leader):** commit `feat(core): extension protocol and command router`

### Task 5: ExtensionServer + OriginValidator + kunci ekstensi

**Files:** Create `extension/tools/gen-key.mjs`, `src/MediaTray/Bridge/ExtensionIdentity.cs`, `Bridge/OriginValidator.cs`, `Bridge/ExtensionServer.cs`; Test `OriginValidatorTests.cs`, `ExtensionServerTests.cs`

**Interfaces:**
- Consumes: `Protocol`, `ISourceCommands` (Task 4)
- Produces:
```csharp
public static class ExtensionIdentity { public const string Id = "<32 huruf a-p dari gen-key>"; public const int Port = 47821; }
public sealed class OriginValidator(string extensionId) { public bool IsAllowed(string? origin); }
public sealed class ExtensionServer : ISourceCommands, IDisposable {
    public ExtensionServer(int port, OriginValidator validator);
    public void Start();
    public event Action<IReadOnlyList<MediaSource>>? TabsReceived;
    public event Action? Disconnected;
    public bool IsConnected { get; }
}
```
`gen-key.mjs` (node `crypto`): buat RSA 2048, cetak `key` = base64 DER SPKI public key, dan Id = 32 hex pertama SHA-256(DER) dengan tiap digit hex 0–f dipetakan ke a–p. Leader menjalankannya sekali; `key` masuk `manifest.json` (Task 7), Id masuk `ExtensionIdentity`. Private key tidak disimpan.
Server: `HttpListener` prefix `http://localhost:{port}/`; terima hanya request WebSocket dengan Origin valid (selain itu 403); satu klien aktif — koneksi baru menggantikan yang lama; terima pesan teks → `Protocol.ParseTabs` → `TabsReceived`; saat tutup/putus → `Disconnected`. Perintah `ISourceCommands` dikirim lewat `Protocol.BuildCommand`; bila tidak terhubung, perintah diabaikan diam-diam.

- [ ] **Step 1: tes gagal**
  - OriginValidator: tepat `chrome-extension://<id>` → true; `null`, `"https://www.youtube.com"`, `"chrome-extension://<id lain>"`, `"chrome-extension://<id>.evil"` → false.
  - `Server_rejects_wrong_origin`: `ClientWebSocket` dengan `Options.SetRequestHeader("Origin","https://evil.example")` → `ConnectAsync` melempar `WebSocketException`.
  - `Server_raises_TabsReceived`: klien Origin benar kirim contoh JSON → event dengan 1 sumber "tab:12" (tunggu ≤ 2 detik).
  - `Server_sends_command`: `TogglePlayAsync(tab:12)` → klien menerima JSON `action=="toggle"`.
  - `Server_raises_Disconnected_on_close`.
  (Tes memakai port acak bebas, bukan 47821.)
- [ ] **Step 2:** `dotnet test --filter "OriginValidatorTests|ExtensionServerTests"` → FAIL
- [ ] **Step 3:** implementasi.
- [ ] **Step 4:** → PASS
- [ ] **Step 5 (leader):** commit `feat(bridge): local websocket server with origin check`

### Task 6: Sumber Windows — AumidMapper, AppVolumeController, SmtcProvider

**Files:** Create `src/MediaTray/Windows/AumidMapper.cs`, `AppVolumeController.cs`, `SmtcProvider.cs`; Test `AumidMapperTests.cs`

**Interfaces:**
- Consumes: `MediaSource`, `ISourceCommands`, `LoopMode`
- Produces:
```csharp
public static class AumidMapper {
    public static string AppName(string aumid);
    public static string? ProcessName(string aumid);   // untuk Core Audio; null bila tak diketahui
    public static bool IsBrowser(string aumid);
}
public sealed class AppVolumeController {
    public double? GetVolume(string processName);          // 0..1, null bila tak ada sesi audio
    public bool SetVolume(string processName, double v);  // set SEMUA sesi dengan nama proses itu
}
public sealed class SmtcProvider : ISourceCommands, IDisposable {
    public static Task<SmtcProvider> CreateAsync(AppVolumeController volume);
    public event Action<IReadOnlyList<MediaSource>>? SourcesChanged;  // polling 1000 ms + event SessionsChanged
}
```
SmtcProvider: `Id="smtc:"+aumid`, `OriginLabel=AppName`, cover dari `Thumbnail` → `CoverBytes`, posisi dari `GetTimelineProperties()`, `Loop`/`LoopModes` dari `PlaybackInfo.AutoRepeatMode` (null → `[]`; ada → ketiganya; None↔Off, List↔Playlist, Track↔One). Volume: `IsBrowser` → `null`; selain itu `GetVolume(ProcessName)` dengan `MaxVolume=1.0`. Seek: `TryChangePlaybackPositionAsync(ticks)`; Jump: no-op.

- [ ] **Step 1: tes gagal** AumidMapper:
  - `"Spotify.exe"` → AppName "Spotify", ProcessName "spotify", browser false
  - `"SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"` → "Spotify", "spotify", false
  - `"OperaSoftware.OperaWebBrowser.1778830257"` → "Opera", browser true (nilai nyata dari laptop pengguna)
  - `"Chrome"` → "Chrome", true; `"MSEdge"` → "Edge", true
  - `"vlc.exe"` → "vlc", "vlc", false; `"Foo.Bar!App"` → "App", null, false
- [ ] **Step 2:** `dotnet test --filter AumidMapperTests` → FAIL
- [ ] **Step 3:** implementasi ketiga class.
- [ ] **Step 4:** → PASS; lalu uji manual: program konsol sementara / `Debug.WriteLine` dengan Spotify memutar → muncul 1 sumber "Spotify" dengan volume angka; Opera memutar → sumber "Opera" volume null. Hapus kode sementara.
- [ ] **Step 5 (leader):** commit `feat(windows): smtc provider and per-app volume`

### Task 7: Ekstensi — manifest, service worker, kontrol media standar

**Files:** Create `extension/manifest.json`, `background.js`, `lib/core.js`, `content/media.js`, `tests/core.test.mjs`

**Interfaces:**
- Consumes: Protokol (bagian atas); `key` dari Task 5; adapter situs dari Task 8 lewat `globalThis.MediaTraySite` (boleh belum ada → fitur terkait dianggap tidak didukung).
- Produces:
```js
// lib/core.js — dipasang ke globalThis.MediaTrayCore (dipakai content script & service worker via importScripts)
nextDelay(attempt)            // min(1000 * 2**attempt, 15000)
clampVolume(v)                // NaN/undefined → 1; batasi 0..1.5
finiteOrNull(x)               // angka terhingga atau null
canAttachGain({hasBeenActive, contextState})  // true hanya bila hasBeenActive && contextState==="running"
createGainTracker()           // { isAttached(el), markAttached(el) } berbasis WeakSet
buildSnapshot(tabsById)       // {type:"tabs", tabs:[...]} urut tabId naik
// Kontrak adapter situs (diisi Task 8):
// globalThis.MediaTraySite = { site, next():bool, prev():bool, canNext():bool, canPrev():bool,
//   getPlaylist():{index,items}|null, jump(i):bool, getLoopPlaylist():bool|null, setLoopPlaylist(on):bool }
```
manifest: MV3, `name` "Media Tray Bridge", `key`, `permissions:["tabs"]`, `host_permissions` youtube + music.youtube, `background.service_worker:"background.js"`, `action.default_title:"Media Tray — klik untuk reset volume tab ini ke 100%"`, content_scripts: youtube `["lib/core.js","content/youtube.js","content/media.js"]`, ytmusic `["lib/core.js","content/ytmusic.js","content/media.js"]`, `run_at:"document_idle"`.
background.js: `importScripts("lib/core.js")`; WebSocket ke `ws://localhost:47821`; reconnect pakai `nextDelay`; kirim snapshot pada setiap state masuk dan tiap 20 detik; hapus tab saat `chrome.tabs.onRemoved`; teruskan command ke tab; `chrome.action.onClicked` → command `volume` 1.0 ke tab itu. Error koneksi tidak di-`console.error` berulang (cukup satu `console.info` per putus).
media.js: elemen `document.querySelector("video")`; laporkan state tiap 1 detik saat memutar, tiap 5 detik saat jeda, dan segera pada event `play/pause/loadedmetadata/volumechange`. Judul/artis/cover dari `navigator.mediaSession.metadata`, fallback `document.title` tanpa akhiran `" - YouTube"`. Kontrol: toggle (`play()/pause()`), seek (`currentTime`), loop one (`video.loop`), volume via GainNode sesuai Review Focus 1–2, simpan ke `sessionStorage["mediaTray.volume"]` dan terapkan ulang saat load bila `canAttachGain`. `loopModes` = `["off","one"]` ditambah `"playlist"` bila `getLoopPlaylist()!==null` (urutan off, playlist, one). Semua panggilan adapter dibungkus try/catch → gagal = fitur tidak didukung.

- [ ] **Step 1: tes gagal** `tests/core.test.mjs` (muat `lib/core.js` ke `vm` context, ambil `MediaTrayCore`):
  - `nextDelay`: 0→1000, 1→2000, 3→8000, 10→15000
  - `clampVolume`: NaN→1, -1→0, 2→1.5, 0.7→0.7
  - `finiteOrNull`: Infinity→null, NaN→null, 3→3
  - `canAttachGain`: `{false,"running"}`→false, `{true,"suspended"}`→false, `{true,"running"}`→true
  - `createGainTracker`: objek sama attached setelah mark; objek lain tidak
  - `buildSnapshot`: map {5:…, 2:…} → tabs urut [2,5], `type=="tabs"`
- [ ] **Step 2:** `node --test extension/tests/` → FAIL
- [ ] **Step 3:** implementasi `lib/core.js`, lalu `background.js`, `media.js`, `manifest.json`.
- [ ] **Step 4:** `node --test extension/tests/` → PASS; load unpacked di Opera tanpa error di halaman extensions.
- [ ] **Step 5 (leader):** commit `feat(extension): bridge service worker and standard media control`

### Task 8: Adapter situs YouTube & YouTube Music

**Files:** Create `extension/content/youtube.js`, `extension/content/ytmusic.js`

**Interfaces:**
- Consumes/Produces: kontrak `globalThis.MediaTraySite` (Task 7).

Pemilihan selektor/cara memanggil player diserahkan ke builder (spec §8), dengan batasan: playlist YouTube dibaca dari panel playlist halaman watch (`list=` di URL) — **bukan** dari saran "Up next"/autoplay; YT Music dari antrean pemutar, juga tanpa bagian saran otomatis. Next/prev memakai tombol/pemutar situs. Tiap fungsi mengembalikan `false`/`null` bila elemen tak ditemukan.

- [ ] **Step 1:** implementasi kedua adapter.
- [ ] **Step 2: uji manual di Opera** (dengan Task 7 terpasang; cek log `background.js` lewat "Inspect service worker"):
  - YouTube playlist (`watch?v=…&list=…`): snapshot berisi `playlist.items` = isi playlist, `index` = lagu aktif; `jump(4)` pindah ke lagu ke-5; loop playlist on/off mengubah tombol loop playlist di halaman.
  - YouTube video tunggal: `playlist:null`; `loopModes` tidak memuat `"playlist"`.
  - YT Music: antrean terbaca; next/prev jalan.
- [ ] **Step 3 (leader):** commit `feat(extension): youtube and youtube music adapters`

### Task 9: Popup UI, ViewModel, tray icon, wiring

**Files:** Create `src/MediaTray/Ui/PopupViewModel.cs`, `Ui/PopupWindow.xaml(.cs)`, `Ui/Theme.cs`, `Assets/tray.ico`; Modify `App.xaml`, `App.xaml.cs`; Test `PopupViewModelTests.cs`

**Interfaces:**
- Consumes: `SourceRegistry` (Task 3), `ISourceCommands`/`CommandRouter` (Task 4), `ExtensionServer` (Task 5), `SmtcProvider`/`AppVolumeController` (Task 6), `Format`, `LoopCycle` (Task 2)
- Produces:
```csharp
public sealed class PopupViewModel : INotifyPropertyChanged {
    public PopupViewModel(SourceRegistry registry, ISourceCommands commands, Action<Action> dispatch);
    public MediaSource? Current { get; }  public bool IsLocked { get; }
    public string PositionText { get; }   public string DurationText { get; }
    public bool ShowVolume { get; }       public bool ShowLoop { get; }   public bool ShowPlaylist { get; }
    public string? PlaylistHeader { get; } public string OthersText { get; } public IReadOnlyList<MediaSource> Others { get; }
    public Task TogglePlayAsync(); public Task NextAsync(); public Task PrevAsync();
    public Task CycleLoopAsync();                     // SetLoopAsync(Current, LoopCycle.Next(...))
    public void ToggleLock();                         // Lock(Current.Id) / Unlock()
    public Task SetVolumeAsync(double v); public Task SeekAsync(double sec); public Task JumpAsync(int index);
    public void SelectSource(string id);
    public Task TogglePlayOtherAsync(string id);     // tombol pause di daftar sumber lain
    public void LockSource(string id);               // tombol gembok di daftar sumber lain → Lock(id), jadi Current
}
```
App.xaml.cs (composition root): single instance via `Mutex` bernama `"MediaTray.SingleInstance"`; buat `SourceRegistry(() => DateTimeOffset.Now)`, `ExtensionServer(ExtensionIdentity.Port, new OriginValidator(ExtensionIdentity.Id))` (`TabsReceived`→`SetExtensionSources`, `Disconnected`→`ClearExtensionSources`), `SmtcProvider` (`SourcesChanged`→`SetSmtcSources`), `CommandRouter`; `TaskbarIcon` (H.NotifyIcon) klik kiri → tampil/sembunyi popup. Port terpakai → tampilkan balon "Port 47821 dipakai program lain" dan tetap jalan tanpa ekstensi.
Popup: layout A persis urutan spec §6; lebar 340; `WindowStyle=None`, `ResizeMode=NoResize`, `Topmost`, `ShowInTaskbar=false`; posisi pojok kanan bawah `SystemParameters.WorkArea` dengan margin 12; `Deactivated` → `Hide()`; warna dari `Theme` (registry `AppsUseLightTheme`). Slider volume: maksimum = `Current.MaxVolume`, kirim `SetVolumeAsync` dibatasi paling sering tiap 100 ms; seek dikirim saat geser selesai. Playlist: `ListBox` tinggi ±3 baris, otomatis scroll ke lagu aktif. Tanpa sumber: tampilkan "Tidak ada yang diputar".

- [ ] **Step 1: tes gagal** (fake `ISourceCommands` yang mencatat panggilan, `dispatch = a => a()`):
  - `CycleLoop_sends_next_mode` (Current Loop Off, modes 3 → SetLoop Playlist)
  - `ToggleLock_locks_current_then_unlocks`
  - `ShowVolume_false_when_volume_null`
  - `ShowLoop_false_when_no_loop_modes`
  - `PlaylistHeader_formats` → "Playlist · 3 dari 12"
  - `OthersText_counts_others` → "2 sumber lain memutar"
  - `Raises_PropertyChanged_when_registry_changes`
  - `LockSource_makes_other_current_and_locked`
  - `TogglePlayOther_targets_that_source` (bukan Current)
- [ ] **Step 2:** `dotnet test --filter PopupViewModelTests` → FAIL
- [ ] **Step 3:** implementasi ViewModel, lalu XAML + App wiring.
- [ ] **Step 4:** → PASS; `dotnet run --project src/MediaTray`: ikon tray muncul, popup terbuka/tutup, Spotify (bila ada) bisa pause/next/volume.
- [ ] **Step 5 (leader):** commit `feat(ui): tray icon and popup`

### Task 10: Auto-start dan menu klik kanan

**Files:** Create `src/MediaTray/Windows/AutoStart.cs`; Modify `App.xaml.cs`; Test `AutoStartTests.cs`

**Interfaces:**
```csharp
public sealed class AutoStart {
    public AutoStart(string runKeyPath, string settingsPath, string exePath); // produksi: @"Software\Microsoft\Windows\CurrentVersion\Run", %LOCALAPPDATA%\MediaTray\settings.json
    public bool IsEnabled { get; }
    public void SetEnabled(bool on);       // tulis/hapus nilai "MediaTray" = "\"<exePath>\"" + simpan {"autoStart":on}
    public void ApplyFirstRunDefault();    // bila settings.json belum ada → SetEnabled(true)
}
```
- [ ] **Step 1: tes gagal** (runKeyPath `Software\MediaTrayTests\Run`, settings di folder temp; bersihkan di Dispose):
  - `First_run_enables` → nilai registry ada, settings `autoStart:true`
  - `First_run_respects_existing_settings` (settings `false` sudah ada → tetap mati)
  - `SetEnabled_false_removes_value`
- [ ] **Step 2:** `dotnet test --filter AutoStartTests` → FAIL
- [ ] **Step 3:** implementasi; menu klik kanan tray: "Jalankan saat Windows mulai" (centang = `IsEnabled`) dan "Keluar" (tutup server, buang ikon, `Shutdown()`).
- [ ] **Step 4:** → PASS
- [ ] **Step 5 (leader):** commit `feat: auto-start toggle and tray menu`

### Task 11: Verifikasi ujung-ke-ujung (leader + pengguna)

- [ ] `dotnet test MediaTray.sln` dan `node --test extension/tests/` → semua PASS.
- [ ] `dotnet publish src/MediaTray -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` → satu `MediaTray.exe`.
- [ ] Skenario di Opera (3 tab seperti uji awal: video, playlist YT, YT Music):
  1. Popup menampilkan tab yang terakhir mulai memutar; "2 sumber lain memutar" terbuka dan berisi dua tab lain.
  2. Lock tab playlist → putar video di tab lain → popup tetap di playlist; pause/next mengontrol playlist.
  3. Volume tab playlist 40% → tab video tidak berubah; reload tab playlist → tetap 40%; tutup tray → tetap 40%; klik ikon ekstensi → 100%.
  4. Volume 150% terdengar lebih keras dari 100%.
  5. Loop: tiga mode berganti dengan ikon berbeda; video tunggal hanya off/one.
  6. Klik lagu ke-5 di playlist → pindah.
  7. Tutup tab yang di-lock → lock lepas.
  8. Tray dimatikan lalu dijalankan lagi → tab muncul kembali ≤ 20 detik.
  9. Uji Origin: dari console DevTools halaman biasa, `new WebSocket("ws://localhost:47821")` → gagal.
- [ ] RAM idle `MediaTray.exe` di Task Manager < ~60 MB.
- [ ] Leader: commit akhir + tag `v0.1.0`.
