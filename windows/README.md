# Ohmega for Windows

A native Windows app (C#, WinForms on .NET Framework 4.8 — built into every Windows 10 and 11) with the same
engine as the Linux version (`ohmega_core.py`) and the Mac app: the same ffmpeg commands, targets, album gain,
backups and profiles (`%APPDATA%\Ohmega\profiles.json`, same format as `~/.config/ohmega/profiles.json`).

## Build (on Linux)

```sh
windows/build.sh               # → windows/build/Ohmega.exe
windows/build.sh --installer   # …and Ohmega-1.3.1-Windows-Setup.exe
```

- `Ohmega.exe` needs the .NET SDK in `~/.dotnet` (no root: `dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet`);
  NuGet fetches the .NET Framework 4.8 reference assemblies.
- The installer is made with NSIS (`makensis`) and needs `ffmpeg.exe` from the
  [gyan.dev release essentials build](https://www.gyan.dev/ffmpeg/builds/) (GPL-3.0, linked from
  [ffmpeg.org/download.html](https://ffmpeg.org/download.html)), checked against its published SHA-256.
  It installs for the current user only (`%LOCALAPPDATA%\Programs\Ohmega`, no admin rights), adds a Start menu
  shortcut and an entry in Settings → Apps.
- `Ohmega.ico` comes from `make_icon.py` (Pillow).

## Checking without a screen

```
Ohmega.exe --selftest <folder> <out.txt>       scan + normalize to −14 LUFS + rescan, results into out.txt
Ohmega.exe --snapshot out.png [paths…] [--then-normalize]   the window with these files, saved as PNG
```
