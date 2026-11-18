# Media Tray — Desain

Tanggal: 2026-11-18
Status: disetujui pengguna (2026-11-18)

## 1. Maksud dan alasan

Pengguna sering memutar musik di tab YouTube / YouTube Music di Opera sambil
membuka tab lain (video, dll). Untuk pause/next lagu sekarang ia harus
berpindah ke jendela Opera dan mencari tab musiknya. Itu ribet.

Media Tray adalah ikon kecil di system tray Windows 11. Satu klik membuka
popup untuk mengontrol musik **tanpa membuka jendela app yang memutarnya**.
Ia otomatis mengikuti apa yang sedang memutar (Spotify, browser, app lain),
dan bisa **di-lock** ke satu sumber sehingga tombolnya tidak pindah ke video
lain yang baru diputar.

Pengguna juga memakai project ini untuk belajar C#, jadi kode harus mudah
dibaca dan terstruktur rapi.

### Fakta yang sudah dibuktikan
Skrip uji di laptop pengguna (Opera, 3 tab bersuara: 1 video YouTube,
1 playlist YouTube, 1 YouTube Music) menunjukkan Windows (SMTC) **hanya
melihat 1 sesi** untuk Opera. Artinya kontrol per tab di Opera tidak mungkin
lewat API Windows saja → butuh ekstensi browser.

## 2. Sasaran

1. Ikon tray; klik kiri membuka popup (layout "A", lihat §6).
2. Auto-detect sumber yang sedang memutar: setiap tab YouTube/YT Music di
   Opera (lewat ekstensi) sebagai sumber terpisah, ditambah sesi media
   Windows dari app lain (Spotify, Chrome, Edge, VLC, dll).
3. **Lock**: kunci popup ke satu sumber. Selama terkunci, sumber yang tampil
   dan dikontrol tidak berubah walau sumber lain mulai memutar.
4. Kontrol: play/pause, next, previous, progress bar yang bisa digeser (seek).
5. **Volume per sumber**:
   - Tab YouTube: 0–150%, lapisan volume sendiri lewat Web Audio (GainNode),
     terpisah dari slider volume YouTube, hanya memengaruhi tab itu.
   - App non-browser (Spotify, VLC, dll): 0–100%, volume per-app Windows
     (seperti Volume Mixer). *Keputusan leader:* Windows membatasi volume
     per-app di 100%; di atas itu butuh driver audio, di luar cakupan.
   - Browser yang hanya terlihat lewat Windows (sumber cadangan "Opera",
     Chrome, Edge): slider volume **disembunyikan**. *Keputusan leader:*
     volume Windows berlaku untuk seluruh browser, jadi akan ikut mengubah
     tab lain — melanggar janji "hanya tab itu".
   - Saat koneksi tray–ekstensi putus atau tray app keluar, volume tab
     **tetap** di nilai terakhir (tidak melompat keras ke 100%). Nilai
     disimpan per tab selama tab itu hidup (bertahan saat reload, hilang
     saat tab ditutup; tab baru mulai 100%). Klik ikon ekstensi di toolbar
     Opera mengembalikan volume tab aktif ke 100% — jalan keluar bila tray
     tidak jalan. *Keputusan leader.*
6. **Loop satu tombol**, berputar: mati → ulang playlist → ulang 1 lagu → mati.
7. **List lagu playlist** untuk tab YouTube (panel playlist) dan YouTube Music
   (antrean). Klik item → lompat ke lagu itu. Hanya isi playlist/antrean;
   saran autoplay setelah playlist habis tidak ditampilkan.
8. Info: cover, judul, artis/channel, nama app/tab asal.

## 3. Bukan sasaran (versi 1)

- Auto-pause / fade saat tab lain bersuara (meniru Player Opera) — dibuang
  pengguna.
- Antrean/list lagu Spotify (butuh Spotify Web API + Premium).
- Hotkey global, scroll di ikon tray untuk volume — kandidat versi 2.
- Firefox. Ekstensi untuk Chrome/Edge tidak dilarang (Chromium, kode sama),
  tapi yang diuji hanya Opera.
- Publish ke Web Store / Opera Add-ons. Ekstensi dipasang manual (mode
  developer, "Load unpacked").
- Kontrol situs selain youtube.com dan music.youtube.com lewat ekstensi.

## 4. Anti-sasaran (gagal walau "jalan")

- Berat: tray app harus ringan (target < ~60 MB RAM saat idle) karena jalan
  terus di latar belakang.
- Mengganggu pemutaran: kalau tray app mati/crash atau ekstensi kehilangan
  koneksi, musik di YouTube harus tetap jalan normal; volume tab tidak boleh
  "nyangkut" di nilai aneh tanpa cara mengembalikannya.
- Data keluar dari laptop: semua komunikasi hanya lokal (127.0.0.1). Tidak ada
  telemetri, tidak ada server luar.
- Halaman web biasa bisa mengendalikan tray app lewat koneksi lokal itu.

