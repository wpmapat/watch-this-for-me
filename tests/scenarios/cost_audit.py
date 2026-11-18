"""Cost & noise audit: how much does each watch cost to run, and is it worth it?

Reads every watch's stored history from a running Api and reports, per watch:
  - how often a check finds a "change" (each change = 3 AI calls)
  - how many of those changes were meaningful (the rest is noise we paid for)
  - estimated AI tokens and dollars per month at the watch's schedule
  - how many run records are stored, and how fast they grow

Usage:  python -I tests/scenarios/cost_audit.py [api-base-url]     (default http://localhost:5003)

Token counts are ESTIMATES (characters / 4), since tokens aren't stored per watch. Prices are
constants below - update them from https://openai.com/api/pricing before relying on dollars.
"""
import json
import sys
import urllib.request
from datetime import datetime, timedelta, timezone

API = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5003").rstrip("/") + "/api/watches"

# USD per 1M tokens (gpt-4o-mini input/output, text-embedding-3-small) - check current prices.
PRICE_IN, PRICE_OUT, PRICE_EMBED = 0.15, 0.60, 0.02
CLASSIFIER_INSTRUCTIONS_CHARS = 4200   # fixed prompt in OpenAiChangeClassifier (approx.)
PAST_CONTEXT_CHARS = 3 * 250           # up to 3 similar past changes, ~250 chars each
OUTPUT_TOKENS = 80                     # one JSON verdict with a one-sentence summary


def get(path):
    with urllib.request.urlopen(API + path) as r:
        return json.load(r)


def parse_time(s):
    return datetime.fromisoformat(s.replace("Z", "+00:00"))


def parse_span(s):  # "01:00:00" or "1.00:00:00"
    days, _, rest = s.partition(".") if s.count(":") == 2 and "." in s.split(":")[0] else ("0", "", s)
    h, m, sec = (float(x) for x in rest.split(":"))
    return timedelta(days=int(days), hours=h, minutes=m, seconds=sec)


def tokens(chars):
    return chars / 4


now = datetime.now(timezone.utc)
rows, total_month, total_records = [], 0.0, 0

for w in get(""):
    wid, freq = w["id"], parse_span(w["checkFrequency"])
    observations = get(f"/{wid}/observations?count=1000")
    runs = get(f"/{wid}/executions?count=100000")
    total_records += len(runs)

    # Only the last 10 snapshots are kept, so measure the change rate over the window they cover.
    window_start = min((parse_time(o["checkedAt"]) for o in observations), default=now)
    changes = [o for o in observations if o.get("diffSummary")]
    successful_runs = [r for r in runs if r["status"] == "Succeeded" and parse_time(r["startedAt"]) >= window_start]
    change_rate = min(1.0, len(changes) / len(successful_runs)) if successful_runs else 0.0
    meaningful = sum(1 for o in changes if o.get("meaningful"))

    avg_diff = sum(len(o["diffSummary"]) for o in changes) / len(changes) if changes else 0
    avg_summary = sum(len(o.get("classificationSummary") or "") for o in changes) / len(changes) if changes else 0
    classify_in = tokens(CLASSIFIER_INSTRUCTIONS_CHARS + len(w["meaningfulChangeCriteria"]) + PAST_CONTEXT_CHARS + avg_diff)
    cost_per_change = (classify_in * PRICE_IN + OUTPUT_TOKENS * PRICE_OUT
                       + tokens(avg_diff + avg_summary) * PRICE_EMBED) / 1_000_000

    checks_month = timedelta(days=30) / freq if w["status"] == "Active" else 0
    changes_month = checks_month * change_rate
    cost_month = changes_month * cost_per_change
    total_month += cost_month

    oldest_run = min((parse_time(r["startedAt"]) for r in runs), default=now)
    rows.append((w["name"][:38], w["status"], f"{freq}", f"{len(changes)}/{len(successful_runs)}", f"{change_rate:.0%}",
                 f"{meaningful}/{len(changes)}", f"{avg_diff:,.0f}", f"{changes_month:,.0f}", f"${cost_month:.3f}",
                 f"{len(runs)} ({(now - oldest_run).days}d)", f"{timedelta(days=365) / freq:,.0f}"))

headers = ("Watch", "Status", "Every", "Changes/checks", "Change rate", "Meaningful", "Avg diff chars",
           "AI changes/mo", "AI $/mo", "Run records (age)", "Records/yr")
widths = [max(len(str(x)) for x in col) for col in zip(headers, *rows)]
for row in (headers, *rows):
    print("  ".join(str(v).ljust(n) for v, n in zip(row, widths)))
print(f"\nTotal estimated AI cost: ${total_month:.2f}/month - run records stored: {total_records}")
print("Change rate = share of successful checks that found a change (each change = 3 AI calls).")
