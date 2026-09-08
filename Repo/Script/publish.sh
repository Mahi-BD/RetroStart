#!/usr/bin/env bash
# Builds the Windows x64 binaries from any OS (needs .NET 8 SDK). Output → ./out
set -euo pipefail
cd "$(dirname "$0")/../.."
DOTNET=${DOTNET:-$([ -x "$HOME/.dotnet/dotnet" ] && echo "$HOME/.dotnet/dotnet" || command -v dotnet)}
"$DOTNET" publish src/RetroStart -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:PublishReadyToRun=false -o out/fdd
"$DOTNET" publish src/RetroStart -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o out/sc
ls -la out/fdd out/sc