## 5. Pendekatan

Dipertimbangkan untuk tray app:

| Pendekatan | Kelebihan | Kekurangan |
|---|---|---|
| **C# .NET + WPF** (dipilih) | Akses langsung ke SMTC & Core Audio, popup rapi ala Windows 11, ringan, satu .exe | Perlu install .NET SDK |
| Python (pystray + winsdk) | Python sudah terpasang, prototipe cepat | UI kaku, .exe besar, sering kena antivirus |
| Electron (Node) | UI paling fleksibel | Berat (150 MB+ RAM) untuk app yang selalu jalan |

Pengguna memilih **C# .NET + WPF** (juga ingin belajar C#).

Untuk per-tab di Opera, dipertimbangkan: (a) terima batasan SMTC, lock hanya
antar-app; (b) ekstensi pembantu; (c) musik di browser lain. Pengguna memilih
**(b) ekstensi**.

Layout popup: dua mockup ditunjukkan — A (lagu yang di-lock dulu, sumber lain
terlipat) dan B (daftar semua sumber dulu). Pengguna memilih **A**.

## 6. Desain yang sudah diputuskan pengguna

### Popup (layout A), dari atas ke bawah
1. Baris atas: ikon + nama asal ("Opera · tab YouTube", "Spotify"), dan
   tombol gembok (status: Terkunci / tidak).
2. Cover (±64 px) + judul + artis/channel.
3. Progress bar + waktu berjalan / durasi; bisa digeser.
4. Tombol: ⏮ ⏯ (besar, bulat) ⏭, lalu tombol loop.
5. Slider volume + angka persen (batas atas sesuai jenis sumber, lihat §2.5;
   disembunyikan bila sumber tidak mendukung).
6. Bagian playlist: "Playlist · 3 dari 12", beberapa item di sekitar lagu
   aktif, lagu aktif ditandai; bisa di-scroll untuk melihat semua.
   Disembunyikan bila sumber tidak punya playlist.
7. Baris terlipat paling bawah: "N sumber lain memutar" → klik membuka daftar
   sumber lain (ikon kecil, judul, asal, tombol pause, tombol gembok). Klik
   sumber → jadi sumber yang ditampilkan.

### Loop
Satu tombol, siklus mati → ulang playlist → ulang 1 lagu. Ikon berbeda per
mode. Bila sumber tidak mendukung mode tertentu, mode itu dilewati; bila
tidak mendukung loop sama sekali, tombol disembunyikan.

## 7. Arsitektur (keputusan builder/leader, bisa dikoreksi)

```
 Opera
 ┌──────────────────────────────┐         ┌────────────────────────────┐
 │ content script per tab YT    │         │ MediaTray.exe (WPF, tray)  │
 │  - baca status <video>       │         │  - WebSocket server         │
 │  - GainNode volume           │◄──────► │    127.0.0.1:<port>         │
 │  - baca playlist DOM         │  WS     │  - SMTC (app lain)          │
 │ service worker (MV3)         │  JSON   │  - Core Audio (volume app)  │
 │  - kumpulkan semua tab       │         │  - logika sumber + lock     │
 │  - koneksi WebSocket ke tray │         │  - popup UI                 │
 └──────────────────────────────┘         └────────────────────────────┘
```

### Tray app (C#)
- **.NET 10 (LTS), WPF**, target `net10.0-windows10.0.19041.0` agar API WinRT
  (`Windows.Media.Control`) bisa dipakai langsung.
- Ikon tray: library `H.NotifyIcon.Wpf` (WPF tidak punya tray bawaan).
- Volume per-app: Core Audio (`IAudioSessionManager2` / `ISimpleAudioVolume`),
  lewat library `NAudio` (`NAudio.CoreAudioApi`).
- Komunikasi dengan ekstensi: **WebSocket di 127.0.0.1** dengan port tetap
  (konstanta, mis. 47821). Dipilih ketimbang native messaging karena tray app
  jalan sendiri (auto-start), bukan dijalankan oleh browser; native messaging
  akan butuh program relay tambahan + entri registry.
- Keamanan koneksi: hanya bind ke 127.0.0.1; tolak koneksi yang header
  `Origin`-nya bukan `chrome-extension://<id ekstensi>`. ID ekstensi dibuat
  stabil dengan field `key` di `manifest.json`.
- Popup: jendela tanpa border, muncul di atas pojok kanan bawah (dekat tray),
  menutup saat kehilangan fokus. Mengikuti tema terang/gelap Windows.
- Klik kanan ikon tray: menu "Jalankan saat Windows mulai" (toggle, default
  aktif, via `HKCU\...\Run`) dan "Keluar".

### Ekstensi Opera (Manifest V3)
- Content script di `https://www.youtube.com/*` dan `https://music.youtube.com/*`.
- Service worker mengumpulkan status semua tab dan menjaga koneksi WebSocket
  ke tray (reconnect otomatis dengan jeda; pesan heartbeat < 30 detik agar
  service worker tidak ditidurkan).
