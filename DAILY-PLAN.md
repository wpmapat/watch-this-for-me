# Watch This for Me — Daily Plan & Progress Tracker

This file tracks daily progress against the 5–6 week build plan in
`watch-this-for-me-project.md`. Weekends are intentionally excluded.
Dates are a *target guide*, not a hard deadline — if a day runs long
or short, just shift the remaining checkboxes rather than the dates.

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

| Date | Day | Focus | Status |
|---|---|---|---|
| Oct 5 | Mon | Start Cosmos DB Emulator, verify storage layer (create/read a Watch) | [ ] |
| Oct 6 | Tue | Watch CRUD API endpoints (Create/Get/List/Update/Delete) | [ ] |
| Oct 7 | Wed | Web page connector (fetch a URL, normalize + hash content) | [ ] |
| Oct 8 | Thu | Observation storage + basic change detection (store snapshots, compare hashes) | [ ] |
| Oct 9 | Fri | Scheduler (periodic checks) + minimal AI step (NL request → structured Watch) + basic Blazor UI page | [ ] |

## Week 2 — Semantic change detection (Oct 12 – Oct 16)
Goal: tell "page changed" apart from "something important changed."

| Date | Day | Focus | Status |
|---|---|---|---|
| Oct 12 | Mon | Content normalization (strip noise/boilerplate from fetched pages) | [ ] |
| Oct 13 | Tue | Document snapshot history (keep prior versions for diffing) | [ ] |
| Oct 14 | Wed | Basic textual diff | [ ] |
| Oct 15 | Thu | LLM semantic classification (changed/meaningful/category/confidence) + structured output | [ ] |
| Oct 16 | Fri | Wire pipeline end-to-end; test against the .NET release demo scenario | [ ] |

## Week 3 — Agent + MCP (Oct 19 – Oct 23)
Goal: turn the system into a genuine multi-step agent. Most important week for the AI story.

| Date | Day | Focus | Status |
|---|---|---|---|
| Oct 19 | Mon | MCP server: initial tools (fetch page, get/save observation) | [ ] |
| Oct 20 | Tue | GitHub connector as MCP tools (releases, issues) | [ ] |
| Oct 21 | Wed | Agent investigation loop (decide if more info is needed, call tools, iterate) | [ ] |
| Oct 22 | Thu | Wire investigation output into grounded notification generation | [ ] |
| Oct 23 | Fri | End-to-end test of the agentic investigation demo; fix issues | [ ] |

## Week 4 — RAG + production workflow (Oct 26 – Oct 30)
Goal: give the agent memory and make it production-sturdy.

| Date | Day | Focus | Status |
|---|---|---|---|
| Oct 26 | Mon | Azure AI Search / vector store + embeddings for observations | [ ] |
| Oct 27 | Tue | RAG retrieval ("has something similar happened before?") | [ ] |
| Oct 28 | Wed | User interest profile (store + use as context) | [ ] |
| Oct 29 | Thu | Retries, timeout handling, idempotency in the pipeline | [ ] |
| Oct 30 | Fri | Execution state tracking + dead-letter/error handling | [ ] |

## Week 5 — Evaluation + productionization (Nov 2 – Nov 6)
Goal: prove it actually works, and make it deployable. Don't skip this week.

| Date | Day | Focus | Status |
|---|---|---|---|
| Nov 2 | Mon | Build evaluation dataset (~100 historical changes, labeled meaningful/not) | [ ] |
| Nov 3 | Tue | Run evaluation — precision/recall/false positives/negatives; tune filtering | [ ] |
| Nov 4 | Wed | Observability (App Insights / OpenTelemetry) across the pipeline | [ ] |
| Nov 5 | Thu | Security (Entra ID auth, Managed Identity, Key Vault secrets) | [ ] |
| Nov 6 | Fri | CI/CD + Bicep infra, deploy to Azure | [ ] |

## Week 6 — Polish (optional stretch) (Nov 9 – Nov 13)
Goal: make the existing product excellent — don't add new features.

| Date | Day | Focus | Status |
|---|---|---|---|
| Nov 9 | Mon | Dashboard UI polish (watch list with status indicators) | [ ] |
| Nov 10 | Tue | Execution history timeline view | [ ] |
| Nov 11 | Wed | Prepare Demo 1 (simple monitoring) + Demo 2 (intelligent filtering) | [ ] |
| Nov 12 | Thu | Prepare Demo 3 (agentic investigation) | [ ] |
| Nov 13 | Fri | Final polish, bug fixes, rehearse demos | [ ] |
