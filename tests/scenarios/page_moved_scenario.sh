#!/usr/bin/env bash
# Scenario: the watched page changes "version" (redesign, moved to /v2) or its address stops
# being valid (404, redirect to the homepage, a "page not found" page served with HTTP 200).
# See TESTING.md ("Scenario 8") for the walkthrough and results.
#
# Prerequisites: same as outage_scenario.sh (fake site on :5098, separate Api on :5013 with
# the WatchThisForMeScenario database). Then: source tests/scenarios/page_moved_scenario.sh
# and run step1 ... step4 in order.

source "$(dirname "${BASH_SOURCE[0]}")/outage_scenario.sh"   # helpers: mode, publish, wid, check

PAGES="redesign moved home soft404 gone"
CRITERIA="New releases, security fixes or breaking changes"

health() {  # health <page>: what the dashboard shows for this watch
  curl -s "$A/summaries" | python -c "
import json,sys
for s in json.load(sys.stdin):
  if s['watch']['id']=='$(wid "$1")': print('     dashboard:', s['health'], '|', s['watch']['status'], '|', s['lastError'] or '')"
}

step1() {  # Healthy pages with two releases each; create one watch per page and take baselines.
  for p in $PAGES; do
    publish $p "v1.0: Initial release with core features."
    publish $p "v1.1: Faster startup and a new settings page."
    curl -s -X POST $A -H "Content-Type: application/json" -d "{\"name\":\"Scenario $p\",\"userRequest\":\"scenario\",\"source\":\"$S/$p\",\"sourceType\":\"WebPage\",\"checkFrequency\":\"01:00:00\",\"meaningfulChangeCriteria\":\"$CRITERIA\",\"notificationPolicy\":\"OnlyMeaningfulChanges\",\"notificationChannel\":\"Email\"}" \
      | python -c "import sys,json; print(json.load(sys.stdin)['id'])" > "$STATE_DIR/id-$p"
    check $p
  done
}

step2() {  # Each page changes in its own way - no new release anywhere.
  for p in $PAGES; do mode $p $p; done
  for p in $PAGES; do echo "== $p"; check $p; health $p; done
}

step3() {  # A real release on the moved page: caught through the redirect? The 404 page: stopped after 3?
  publish moved "v1.2: Security fix for a token leak in the login flow."
  echo "== moved (new release after the move)"; check moved; health moved
  echo "== gone (2 more failures)"; check gone; check gone; health gone
}

step4() {  # The owner finds the page's new address, updates the watch and resumes it.
  publish gone-new "v1.0: Initial release with core features."
  publish gone-new "v1.1: Faster startup and a new settings page."
  local id; id=$(wid gone)
  curl -s "$A/$id" | python -c "
import json,sys; w=json.load(sys.stdin); w['source']='$S/gone-new'; print(json.dumps(w))" \
    | curl -s -o /dev/null -w "   PUT new address: HTTP %{http_code}\n" -X PUT "$A/$id" -H "Content-Type: application/json" -d @-
  curl -s -o /dev/null -w "   resume: HTTP %{http_code}\n" -X POST "$A/$id/resume"
  echo "== gone -> gone-new (same releases at the new address)"; check gone; health gone
}
