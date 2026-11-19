#!/usr/bin/env bash
# Outage scenario: our Api AND the watched site are down while real releases are published.
# See TESTING.md ("Scenario 6") for the full walkthrough and the results from 2026-11-17.
#
# Prerequisites (from the repo root, Cosmos emulator running, OpenAI key in user-secrets):
#   1. python -I tests/scenarios/fake_release_site.py            (fake site on :5098)
#   2. Start a SEPARATE Api on :5013 with a throwaway database:
#        ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5013 \
#        CosmosDb__DatabaseName=WatchThisForMeScenario Sources__AllowPrivateAddresses=true \
#        dotnet run --no-launch-profile --project src/WatchThisForMe.Api
#      (AllowPrivateAddresses: the fake site is on localhost, which the SSRF guard refuses otherwise.)
#   3. source tests/scenarios/outage_scenario.sh, then run step1 ... step5 in order,
#      stopping/starting the :5013 Api where the steps say so.
#   4. Clean up: dotnet run tests/scenarios/drop_database.cs WatchThisForMeScenario

A=http://localhost:5013/api/watches
S=http://localhost:5098
STATE_DIR="${STATE_DIR:-${TMPDIR:-/tmp}/wtfm-scenario}"
mkdir -p "$STATE_DIR"
# Windows (Git Bash): give Python a path it understands (/tmp/... -> C:/Users/.../Temp/...).
command -v cygpath >/dev/null && STATE_DIR=$(cygpath -m "$STATE_DIR")

mode()    { curl -s -o /dev/null "$S/admin/mode?page=$1&mode=$2"; echo "   site $1 -> $2"; }
publish() { curl -s -o /dev/null -G "$S/admin/publish" --data-urlencode "page=$1" --data-urlencode "text=$2"; echo "   published on $1: $2"; }
wid()     { cat "$STATE_DIR/id-$1"; }

# Runs a check and reports it. Note: when nothing changed, the Api returns the PREVIOUS
# observation (old diff included) - compare stored snapshot counts to tell the difference.
check() {
  local id; id=$(wid "$1")
  local code; code=$(curl -s -o "$STATE_DIR/last.json" -w "%{http_code}" -X POST "$A/$id/check")
  if [ "$code" = "200" ]; then
    python -c "
import json; o=json.load(open(r'$STATE_DIR/last.json'))
d=o.get('diffSummary')
print('   check $1: OK | diff:', (d or 'none').replace(chr(10),' / ')[:300])
if d: print('     AI: meaningful=%s category=%s | %s' % (o.get('meaningful'), o.get('category'), o.get('classificationSummary')))"
  else
    echo "   check $1: FAILED (HTTP $code)"
  fi
  curl -s "$A/$id/observations" | python -c "import json,sys; print('     stored snapshots:', len(json.load(sys.stdin)))"
}

step1() {  # Everything healthy: publish v1.0, create one watch per product, take baselines.
  for p in product-a product-b; do
    publish $p "v1.0: Initial release with core features."
    curl -s -X POST $A -H "Content-Type: application/json" -d "{\"name\":\"Scenario $p\",\"userRequest\":\"scenario\",\"source\":\"$S/$p\",\"sourceType\":\"WebPage\",\"checkFrequency\":\"01:00:00\",\"meaningfulChangeCriteria\":\"Security fixes or breaking changes\",\"notificationPolicy\":\"OnlyMeaningfulChanges\",\"notificationChannel\":\"Email\"}" \
      | python -c "import sys,json; print(json.load(sys.stdin)['id'])" > "$STATE_DIR/id-$p"
    check $p
  done
}

step2() {  # Run AFTER stopping the :5013 Api. Product A hard-down (503), B shows a 200 maintenance page.
  mode product-a down
  mode product-b maintenance
  for p in product-a product-b; do publish $p "v1.1: Security fix for a remote code execution vulnerability in the file upload handler. Upgrade immediately."; done
}

step3() {  # Run AFTER restarting the :5013 Api (same database). Sites still broken.
  check product-a
  check product-b
}

step4() {  # Breaking change published while sites are still broken.
  for p in product-a product-b; do publish $p "v2.0: Breaking change: removed the legacy Client.Connect API; callers must migrate to Session.Open."; done
  check product-a
  check product-b
}

step5() {  # Both sites recover - does the agent catch v1.1 AND v2.0?
  mode product-a up
  mode product-b up
  check product-a
  check product-b
}
