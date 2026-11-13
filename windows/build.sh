#!/bin/bash
# build.sh — Ohmega for Windows, built on Linux.
#   ./build.sh               → build/Ohmega.exe (+ Ohmega.exe.config)
#   ./build.sh --installer   → also build/Ohmega-<version>-Windows-Setup.exe, made on the shitbook (NSIS + ffmpeg.exe
#                              in ~/ohmega-build/windows/vendor, fetched by its stiahni-ffmpeg.sh)
# Needs the dotnet SDK in ~/.dotnet (no root: dotnet-install.sh --channel 8.0 --install-dir ~/.dotnet).
# Icon: Ohmega.ico from make_icon.py.
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
DOTNET=${DOTNET:-$HOME/.dotnet/dotnet}
VERSION=$(grep -oP '(?<=<Version>)[^<]+' Ohmega.csproj)

echo "== compile $VERSION"
"$DOTNET" build Ohmega.csproj -c Release -o build -nologo -clp:NoSummary -warnaserror
ls -la build/Ohmega.exe build/Ohmega.exe.config

if [[ " $* " == *" --installer "* ]]; then
    echo "== installer (on the shitbook)"
    ssh shitbook "mkdir -p ~/ohmega-build/windows/in"
    scp -q build/Ohmega.exe build/Ohmega.exe.config installer.nsi Ohmega.ico ../LICENSE shitbook:ohmega-build/windows/in/
    ssh shitbook "cd ~/ohmega-build/windows && test -f vendor/ffmpeg.exe || ./stiahni-ffmpeg.sh >/dev/null && \
        makensis -V2 -DVERSION=$VERSION -DFFMPEG=\$(cat vendor/ffmpeg-verzia.txt) in/installer.nsi"
    scp -q "shitbook:ohmega-build/windows/in/Ohmega-$VERSION-Windows-Setup.exe" build/
    ls -la "build/Ohmega-$VERSION-Windows-Setup.exe"
    sha256sum "build/Ohmega-$VERSION-Windows-Setup.exe"
fi
