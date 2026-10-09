#!/usr/bin/env bash
# Builds AgentHelm as ONE self-contained executable (Bridge API, UI and echo agent
# in one process; see docs/single-file-distribution-plan.md):
#   usage: scripts/build-single-file.sh <rid> <output-dir>
#   rid:   linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64, ...
# Leaves exactly one file in the output directory: agenthelm (agenthelm.exe for win-*).
# The publish goes to a scratch folder and only the executable is copied out, so no
# .pdb, native library or appsettings.json ends up next to it.
set -euo pipefail
rid="${1:?usage: $0 <rid> <output-dir>}"
outdir="${2:?usage: $0 <rid> <output-dir>}"
cd "$(dirname "$0")/.."

exe=agenthelm
case "$rid" in win-*) exe=agenthelm.exe ;; esac

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

# No trimming: ASP.NET Core and Blazor are reflection-heavy. Native libraries and the
# compressed bundle are set in src/AgentHelm.App/AgentHelm.App.csproj.
dotnet publish src/AgentHelm.App -c Release -r "$rid" --self-contained \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=none \
  -o "$scratch/publish"

[ -f "$scratch/publish/$exe" ] || { echo "publish did not produce $exe" >&2; exit 1; }
mkdir -p "$outdir"
cp "$scratch/publish/$exe" "$outdir/$exe"
chmod +x "$outdir/$exe"
echo "built $outdir/$exe ($(du -h "$outdir/$exe" | cut -f1))"
