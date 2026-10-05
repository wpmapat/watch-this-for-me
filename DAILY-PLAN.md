# Watch This for Me — Daily Plan & Progress Tracker

This file tracks daily progress against the 5–6 week build plan in
`watch-this-for-me-project.md`. Weekends are intentionally excluded.
Dates are a *target guide*, not a hard deadline — if a day runs long
or short, just shift the remaining checkboxes rather than the dates.

**Available time:** 2 hours on Mondays, 3 hours Tuesday–Friday (14 hours/week).
Each day's description is scoped to fit that session, not a full workday —
treat it as "what to get done this session," and it's fine to roll the
last bit into the next session if something runs over.

---

## Current Status

- **Active week:** Week 1 — Foundation + basic monitoring
- **Active branch:** `feature/week1-foundation`
- **Last updated:** 2026-10-04

### Done so far
- [x] GitHub repo created (`watch-this-for-me`)
- [x] Solution scaffolded — Api / Core / Infrastructure / Web / Tests
- [x] `Watch` domain model + supporting enums (Core)
- [x] Cosmos DB storage layer — `IWatchRepository`, `CosmosWatchRepository`, startup initializer, DI wiring
- [ ] Verify Cosmos DB Emulator connection end-to-end *(next up)*
- [ ] Watch CRUD API endpoints
- [ ] Web page connector
- [ ] Observation storage + basic change detection
- [ ] Scheduler
- [ ] NL → structured watch config (minimal AI)

---

## Week 1 — Foundation + basic monitoring (Oct 5 – Oct 9)
Goal: a working end-to-end system, almost no AI yet.

| Date | Day | Hrs | Focus | Status |
|---|---|---|---|---|
| Oct 5 | Mon | 2h | Verify Cosmos DB Emulator connection | [ ] |
| Oct 6 | Tue | 3h | Watch CRUD API endpoints | [ ] |
| Oct 7 | Wed | 3h | Web page connector | [ ] |
| Oct 8 | Thu | 3h | Observation storage + basic change detection | [ ] |
| Oct 9 | Fri | 3h | Scheduler + minimal AI + basic UI | [ ] |

**Mon Oct 5 — Verify Cosmos DB Emulator connection.** Get the local
emulator running and confirm the storage layer actually works.
- Start Docker Desktop and run the Cosmos DB Emulator container
- Run the Api project and confirm `CosmosDbInitializer` creates the database/container without errors
- Manually create and read back one test `Watch` to prove the repository works

