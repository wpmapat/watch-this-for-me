"""Load test: many watches, every check a meaningful change, real OpenAI.

Runs a fake 'release notes' site that changes on every fetch, creates N watches against a
separate Api instance (separate Cosmos database), samples Api latency while the scheduler is
busy, then reports what actually happened from the run records.
See TESTING.md ("Scenario 4") for the results from 2026-11-17.

Usage (repo root; Cosmos emulator running; OpenAI key in user-secrets; costs a few cents):
  1. Start a SEPARATE Api on :5013 with a throwaway database:
       ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5013 \
       CosmosDb__DatabaseName=WatchThisForMeLoadTest \
       dotnet run --no-launch-profile --project src/WatchThisForMe.Api
  2. python -I tests/scenarios/load_test.py [watches=40] [seconds=360]
  3. Stop that Api, then: dotnet run tests/scenarios/drop_database.cs WatchThisForMeLoadTest
"""
import json, random, statistics, sys, threading, time, urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

API = "http://localhost:5013"
SITE_PORT = 5099
N_WATCHES = int(sys.argv[1]) if len(sys.argv) > 1 else 40
DURATION_S = int(sys.argv[2]) if len(sys.argv) > 2 else 360

CODENAMES = "aurora birch cobalt dune ember fjord granite harbor iris juniper kestrel lumen meadow nimbus onyx pine quartz raven sierra tundra umber vale willow zephyr".split()
COMPONENTS = "authentication parser serializer session-handler file-upload router cache template-engine connection-pool token-validator".split()
ISSUES = ["remote code execution", "SQL injection", "privilege escalation", "cross-site scripting", "denial of service"]
APIS = ["Client.Connect", "Options.Timeout", "Session.Save", "Request.Body", "Config.Load", "Stream.Read"]

site_state = {}
site_lock = threading.Lock()

def new_release():
    name = f"{random.choice(CODENAMES).title()} {random.choice(CODENAMES).title()}"
    if random.random() < 0.5:
        change = f"Security fix: patched a {random.choice(ISSUES)} vulnerability in the {random.choice(COMPONENTS)}. Upgrade immediately."
    else:
        change = f"Breaking change: removed {random.choice(APIS)}; callers must migrate to the new {random.choice(COMPONENTS)} API."
    return f"<h2>Release {name}</h2><p>{change}</p>"

class Site(BaseHTTPRequestHandler):
    def do_GET(self):
        with site_lock:
            # Every fetch publishes a new release (keeping the latest 5), so every check
            # after the baseline sees a real, meaningful change.
            releases = site_state.setdefault(self.path, [])
            releases.insert(0, new_release())
            del releases[5:]
            body = "<html><body><h1>Product release notes</h1>" + "".join(releases) + "</body></html>"
        data = body.encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/html")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def log_message(self, *args):
        pass

