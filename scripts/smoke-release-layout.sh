#!/usr/bin/env bash
# Smoke test for the release layout (see build-release-layout.sh): starts it
# with its own launcher, the way a user does, and checks what a broken
# launcher loses without any error — the Bridge's agent catalog, the UI's
# assets — and that the built-in echo agent completes a turn.
#   usage: scripts/smoke-release-layout.sh <release-layout-dir>
set -euo pipefail
dist="$(cd "${1:?usage: $0 <release-layout-dir>}" && pwd)"
bridge=http://127.0.0.1:5199
web=http://127.0.0.1:5200
log="$(mktemp)"

# A process group of its own, so the launcher, the Bridge and the UI can be
# stopped together.
setsid "$dist/run.sh" >"$log" 2>&1 &
launcher=$!
cleanup() {
  kill -- "-$launcher" 2>/dev/null || true
  echo "--- launcher output ---"
  cat "$log"
}
trap cleanup EXIT

fail() { echo "SMOKE TEST FAILED: $*" >&2; exit 1; }

for _ in $(seq 60); do
  curl -fsS -o /dev/null "$web/" 2>/dev/null && break
  sleep 1
done
curl -fsS -o /dev/null "$web/" || fail "the UI did not come up on $web"

agents="$(curl -fsS "$bridge/api/agents")"
echo "agents: $agents"
jq -e 'any(.[]; .id == "echo")' <<<"$agents" >/dev/null \
  || fail "the Bridge has no echo agent — did it load bridge/appsettings.json?"

curl -fsS -o /dev/null "$web/app.css" || fail "the UI does not serve app.css — did it find web/wwwroot?"

session="$(curl -fsS -m 60 -X POST "$bridge/api/sessions" \
  -H 'content-type: application/json' \
  -d "$(jq -n --arg cwd "$dist" '{agentId: "echo", cwd: $cwd}')")"
id="$(jq -r .id <<<"$session")"
curl -fsS -o /dev/null -X POST "$bridge/api/sessions/$id/prompt" \
  -H 'content-type: application/json' -d '{"text":"hello from the smoke test"}'

for _ in $(seq 30); do
  detail="$(curl -fsS "$bridge/api/sessions/$id")"
  if jq -e '.status == "idle" and any(.transcript[]; .role == "assistant")' <<<"$detail" >/dev/null; then
    echo "echo agent replied: $(jq -r '[.transcript[] | select(.role == "assistant")][0].text' <<<"$detail")"
    echo "SMOKE TEST PASSED"
    exit 0
  fi
  sleep 1
done
fail "the echo agent did not finish a turn: $detail"