- Kontrol dasar (play/pause/seek/loop 1 lagu/volume) lewat API standar elemen
  `<video>` dan Web Audio, tidak bergantung tampilan YouTube.
- Next/prev, list playlist, lompat ke lagu, loop playlist: bergantung DOM /
  player YouTube; diisolasi di satu modul per situs (`youtube.js`,
  `ytmusic.js`) agar mudah diperbaiki saat YouTube berubah. Bila gagal,
  fitur itu disembunyikan, bukan error.

### Model sumber
- Sumber = satu tab YouTube/YT Music (dari ekstensi) atau satu sesi SMTC.
- Sesi SMTC milik Opera disembunyikan **bila** judulnya cocok dengan salah
  satu sumber dari ekstensi (menghindari duplikat). Bila tidak cocok (mis.
  situs lain di Opera), tetap ditampilkan sebagai sumber "Opera".
- Tanpa lock: popup menampilkan sumber yang **paling baru mulai memutar**.
  Bila tidak ada yang memutar, tampilkan sumber terakhir yang dijeda.
- Dengan lock: tetap pada sumber itu. Bila sumber hilang (tab ditutup, app
  keluar), lock lepas otomatis dan kembali ke aturan tanpa lock.

## 8. Diserahkan ke builder

- Struktur folder dan nama class, asal mudah dibaca untuk pemula C#.
- Format pesan JSON antara ekstensi dan tray.
- Detail visual (ukuran persis, ikon, animasi kecil) mengikuti mockup A dan
  gaya flyout Windows 11.
- Selektor DOM / cara memanggil player YouTube untuk next/prev/playlist.
- Cara menyimpan setelan kecil (mis. auto-start) — registry/JSON lokal.
- Interval polling posisi lagu.

## 9. Tambahan v1.1 — Widget di taskbar (disetujui pengguna 2026-11-18)

### Alasan
Ikon tray baru di Windows 11 selalu masuk menu tersembunyi (^), jadi pause/next
butuh 3 klik. Tujuan awal pengguna adalah kontrol **langsung di taskbar, satu
klik**. Taskbar pengguna rata tengah dan sisi kirinya selalu kosong (Widgets
tidak dipakai).

### Yang diputuskan pengguna
- Widget ditempel di **ujung kiri taskbar** (area kosong).
- Isi: **cover + judul (+ artis) + ⏮ ⏯ ⏭**.
- Cara: jendela kecil dijadikan *child window* dari taskbar (`Shell_TrayWnd`),
  teknik yang juga dipakai TrafficMonitor — bukan jendela mengambang.

### Perilaku (keputusan leader, bisa dikoreksi)
- Lebar ±300 px (DIP), tinggi = tinggi taskbar, rata kiri dengan margin 8 px.
  Bila ruang sampai tombol Start kurang, widget menyusut (judul dipotong);
  di bawah ±120 px widget disembunyikan.
- Isi dari kiri: cover 32×32 (sudut 4), judul (1 baris, terpotong …) dengan
  artis kecil di bawahnya, lalu ⏮ ⏯ ⏭ (tombol 32 px). Latar transparan
  menyatu dengan taskbar; hover = sorotan halus seperti tombol taskbar.
  Warna teks mengikuti tema taskbar (terang/gelap).
- Klik ⏮ ⏯ ⏭ → langsung ke sumber yang sedang tampil (Current, termasuk yang
  di-lock). Klik cover/judul → buka popup lengkap (layout A) di atas widget,
  pojok kiri bawah. Ikon gembok kecil tampil di widget saat terkunci.
- Tidak ada yang diputar → widget tampil redup: ikon musik + "Tidak ada yang
  diputar"; tombol disembunyikan.
- Ikon tray tetap ada (menu klik kanan + cadangan). Menu tray dapat item baru
  **"Tampilkan di taskbar"** (centang, default aktif).
- Widget tidak ditampilkan (cadangan ke ikon tray saja) bila: ikon taskbar
  diatur rata kiri, tombol Widgets aktif, atau taskbar tidak ditemukan.
- Explorer restart / taskbar dibuat ulang → widget menempel ulang otomatis.
- Hanya taskbar monitor utama. Taskbar auto-hide (dipakai pengguna) → widget ikut
  muncul/sembunyi bersama taskbar; popup diposisikan di atas taskbar yang sedang
  tampil, bukan di dasar work area (yang pada auto-hide = dasar layar).

### Bukan sasaran
- Menggeser/menyusun ulang ikon taskbar lain; taskbar monitor kedua;
  Windows 10.

### Risiko
- Update besar Windows bisa mengubah struktur taskbar → widget gagal tempel;
  app harus tetap jalan (ikon tray) tanpa crash.
- Risiko teknis utama dibuktikan dulu lewat spike: jendela WPF transparan
  sebagai child lintas-proses dari `Shell_TrayWnd` menerima klik dengan benar.