**Tue Oct 6 — Watch CRUD API endpoints.** Expose the repository
through a controller so watches can be managed over HTTP.
- Add a `WatchesController` with Create/Get/List/Update/Delete endpoints
- Add a request/response DTO for creating a watch (don't expose the domain model directly)
- Smoke-test each endpoint via the `.http` file or the OpenAPI UI

**Wed Oct 7 — Web page connector.** Build the first source connector:
fetch a URL and produce a comparable snapshot of it.
- Add a web page fetcher in Infrastructure that downloads a page's HTML
- Normalize the content (strip scripts/styles/whitespace) so trivial noise isn't mistaken for a change
- Compute a content hash of the normalized text

**Thu Oct 8 — Observation storage + basic change detection.** Persist
what we've seen before so we can tell if anything changed.
- Add an `Observation` entity + Cosmos container/repository (same pattern as `Watch`)
- Compare each new hash against the last stored observation
- Store a new observation when the hash differs; otherwise just update `LastCheckedAt`

**Fri Oct 9 — Scheduler + minimal AI + basic UI.** Close the loop:
automatic checks, natural-language watch creation, and a page to see it all.
- Add a background service that checks active watches on their `CheckFrequency`
- Add one LLM call that turns a natural-language request into a structured `Watch`
- Add a bare-bones Blazor page listing watches and a form to create one

## Week 2 — Semantic change detection (Oct 12 – Oct 16)
Goal: tell "page changed" apart from "something important changed."

| Date | Day | Hrs | Focus | Status |
|---|---|---|---|---|
| Oct 12 | Mon | 2h | Content normalization | [ ] |
| Oct 13 | Tue | 3h | Document snapshot history | [ ] |
| Oct 14 | Wed | 3h | Basic textual diff | [ ] |
| Oct 15 | Thu | 3h | LLM semantic classification | [ ] |
| Oct 16 | Fri | 3h | Wire pipeline end-to-end | [ ] |

**Mon Oct 12 — Content normalization.** Make the "did it change" signal
less noisy before AI even gets involved.
- Strip HTML/scripts/nav boilerplate, keep just the meaningful text
- Normalize whitespace and dynamic bits (timestamps, counters) that shouldn't count as changes

**Tue Oct 13 — Document snapshot history.** Keep a short history of
versions, not just the latest, so we can diff and reason about trends.
- Extend the Observation model/container to keep a handful of recent snapshots per watch
- Add a way to fetch "the last N observations for a watch"

**Wed Oct 14 — Basic textual diff.** Compute what actually changed
between two snapshots, in a form an LLM (and a human) can read.
- Add a line/word-level diff between previous and current content
- Produce a compact diff summary — don't pass huge raw diffs downstream

**Thu Oct 15 — LLM semantic classification.** Ask the LLM whether the
diff is actually meaningful, not just "text changed."
- Design a prompt using the diff + the watch's `MeaningfulChangeCriteria`
- Return structured output: changed/meaningful/category/confidence/summary

**Fri Oct 16 — Wire pipeline end-to-end.** Put normalize → diff →
classify together and prove it on a real scenario.
- Run the full pipeline against real or simulated .NET release notes
- Confirm a trivial change comes back "not meaningful" and a security fix comes back "meaningful"

## Week 3 — Agent + MCP (Oct 19 – Oct 23)
Goal: turn the system into a genuine multi-step agent. Most important week for the AI story.

| Date | Day | Hrs | Focus | Status |
|---|---|---|---|---|
| Oct 19 | Mon | 2h | MCP server: initial tools | [ ] |
| Oct 20 | Tue | 3h | GitHub connector as MCP tools | [ ] |
| Oct 21 | Wed | 3h | Agent investigation loop | [ ] |
| Oct 22 | Thu | 3h | Grounded notification generation | [ ] |
| Oct 23 | Fri | 3h | End-to-end agentic demo test | [ ] |

**Mon Oct 19 — MCP server: initial tools.** Stand up your own MCP
server with the first couple of tools.
- Scaffold an MCP server project
- Implement `get_web_page()` and `get_previous_observation()`/`save_observation()` as MCP tools wrapping existing Infrastructure code

**Tue Oct 20 — GitHub connector as MCP tools.** Add GitHub as a second
source type, exposed as MCP tools.
- Implement `get_github_release()` and `get_github_issues()` via GitHub's REST API
- Register them alongside the web page tools

**Wed Oct 21 — Agent investigation loop.** Let the agent decide what
to look at next instead of following a fixed sequence.
- Loop: agent sees a change, decides if it needs more info, calls a tool, inspects the result, repeats or finalizes
- Cap the number of tool calls per investigation so it can't run away

**Thu Oct 22 — Grounded notification generation.** Turn investigation
findings into the evidence-backed notification format from the spec.
- Prompt for "what changed / why it matters / confidence / evidence" using only facts gathered via tool calls
- Make sure every claim links back to a real source

**Fri Oct 23 — End-to-end agentic demo test.** Prove Demo 3 (agentic
investigation) actually works.
- Run the full flow against a real GitHub repo release
- Fix rough edges — this is the week the AI story depends on most

## Week 4 — RAG + production workflow (Oct 26 – Oct 30)
Goal: give the agent memory and make it production-sturdy.

| Date | Day | Hrs | Focus | Status |
|---|---|---|---|---|
| Oct 26 | Mon | 2h | Vector store + embeddings | [ ] |
| Oct 27 | Tue | 3h | RAG retrieval | [ ] |
| Oct 28 | Wed | 3h | User interest profile | [ ] |
| Oct 29 | Thu | 3h | Retries, timeouts, idempotency | [ ] |
| Oct 30 | Fri | 3h | Execution state + dead-letter handling | [ ] |

**Mon Oct 26 — Vector store + embeddings.** Get the infrastructure in
place to store and search embeddings.
- Set up Azure AI Search (or a simpler local vector store to start)
- Generate and store embeddings for each observation's summary

**Tue Oct 27 — RAG retrieval.** Let the agent ask "have I seen
something like this before?"
- Add retrieval of semantically similar past observations for a watch
- Feed the top matches into the classification/investigation prompt as context

**Wed Oct 28 — User interest profile.** Let a user's stated interests
shape relevance judgments.
- Add a simple interest profile (technologies, languages, things to care about)
- Use it as extra context in the relevance/meaningful-change prompt

**Thu Oct 29 — Retries, timeouts, idempotency.** Make the pipeline
resilient to transient failures.
- Add retry policies around external calls (fetches, LLM calls)
- Make watch execution idempotent so a retried run doesn't double-notify

**Fri Oct 30 — Execution state + dead-letter handling.** Make failures
visible and recoverable instead of silent.
- Track execution state per watch run (started/succeeded/failed/retrying)
- Add a dead-letter path for runs that fail repeatedly

## Week 5 — Evaluation + productionization (Nov 2 – Nov 6)
Goal: prove it actually works, and make it deployable. Don't skip this week.

| Date | Day | Hrs | Focus | Status |
|---|---|---|---|---|
| Nov 2 | Mon | 2h | Build evaluation dataset | [ ] |
| Nov 3 | Tue | 3h | Run evaluation | [ ] |
| Nov 4 | Wed | 3h | Observability | [ ] |
| Nov 5 | Thu | 3h | Security | [ ] |
| Nov 6 | Fri | 3h | CI/CD + deploy | [ ] |

**Mon Nov 2 — Build evaluation dataset.** Start collecting labeled
examples to measure the system against.
- Gather ~100 historical changes (a mix of meaningful and not) from sources you're already watching
- Label each one (meaningful: yes/no) to create a ground-truth set

**Tue Nov 3 — Run evaluation.** Measure how good the relevance filter
actually is.
- Run the classifier against the labeled set; compute precision/recall/false positives/negatives
- Tune the prompt or thresholds based on results; re-run until it's reasonably good

**Wed Nov 4 — Observability.** Make the system's behavior visible,
not just its output.
- Add Application Insights/OpenTelemetry tracing across fetch → diff → LLM → notify
- Track tokens, latency, and failures per watch execution

**Thu Nov 5 — Security.** Lock down secrets and access before
deploying anywhere public.
- Move connection strings/API keys to Key Vault
- Add Entra ID auth and use Managed Identity for Azure resource access

**Fri Nov 6 — CI/CD + deploy.** Get the app into Azure for real.
- Write Bicep for the core resources (Cosmos DB, Container Apps/Functions, Key Vault, App Insights)
- Set up a GitHub Actions pipeline that builds and deploys on push to master

## Week 6 — Polish (optional stretch) (Nov 9 – Nov 13)
Goal: make the existing product excellent — don't add new features.

| Date | Day | Hrs | Focus | Status |
|---|---|---|---|---|
| Nov 9 | Mon | 2h | Dashboard UI polish | [ ] |
| Nov 10 | Tue | 3h | Execution history view | [ ] |
| Nov 11 | Wed | 3h | Prepare Demo 1 + Demo 2 | [ ] |
| Nov 12 | Thu | 3h | Prepare Demo 3 | [ ] |
| Nov 13 | Fri | 3h | Final polish | [ ] |

**Mon Nov 9 — Dashboard UI polish.** Make the watch list look like a
real product.
- Add status indicators (🟢/🟡) and "last checked"/"last important change" summaries per watch

**Tue Nov 10 — Execution history view.** Let a user see what the
agent actually did, step by step.
- Add a timeline view per watch: fetch → change detected → AI analysis → investigation → notification, with timestamps

**Wed Nov 11 — Prepare Demo 1 + Demo 2.** Get the simple and
filtering demos interview-ready.
- Script and rehearse "watch this repo for releases" and "only notify me about security/breaking changes," with sample data lined up

**Thu Nov 12 — Prepare Demo 3.** Get the agentic investigation demo
interview-ready.
- Script and rehearse the full detect → investigate → report flow, with the UI showing the agent's steps

**Fri Nov 13 — Final polish.** Spend the last session fixing things,
not adding anything new.
- Fix whatever broke during demo rehearsal
- Re-read notifications/UI copy for clarity; cut anything confusing
