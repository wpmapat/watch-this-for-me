# Watch This for Me — Plain-English Project Notes

This file explains the project in plain language, assuming no prior coding
background. `DAILY-PLAN.md` tracks *what's done and what's next*; this file
explains *what each piece means and why it exists*. Updated alongside
`DAILY-PLAN.md` as new concepts get introduced — reread it anytime something
from an earlier session doesn't stick.

---

## The big idea, in one paragraph

You tell the system what to watch (a web page, a GitHub repo) and what you
actually care about on it. It checks automatically on a schedule, figures out
whether anything *real* changed (ignoring noise like timestamps), judges
whether that change is actually *meaningful* to you specifically, and — for
changes worth digging into — can investigate further on its own before
writing you an evidence-backed notification. The whole project is really one
pipeline, built up piece by piece: **fetch → detect change → judge → (maybe)
investigate → notify.**

## The core pieces, explained

**Watch** — one thing you've asked the system to keep an eye on. Has a
source (a URL), how often to check it, and — importantly — your own written
description of what would actually matter to you on this specific watch
(e.g. "notify me about breaking changes or security fixes," not just "notify
me about any change").

**Observation** — one snapshot in time of a watch: what the page looked like
when checked, whether it changed from last time, and (if it did) the verdict
on whether that change was meaningful. Every check creates at most one new
observation; the system keeps the last 10 per watch and discards older ones.

**Diff** — a compact summary of exactly what text changed between two
observations (lines added/removed), not the whole page. This is what
actually gets shown to the AI when judging a change — much cheaper and
clearer than handing it two full pages and asking it to spot the difference.

**Meaningful-change classification** — the system doesn't just notice "the
page changed," it asks an AI model to judge: is this *actually* meaningful
given what you said you care about on this watch? A reworded sentence and a
security advisory are both "changes," but only one of them should bother
you. The judgment comes back as: meaningful (yes/no), a short category label,
a confidence score, and a one-sentence explanation.

**Embeddings & similarity (added Week 4)** — every judged observation gets
turned into a list of numbers (an "embedding") that represents its *meaning*,
not its exact wording. Two differently-worded changes that mean roughly the
same thing end up as nearby numbers. Before judging a *new* change, the
system checks "have I seen something like this before on this watch?" and
feeds the closest matches (and how they were judged) into the AI's prompt —
purely so it judges *consistently* over time, not so it skips notifying you
about repeats. A recurring type of meaningful change still gets flagged
every single time, per your own stated criteria.

**MCP (Model Context Protocol)** — a standard way of handing an AI model a
toolbox of specific abilities it can call on demand (e.g. "look up this
watch's history," "check this repo's GitHub issues"), instead of just giving
it one big pile of text. This project runs its *own* small MCP server
exposing a handful of these tools, and the investigation agent below
connects to it as a client.

**Investigation agent** — a separate, deeper capability from the fast
automatic judgment above. Instead of judging off the diff alone, it can
decide *for itself* whether it needs more context, call tools (from the MCP
server) to get it, look at the result, and repeat — up to a hard cap (5 tool
calls) so it can never run away indefinitely. You trigger this explicitly;
it doesn't run automatically on every check, since it's a heavier, slower
operation than the fast path.

**Grounded notification** — once a change is judged meaningful, the system
can write an actual notification: what changed, why it matters, confidence,
and evidence. The key design point: every piece of "evidence" in that
notification has to point back to something the system genuinely gathered
(the real diff, a real tool call result, or a real similar past judgment) —
the AI is not allowed to just assert things and call them evidence.

**User interest profile (added Week 4)** — separate from each watch's own
criteria, there's a single free-text profile of what you broadly care about
(e.g. "C#, .NET, Azure, AI tooling — not mobile or frontend JavaScript").
It's fed into the same judging prompt as a third piece of context. Important
distinction: it doesn't decide *whether* something is meaningful — the
watch's own criteria still owns that — it only shifts *how* a meaningful
change gets explained. Proved this with a real test: the exact same change
through two opposite interest profiles stayed "meaningful" either way, but
the written summary genuinely changed to emphasize the part of the change
that matched each profile.

**Retries and timeouts (added Week 4)** — every network call this system
makes (fetching a web page, calling OpenAI, calling GitHub) can fail for
reasons that have nothing to do with the system itself — a momentary
network blip, a brief rate limit. Rather than giving up immediately on the
first such hiccup, every outbound call now automatically retries a few
times with a backoff delay before actually failing. This isn't custom code
we wrote — it's a standard, well-tested library behavior (`Polly`, via a
Microsoft package) wired onto each network client.

**Idempotency (added Week 4)** — a related but separate concern: if a
check for the same watch somehow runs twice at once (say, a manual check
you trigger happens to overlap with the scheduler's own automatic check of
that same watch), the system must not end up recording the same real-world
change twice, or notifying about it twice. A lock per watch now makes sure
only one check for a given watch actually runs at a time — a second,
overlapping check waits its turn, then finds nothing new to do.

**Execution tracking and dead-lettering (added Week 4)** — every time the
system checks a watch, it now keeps a durable record of what happened to
that specific attempt: did it start, succeed, or fail, and if it failed,
why. Previously this only existed as a log line that would scroll away.
If a watch fails three checks in a row — meaning something is genuinely
broken (a dead link, an expired credential), not just unlucky timing —
the system stops trying it automatically and flags it clearly as needing
attention, rather than quietly failing forever in the background. You can
tell it to try again once you've fixed the underlying problem.

**Evaluation (added Week 5)** — until now, "the classifier works" meant
"it gave sensible answers on the few examples we tried by hand." That's
not evidence. So we built an answer key: 97 example changes, each labeled
by a human as meaningful or not (`eval/dataset.json`), deliberately
including tricky look-alikes — a typo fix sitting right next to a real
breaking change. A small separate program (`eval/WatchThisForMe.Evaluation`)
runs the real classifier against every example and scores it: *precision*
(when it says "meaningful," how often is it right?) and *recall* (of the
truly meaningful changes, how many did it catch?). The first run missed 3;
two of those revealed real blind spots (confusing a parameter's type with
its name, and treating a change to *how* to report vulnerabilities as a new
vulnerability), and two targeted prompt fixes closed them. The last miss
was left alone on purpose — it's debatable even to a human, and bending
the prompt around one arguable label would be overfitting. Re-running this
after any prompt change now tells you whether you made things better or
worse, instead of guessing.

**Observability (added Week 5)** — logs tell you *that* something
happened; tracing tells you *where the time went and where it broke*. Every
watch check now produces a trace: one parent "WatchCheck" step with a child
step for each stage (fetch, diff, classify, embed, save), each timed, and
every outbound call (OpenAI, GitHub, the watched page) captured
automatically. Alongside that, the system counts things worth watching over
time: OpenAI tokens used (which is what costs money), how long each stage
takes, and how many checks fail per watch. This uses OpenTelemetry, an
industry-standard format, so the same data prints to the console on your
machine today and flows into Azure Application Insights once deployed —
no code change needed, just a connection string.

**Security (added Week 5)** — three separate locks, each closing a
different door.

*Who can use it (Entra ID sign-in).* Before this, anyone who could reach
the Api could create watches or trigger investigations — and every one of
those spends your OpenAI money. Now the web UI makes you sign in with a
Microsoft account from your own Entra tenant, and passes a token proving
who you are along to the Api, which rejects any request without one. The
Api locks *every* endpoint by default rather than one at a time, so a new
endpoint added later can't be accidentally left open.

*Where secrets live (Key Vault).* The OpenAI key currently sits in .NET
user-secrets on your machine — fine locally, but there's no such thing on
a server. In Azure, secrets will live in Key Vault, a locked safe the app
reads from at startup. The neat part: none of the code that *uses* the
OpenAI key changed. .NET reads settings from a stack of sources (appsettings
file, user-secrets, environment, Key Vault...), so Key Vault just becomes
one more layer on top.

*How the app proves who it is (Managed Identity).* Even a Key Vault needs
a key to open it — which would just move the problem. Managed Identity
solves that: Azure gives the running app its own identity, and you grant
*that identity* permission to read the vault and the database. No password
exists anywhere to leak. The same goes for Cosmos DB: in Azure the app
connects with its identity instead of an account key. One catch learned
along the way: identity-based access to Cosmos only covers reading and
writing data, not creating databases or containers — so in Azure those get
created by the deployment script, and the app's startup "create if missing"
step only runs against the local emulator.

*The safety rule tying it together.* On your machine, with none of this
configured, everything still works anonymously exactly as before (with a
loud warning in the log). But anywhere other than local development, a
missing sign-in setting makes the app *refuse to start* — a forgotten
setting can never quietly mean "let everyone in."

**The dashboard and watch health (added Week 6)** — the watch list used
to show only what you typed in when creating a watch. Now each row answers
the two questions you'd actually ask at a glance: *is this watch working?*
and *has it found anything?*

*Health* comes from the record of each run (the "executions" added in
Week 4): 🟢 Healthy if the last run worked, 🟡 Needs attention if it
failed but will try again, 🔴 Stopped if it failed too many times in a row
and was switched off, plus Paused and "Waiting for first check". When
something's wrong, the actual error ("No such host is known") is shown
right under the badge, so "needs attention" tells you *what* needs it.

*Last important change* shows the most recent change the AI judged
meaningful, with its category ("New release") and the AI's one-line
summary. The server assembles all of this into one row per watch, so the
page makes a single request instead of several per watch.

*What the product is for.* The Home page now says it plainly: this is
for changes that need **interpreting** ("is this release relevant to me?",
"did this company announce something significant?"), not number alerts
like "tell me when a stock drops below $400" — a brokerage app does those
better. A useful line to have ready if an interviewer asks "could it watch
stocks?".

**A real bug found by testing failure on purpose (Week 6)** — to see the
🟡 and 🔴 states, we created a watch pointing at a website that can't
exist. It went from created to permanently Stopped in about 30 seconds,
even though it was set to check hourly.

*Why:* the scheduler decides a watch is "due" by looking at when it was
last *successfully* checked. A failing watch never succeeds, so it looked
overdue on every scheduler tick (every 10 seconds locally) and burned
through its 3 allowed failures almost instantly.

*Why it mattered:* this wasn't just about broken websites. Any brief
hiccup — 30 seconds of dropped wifi, a site being down for a minute —
would have permanently switched off **every** watch, including healthy
ones, with nobody noticing until they checked the dashboard.

*The fix:* each watch now also remembers when a check was last
*attempted* (success or not), and the scheduler waits a full interval
after any attempt. So an hourly watch now needs 3 hours of continuous
failure before it's stopped, and a daily watch 3 days — which is what
the 3-strikes rule was meant to mean. Verified live: a new broken watch
was tried once and then left alone, instead of three times in 30 seconds.

*The lesson:* the unit tests all passed before this fix — none of them
tried "what if it keeps failing, over time?". Deliberately breaking
something and watching what happens caught what testing the happy path
never would.

**Testing what happens when things go wrong (Week 6)** — after finding
that bug, we went looking for more, deliberately.

*Fast tests of the whole check pipeline.* The heart of the system — fetch
the page, spot a change, ask the AI, save, retry, give up after 3 failures
— had no automated tests at all. Now it has 19, using *fakes*: pretend
versions of the website, the AI and the database that give scripted
answers ("fail twice, then succeed"). They run in under a second, cost no
OpenAI money, and can force situations that are hard to create for real.
One proved something reassuring: **if OpenAI is down when a change
arrives, the change isn't lost** — it's classified on the next check. And
a test is only worth having if it fails when the bug comes back, so we
temporarily removed this morning's fix and confirmed the test caught it.

*Load test — what if there are lots of busy watches?* We ran a separate
copy of the app with a separate database, pointed 40 watches at a fake
website that published a new security fix or breaking change on *every*
visit, and set each to check every minute — with the real AI judging every
change. Results:
- Nothing broke and every judgment was right, but the app **fell behind**:
  it managed about 16 checks a minute, so each watch was checked every ~2.5
  minutes instead of every 1. The cause is that the scheduler checks
  watches **one at a time**. OpenAI was not the bottleneck.
- The website stayed fast for normal requests, but the dashboard slowed to
  over a second at 40 watches, because it reads every saved page snapshot
  just to show a one-line summary.
Both have known fixes (check several watches at once; save the summary on
the watch itself) — noted for later rather than done now.

*What if our own Api goes down?* Checking stops, because the scheduler
lives inside the Api — but nothing is lost: websites keep their content,
and when the Api comes back it catches up automatically, reporting
everything that changed meanwhile as one combined change. Three fixes came
out of this:
- The Watches page used to show a wall of technical error text when the Api
  was unreachable. Now it says *"We can't reach the monitoring service right
  now"* with a Try again button.
- A **health check** (`/health`): a tiny address Azure can ping every
  minute to ask "are you alive?", and restart the app or raise an alert if
  not. It deliberately only checks the app itself, not the database —
  otherwise a short database hiccup would make Azure restart a perfectly
  healthy app over and over, which fixes nothing.
- A hidden crash risk: if the database had a hiccup at the exact moment the
  scheduler asked for the list of watches, the error went unhandled, and
  .NET's default reaction is to **shut down the whole app**. Now the
  scheduler logs it and simply tries again on its next round — with a test
  that proves it.

**When a watched website stops responding, then comes back (Week 6)** —
while a site is down, each check fails, the watch shows 🟡 with the
reason, and it retries on its normal schedule. After 3 failures in a row
(3 hours for an hourly watch) it's 🔴 Stopped. We saw this happen for
real: "Broken Source Test 2" failed at 16:54, 17:55 and 18:55, then
stopped.

When the site comes back, the next check compares it against the **last
snapshot saved before the outage** — so nothing is skipped, but
everything that changed meanwhile arrives as *one* combined change (two
releases during the outage = one alert mentioning both). Something
published and then removed during the outage is never seen.

A stopped watch used to stay stopped forever unless you sent a hand-written
API request. Now stopped and paused watches have a **Resume** button, which
switches the watch back on and checks it straight away. Resuming gives a
**fresh set of 3 attempts** — otherwise the old failures would still count,
and the very next hiccup would stop it again.

**The bug that made the AI blind (Week 6, fixed Nov 18)** — the page
cleanup turned every real web page into *one long line*. A line-by-line
diff of one line against one line is just "whole old page / whole new
page", and it was cut off at 4,000 characters — so on real pages the AI
was mostly judging the menu and header, never the new release. Our unit
tests missed it because they fed the differ hand-made text that already had
line breaks. Now the cleanup keeps one line per heading, paragraph and list
item, and an end-to-end test runs real-looking HTML through the same path
the app uses. Lesson: test the pieces *joined together*, with realistic input.

Saved snapshots now carry a **format version**. When the cleanup changes,
old snapshots are either upgraded in place or (if they can't be) used as a
fresh starting point — otherwise every watch would raise one false alert
just because *we* changed, not the page.

**When the page moves or breaks (Week 6)** — a site that answers "200 OK"
isn't necessarily fine: it may redirect to its homepage, or show a "page not
found" message with a success code. The app now notices both (the redirect
from the final address, the "not found" page from the AI's new
`page_unavailable` category) and shows 🟡 with the reason. Fetch errors are
in plain words ("The page wasn't found (404)…"), there's an **Edit** button
to fix the address, and once the page is back, it's compared with the last
*real* version, not with the error page.

**What watching costs (Week 6 cost audit)** — a check where nothing changed
costs no AI at all; a change costs 3 small AI calls (about $0.0004). The
audit found one page that "changed" every hour only because it prints a
random trace id — now random ids are masked like dates and counts. Run
records now expire after 30 days, and deleting a watch deletes its history.

## How to see the shape of the whole system

A visual class diagram of the current architecture was published as a
Claude Artifact on 2026-10-27 — see your `/artifacts` list in Claude Code, or
ask for a refreshed one if it's been a while since new pieces were added.

## What's not built yet

No actual notification delivery yet (email/Slack — the setting exists,
nothing sends yet; a small in-app version is planned this week, real email
at deploy time), no protection yet against watching internal addresses
(SSRF — next up), and nothing is deployed anywhere outside this machine —
the Key Vault, Managed Identity and Entra sign-in code is built and switches
on by configuration, but isn't proven against real Azure resources until
they're created during deployment.
See
`DAILY-PLAN.md` for exactly what's next.
