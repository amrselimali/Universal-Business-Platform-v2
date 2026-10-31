#!/usr/bin/env bash
# Smoke test: opens every registered screen with a real login and reports non-200 pages.
# Usage: BASE_URL=http://127.0.0.1:5080 SMOKE_USER=admin SMOKE_PASS='...' bash scripts/smoke-pages.sh
set -uo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5080}"
SMOKE_USER="${SMOKE_USER:?SMOKE_USER is required}"
SMOKE_PASS="${SMOKE_PASS:?SMOKE_PASS is required}"
JAR="$(mktemp)"
trap 'rm -f "$JAR"' EXIT

code=$(curl -s -c "$JAR" -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/v1/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"username\":\"$SMOKE_USER\",\"password\":\"$SMOKE_PASS\"}")
if [ "$code" != "200" ]; then echo "LOGIN FAILED ($code)"; exit 1; fi
echo "login: 200"

routes=$(grep -rhoE '"/[a-zA-Z0-9/._-]*"' src/Ubp.Core/Security/AppRegistry*.cs \
  | tr -d '"' | sort -u)

fail=0
total=0
for route in $routes; do
  total=$((total + 1))
  status=$(curl -s -b "$JAR" -o /dev/null -w '%{http_code}' "$BASE_URL$route")
  if [ "$status" = "200" ]; then
    printf 'OK   %-45s %s\n' "$route" "$status"
  else
    printf 'FAIL %-45s %s\n' "$route" "$status"
    fail=$((fail + 1))
  fi
done

for api in /health/live /api/v1/system/health /api/v1/system/me; do
  total=$((total + 1))
  status=$(curl -s -b "$JAR" -o /dev/null -w '%{http_code}' "$BASE_URL$api")
  printf 'API  %-45s %s\n' "$api" "$status"
  [ "$status" = "200" ] || fail=$((fail + 1))
done

echo "---"
echo "checked=$total failed=$fail"
[ "$fail" -eq 0 ]
