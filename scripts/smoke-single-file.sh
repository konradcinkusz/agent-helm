#!/usr/bin/env bash
# Smoke test for the single file (see build-single-file.sh). Starts it from an empty
# folder the way a user does, and checks what a broken build loses without any error:
# the health route, the UI and the static files that only the file itself can serve,
# and that the built-in echo agent (this same executable) finishes a turn. Last, the
# output folder must hold nothing but the executable.
#   usage: scripts/smoke-single-file.sh <output-dir>
set -euo pipefail
out="$(cd "${1:?usage: $0 <output-dir>}" && pwd)"
exe="$out/agenthelm"
port=5399
base="http://127.0.0.1:$port"
work="$(mktemp -d)"
log="$(mktemp)"
pid=""

fail() { echo "SMOKE TEST FAILED: $*" >&2; exit 1; }
cleanup() {
  [ -n "$pid" ] && kill "$pid" 2>/dev/null || true
  rm -rf "$work"
  echo "--- app output ---"
  cat "$log"
}
trap cleanup EXIT

[ -x "$exe" ] || fail "$exe is missing or not executable"

cd "$work"
"$exe" --no-browser --urls "$base" >"$log" 2>&1 &
pid=$!

for _ in $(seq 60); do
  curl -fsS -o /dev/null "$base/api/health" 2>/dev/null && break
  kill -0 "$pid" 2>/dev/null || fail "the app exited during startup"
  sleep 1
done
health="$(curl -fsS "$base/api/health")" || fail "/api/health does not answer"
echo "health: $health"

curl -fsS -o /dev/null "$base/" || fail "the UI does not serve /"
for asset in app.css AgentHelm.Web.styles.css chat.js _framework/blazor.web.js; do
  curl -fsS -o /dev/null "$base/$asset" || fail "the single file does not serve $asset"
done

agents="$(curl -fsS "$base/api/agents")"
jq -e 'any(.[]; .id == "echo")' <<<"$agents" >/dev/null || fail "the agent catalog has no echo agent"

session="$(curl -fsS -m 60 -X POST "$base/api/sessions" \
  -H 'content-type: application/json' \
  -d "$(jq -n --arg cwd "$work" '{agentId: "echo", cwd: $cwd}')")"
id="$(jq -r .id <<<"$session")"
curl -fsS -o /dev/null -X POST "$base/api/sessions/$id/prompt" \
  -H 'content-type: application/json' -d '{"text":"hello from the single-file smoke test"}'

replied=""
for _ in $(seq 30); do
  detail="$(curl -fsS "$base/api/sessions/$id")"
  if jq -e '.status == "idle" and any(.transcript[]; .role == "assistant")' <<<"$detail" >/dev/null; then
    replied="$(jq -r '[.transcript[] | select(.role == "assistant")][0].text' <<<"$detail")"
    break
  fi
  sleep 1
done
[ -n "$replied" ] || fail "the echo agent did not finish a turn: ${detail:-no session detail}"
echo "echo agent replied: $replied"

kill "$pid"
wait "$pid" 2>/dev/null || true
pid=""

files="$(ls -A "$out")"
[ "$files" = "agenthelm" ] || fail "the output folder should hold only agenthelm, found: $files"
echo "SMOKE TEST PASSED"
