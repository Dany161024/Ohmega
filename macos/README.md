# Ohmega for macOS

A native SwiftUI app with the same engine as the Linux version (`ohmega_core.py`): the same ffmpeg
commands, targets, album gain and backups. Universal binary (Intel + Apple Silicon), macOS 13 or newer.

## Build

On a Mac with the Command Line Tools (`xcode-select --install`):

```sh
macos/build.sh            # → macos/build/Ohmega.app and Ohmega-1.3.0.dmg
macos/build.sh --install  # …and copy it to /Applications
```

The script needs the FFmpeg binary in `macos/vendor/ffmpeg` (not in git):

1. Download the static release build from [evermeet.cx](https://evermeet.cx/ffmpeg/) — the macOS build linked
   from [ffmpeg.org/download.html](https://ffmpeg.org/download.html), e.g. `ffmpeg-9.0.2.zip` and its `.sig`.
2. Verify it: `gpg --import 0x1A660874.asc` (from evermeet.cx), then
   `gpg --verify ffmpeg-9.0.2.zip.sig ffmpeg-9.0.2.zip` → *Good signature from "static FFmpeg binaries"*,
   key `20F6 EA3E 0CFD 6B4C 5344 7A73 476C 4B61 1A66 0874`.
3. Unzip it into `macos/vendor/`.

evermeet.cx publishes Intel builds only; on Apple Silicon ffmpeg runs through Rosetta 2
(Ohmega tells the user to install it if it is missing).

## Licences

Ohmega is GPL-3.0. The bundled FFmpeg 9.0.2 is built with `--enable-gpl --enable-version3`
(no non-free parts), so it is GPL-3.0 as well; its source code is at
[ffmpeg.org/download.html](https://ffmpeg.org/download.html). The About window says the same.

## Not signed by Apple

There is no Apple Developer account behind Ohmega, so the app is signed ad-hoc and not notarized.
The first time, macOS refuses to open it: open **System Settings → Privacy & Security**, scroll down
and click **Open Anyway** next to Ohmega. After that it opens normally.

## Development helpers

```sh
Ohmega.app/Contents/MacOS/Ohmega --selftest <folder>       # normalize every file to −14 LUFS, print before → after
Ohmega.app/Contents/MacOS/Ohmega --snapshot out.png <files…> [--then-normalize]   # window screenshot
```
