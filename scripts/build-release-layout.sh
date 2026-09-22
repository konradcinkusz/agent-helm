#!/usr/bin/env bash
# Builds the portable release layout — what the release zip contains — into
# the given directory: framework-dependent publishes of the Bridge, the Web UI
# and the echo agent, the launchers, and the files that ship next to them.
# Used by .github/workflows/release.yml to package a release and by ci.yml to
# smoke-test the same layout on every pull request.
#   usage: scripts/build-release-layout.sh <output-dir>
set -euo pipefail
out="${1:?usage: $0 <output-dir>}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
cd "$(dirname "$0")/.."

dotnet publish src/AgentHelm.Bridge -c Release -o "$out/bridge"
dotnet publish src/AgentHelm.Web -c Release -o "$out/web"
dotnet publish tools/AgentHelm.EchoAgent -c Release -o "$out/echo-agent"

# In the source tree the echo agent is started with `dotnet run`; in the layout
# it is the published DLL next to the Bridge.
jq '(.AgentHelm.Agents[] | select(.Id=="echo") | .Args) = ["${AGENTHELM_DIR}../echo-agent/AgentHelm.EchoAgent.dll"]' \
  "$out/bridge/appsettings.json" > "$out/bridge/appsettings.json.tmp"
mv "$out/bridge/appsettings.json.tmp" "$out/bridge/appsettings.json"

cp scripts/run.sh scripts/run.ps1 README.md LICENSE CONTRIBUTING.md docker-compose.ghcr.yml "$out/"
chmod +x "$out/run.sh"
