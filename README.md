# Media Tray

Ikon kecil di system tray Windows 11 untuk mengontrol musik yang sedang diputar
(tab YouTube / YouTube Music di Opera, Spotify, dan app lain) tanpa membuka
jendela app-nya. Desain: [docs/superpowers/specs/2026-11-18-media-tray-design.md](docs/superpowers/specs/2026-11-18-media-tray-design.md).

## Kebutuhan

- Windows 11
- .NET 10 SDK. Bila belum ada, pasang per-user tanpa admin dengan skrip resmi Microsoft:

  ```powershell
  Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
  .\dotnet-install.ps1 -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
  ```

- Node.js (hanya untuk menjalankan tes ekstensi)

## Menjalankan tray app

```powershell
dotnet run --project src/MediaTray
```

## Tes

```powershell
dotnet test MediaTray.sln
node --test "extension/tests/*.test.mjs"
```

## Memasang ekstensi di Opera

1. Buka `opera://extensions`.
2. Aktifkan **Developer mode** (pojok kanan atas).
3. Klik **Load unpacked** lalu pilih folder `extension/` di project ini.

Klik ikon ekstensi di toolbar Opera untuk mengembalikan volume tab aktif ke 100%.

## Batasan yang diketahui

- Popup selalu muncul di pojok kanan bawah monitor utama.
- Setelah ekstensi di-reload atau diperbarui, muat ulang (reload) tab YouTube yang sedang terbuka agar tersambung kembali.
- Gambar sampul dimuat dari server gambar YouTube (`i.ytimg.com` dan sejenisnya), jadi butuh koneksi internet.
