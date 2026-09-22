#!/usr/bin/env bash
# AgentHelm launcher (release layout): Bridge + Web as local processes.
# Requires the .NET 8 ASP.NET Core runtime. Postgres (history) is optional —
# without it AgentHelm runs memory-only and says so in the logs.
set -euo pipefail
cd "$(dirname "$0")"

# OTEL preconfig — routes Copilot SDK telemetry to CopilotScope collector.
OTEL_ENDPOINT="${OTEL_EXPORTER_OTLP_ENDPOINT:-http://localhost:4318}"
export COPILOT_OTEL_ENABLED="true"
export COPILOT_OTEL_EXPORTER_TYPE="otlp-http"
export OTEL_EXPORTER_OTLP_ENDPOINT="$OTEL_ENDPOINT"
export OTEL_EXPORTER_OTLP_PROTOCOL="http/protobuf"
export OTEL_EXPORTER_OTLP_TRACES_PROTOCOL="http/protobuf"
export OTEL_EXPORTER_OTLP_METRICS_PROTOCOL="http/protobuf"
export OTEL_EXPORTER_OTLP_LOGS_PROTOCOL="http/protobuf"
export OTEL_EXPORTER_OTLP_HEADERS="${OTEL_EXPORTER_OTLP_HEADERS:-x-api-key=dev-secret-123}"
export OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT="true"

# Each app starts from its own folder: ASP.NET Core takes the current directory
# as its content root, which is where it finds appsettings.json (the Bridge's
# agent catalog) and wwwroot (the UI's styles and scripts).
echo "AgentHelm — Bridge on http://127.0.0.1:5199"
(cd bridge && exec dotnet AgentHelm.Bridge.dll) &
BRIDGE_PID=$!
trap 'kill $BRIDGE_PID 2>/dev/null || true' EXIT

WEB_URLS="${AGENTHELM_WEB_URLS:-http://127.0.0.1:5200}"
echo "AgentHelm — Web on $WEB_URLS"
cd web
ASPNETCORE_URLS="$WEB_URLS" dotnet AgentHelm.Web.dll
