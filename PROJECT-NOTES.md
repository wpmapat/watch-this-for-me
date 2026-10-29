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

## How to see the shape of the whole system

A visual class diagram of the current architecture was published as a
Claude Artifact on 2026-10-27 — see your `/artifacts` list in Claude Code, or
ask for a refreshed one if it's been a while since new pieces were added.

## What's not built yet

No actual notification delivery yet (email/Slack — the setting exists,
nothing sends yet), and nothing is deployed anywhere outside this machine.
See
`DAILY-PLAN.md` for exactly what's next.