def http(method, path, body=None, timeout=60):
    req = urllib.request.Request(API + path, method=method,
                                 data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Content-Type": "application/json"})
    start = time.perf_counter()
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        payload = resp.read()
    return (time.perf_counter() - start), (json.loads(payload) if payload else None)

def pct(values, p):
    if not values:
        return float("nan")
    values = sorted(values)
    return values[min(len(values) - 1, int(round(p / 100 * (len(values) - 1))))]

def main():
    server = ThreadingHTTPServer(("localhost", SITE_PORT), Site)
    threading.Thread(target=server.serve_forever, daemon=True).start()

    # Baseline API latency before any load.
    idle = [http("GET", "/api/watches")[0] for _ in range(5)]

    ids = []
    for i in range(N_WATCHES):
        _, w = http("POST", "/api/watches", {
            "name": f"LoadTest-{i:02d}", "userRequest": "load test",
            "source": f"http://localhost:{SITE_PORT}/product-{i}",
            "sourceType": "WebPage", "checkFrequency": "00:01:00",
            "meaningfulChangeCriteria": "New releases, security fixes or breaking changes",
            "notificationPolicy": "OnlyMeaningfulChanges", "notificationChannel": "Email"})
        ids.append(w["id"])
    t0 = datetime.now(timezone.utc)
    print(f"created {len(ids)} watches at {t0:%H:%M:%S}; running {DURATION_S}s", flush=True)

    list_lat, summ_lat = [], []
    end = time.time() + DURATION_S
    next_report = time.time() + 60
    while time.time() < end:
        list_lat.append(http("GET", "/api/watches")[0])
        summ_lat.append(http("GET", "/api/watches/summaries")[0])
        if time.time() >= next_report:
            print(f"  {int(DURATION_S - (end - time.time()))}s elapsed; list p50 {statistics.median(list_lat)*1000:.0f}ms, summaries p50 {statistics.median(summ_lat)*1000:.0f}ms", flush=True)
            next_report += 60
        time.sleep(5)

    # Gather results from the run records.
    runs, gaps, first_check_delay = [], [], []
    meaningful = changed = 0
    for wid in ids:
        _, execs = http("GET", f"/api/watches/{wid}/executions?count=100")
        execs = sorted(execs, key=lambda e: e["startedAt"])
        starts = [datetime.fromisoformat(e["startedAt"]) for e in execs]
        if starts:
            first_check_delay.append((starts[0] - t0).total_seconds())
        gaps += [(b - a).total_seconds() for a, b in zip(starts, starts[1:])]
        for e in execs:
            dur = (datetime.fromisoformat(e["completedAt"]) - datetime.fromisoformat(e["startedAt"])).total_seconds() if e.get("completedAt") else None
            runs.append((e["status"], dur, e.get("errorMessage")))
        _, obs = http("GET", f"/api/watches/{wid}/observations?count=100")
        changed += sum(1 for o in obs if o.get("diffSummary"))
        meaningful += sum(1 for o in obs if o.get("meaningful"))

    ok = [d for s, d, _ in runs if s == "Succeeded" and d is not None]
    failed = [(d, m) for s, d, m in runs if s == "Failed"]
    expected = N_WATCHES * (DURATION_S // 60 + 1)
    print("\n===== RESULTS =====")
    print(f"watches: {N_WATCHES}, frequency: every 60s, duration: {DURATION_S}s")
    print(f"checks wanted (if on schedule): ~{expected}   checks actually run: {len(runs)}  ({len(runs)/max(expected,1):.0%})")
    print(f"succeeded: {len(ok)}   failed: {len(failed)}   in flight at end: {sum(1 for s, d, _ in runs if d is None)}")
    print(f"changes classified by AI: {changed}   judged meaningful: {meaningful}")
    print(f"check duration (succeeded): p50 {pct(ok,50):.1f}s  p90 {pct(ok,90):.1f}s  max {max(ok) if ok else float('nan'):.1f}s")
    print(f"gap between checks of the same watch (target 60s): p50 {pct(gaps,50):.0f}s  p90 {pct(gaps,90):.0f}s  max {max(gaps) if gaps else float('nan'):.0f}s")
    print(f"time until each watch's FIRST check: p50 {pct(first_check_delay,50):.0f}s  max {max(first_check_delay) if first_check_delay else float('nan'):.0f}s")
    print(f"API latency idle:              p50 {statistics.median(idle)*1000:.0f}ms")
    print(f"API latency under load (list): p50 {pct(list_lat,50)*1000:.0f}ms  p90 {pct(list_lat,90)*1000:.0f}ms  max {max(list_lat)*1000:.0f}ms")
    print(f"dashboard summaries under load: p50 {pct(summ_lat,50)*1000:.0f}ms  p90 {pct(summ_lat,90)*1000:.0f}ms  max {max(summ_lat)*1000:.0f}ms")
    errors = {}
    for _, m in failed:
        errors[(m or "")[:120]] = errors.get((m or "")[:120], 0) + 1
    for m, c in errors.items():
        print(f"  error x{c}: {m}")
    server.shutdown()

if __name__ == "__main__":
    main()
