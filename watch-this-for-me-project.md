# Watch This for Me — AI-Powered Persistent Monitoring Agent

## 1\. Project idea

**Watch This for Me** is an AI agent that lets a user describe something they care about monitoring in natural language.

Examples:

> “Watch the .NET 11 release notes and tell me if there are breaking changes affecting ASP.NET Core.”

> “Monitor this GitHub repository and tell me when a new release contains security fixes.”

> “Watch the Azure Cosmos DB documentation and alert me if anything changes around vector search.”

> “Monitor this product and let me know if its price drops below $500.”

The system converts the request into a **persistent monitoring job**.

It periodically:

1. Retrieves fresh information.
2. Determines whether anything meaningful changed.
3. Compares it with previous observations.
4. Uses an LLM to understand the significance of the change.
5. Decides whether the user should be notified.
6. Optionally performs deeper investigation.
7. Produces a concise explanation with links and evidence.

The important distinction is:

**This isn't a chatbot that answers a question. It's an AI system that continues working after the user leaves.**

\---

## 2\. Example user experience

### Create a watch

User:

> Watch the .NET GitHub repository. Tell me whenever a new release comes out, but only notify me if it contains breaking changes or security-related fixes.

The AI responds:

> \*\*Watch created\*\*
>
> Source: dotnet/runtime GitHub repository  
> Event: New release  
> Filter: Breaking changes or security fixes  
> Frequency: Daily  
> Notification: Email

### Several days later

A new release appears.

The system sees:

```text
Previous version: 10.0.1
New version:      10.0.2
```

Rather than blindly notifying:

> “New .NET release available!”

the AI investigates the release notes and concludes:

> \*\*Potentially important .NET update\*\*
>
> .NET 10.0.2 was released today. The release includes a security fix affecting ASP.NET Core authentication.
>
> \*\*Why this matters:\*\* Your watch specifically requested security-related changes.
>
> Source: GitHub release

That's the interesting AI problem.

\---

## 3\. High-level architecture

```text
                         ┌──────────────────────┐
                         │      Web UI           │
                         │                      │
                         │ Create / View Watches│
                         │ Review Notifications │
                         └──────────┬───────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │     API / Backend    │
                         │       ASP.NET Core   │
                         └──────────┬───────────┘
                                    │
                  ┌─────────────────┼──────────────────┐
                  │                 │                  │
                  ▼                 ▼                  ▼
          ┌──────────────┐ ┌──────────────┐ ┌────────────────┐
          │ Watch        │ │ Watch        │ │ Notification   │
          │ Definition   │ │ Scheduler    │ │ Service        │
          └──────────────┘ └──────┬───────┘ └────────────────┘
                                  │
                                  ▼
                         ┌──────────────────────┐
                         │ Monitoring Workers   │
                         │                      │
                         │ Fetch → Detect → AI  │
                         └──────────┬───────────┘
                                    │
                   ┌────────────────┼────────────────┐
                   │                │                │
                   ▼                ▼                ▼
             ┌──────────┐     ┌──────────┐     ┌──────────┐
             │ Web      │     │ GitHub   │     │ RSS/API  │
             │ Watcher  │     │ Watcher  │     │ Watcher  │
             └──────────┘     └──────────┘     └──────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │     AI Agent         │
                         │                      │
                         │ Change analysis      │
                         │ Relevance analysis   │
                         │ Investigation         │
                         │ Summarization        │
                         └──────────┬───────────┘
                                    │
                                    ▼
                         ┌──────────────────────┐
                         │       MCP Layer      │
                         │                      │
                         │ Web tools            │
                         │ GitHub tools         │
                         │ Search tools         │
                         │ Notification tools   │
                         └──────────────────────┘
```

\---

## 4\. Core components

### A. Watch Definition

This is the central object.

A watch might contain:

```text
Watch
 ├── Name
 ├── User request
 ├── Source
 ├── Source type
 ├── Check frequency
 ├── What constitutes a change?
 ├── What constitutes a meaningful change?
 ├── Investigation instructions
 ├── Notification preferences
 └── Current state
```

Example:

```text
Name:
.NET Security Watch

Source:
https://github.com/dotnet/runtime/releases

Monitor:
New releases

Meaningful change:
Security fixes or breaking changes

Investigation:
Read release notes and determine affected components

Notify:
Only when meaningful

Frequency:
Every 6 hours
```

The user doesn't necessarily have to fill these fields out manually. They can simply say:

> “Watch the .NET runtime releases and alert me about security fixes.”

The AI turns that into a structured watch definition.

That's the first agentic capability.

\---

## 5\. Monitoring pipeline

Each execution follows roughly:

```text
Scheduled
   │
   ▼
Retrieve source
   │
   ▼
Normalize content
   │
   ▼
Compare with previous observation
   │
   ├── No change ──────────────► Done
   │
   ▼
Change detected
   │
   ▼
Determine whether change is meaningful
   │
   ├── Not meaningful ─────────► Store observation
   │
   ▼
Investigate
   │
   ▼
Generate explanation
   │
   ▼
Notify user
```

This gives you a very nice demo because you can actually show the agent progressing through those states.

\---

## 6\. Where the AI comes in

### 6.1 Natural-language watch creation

User:

> “Keep an eye on Azure Cosmos DB vector search and let me know if Microsoft announces something that could affect applications using the Python SDK.”

AI extracts a structured watch configuration such as:

```json
{
  "topic": "Azure Cosmos DB vector search",
  "sources": \[
    "documentation",
    "announcements",
    "GitHub"
  ],
  "event\_types": \[
    "documentation\_change",
    "announcement",
    "sdk\_change"
  ],
  "notification\_policy": "Only notify when potentially relevant"
}
```

### 6.2 Semantic change detection

Traditional monitoring asks:

> Did the webpage change?

Your system asks:

> \*\*Did anything important change?\*\*

For example:

```text
Version 1:
"The API supports authentication using X."

Version 2:
"The API supports authentication using X and Y."
```

Text changed, but perhaps not important.

Whereas:

```text
Version 1:
"Feature available in preview."

Version 2:
"Feature is no longer supported."
```

is extremely important.

The AI determines the difference.

### 6.3 Relevance determination

Suppose Microsoft publishes 30 changes.

The user's watch only cares about:

```text
Cosmos DB
+
vector search
+
Python SDK
```

The agent needs to filter out irrelevant information.

This is a classic **semantic relevance** problem.

### 6.4 Agentic investigation

A particularly good scenario:

```text
Change detected
      ↓
Is it potentially relevant?
      ↓
Yes
      ↓
Search for additional information
      ↓
Read documentation
      ↓
Check GitHub issues
      ↓
Determine impact
      ↓
Generate report
```

The LLM isn't merely summarizing information supplied to it.

It is deciding **what information to retrieve next**.

That's where the project starts looking like a genuine agent.

\---

## 7\. MCP

MCP should be one of the major learning goals.

You could implement your own MCP server exposing tools such as:

```text
get\_web\_page()
get\_github\_release()
get\_github\_issues()
search\_web()
get\_rss\_feed()
compare\_documents()
send\_notification()
get\_previous\_observation()
save\_observation()
```

Then the AI agent can decide which tools it needs.

For example:

```text
User asks:
"Is this .NET release important?"

Agent:

1. get\_github\_release()
2. get\_github\_issues()
3. search\_web()
4. compare\_documents()
5. generate\_analysis()
6. send\_notification()
```

This demonstrates the **tool-use aspect of AI agents** very clearly.

\---

## 8\. RAG

RAG doesn't need to be the central feature.

The project can use RAG where it genuinely makes sense.

The system can retain:

```text
Previous observations
Previous notifications
Watch configuration
User's interests
Relevant historical changes
```

When a new change occurs:

> “Has something similar happened before?”

The agent can retrieve previous observations and use them as context.

Another possibility is letting the user provide a small "interest profile":

```text
Technologies:
.NET
ASP.NET Core
Azure
Cosmos DB

Languages:
C#
Java

Things I care about:
Breaking changes
Security vulnerabilities
Pricing changes
Deprecations
```

That becomes part of the retrieval/context process.

\---

## 9\. Other AI concepts the project can demonstrate

|Concept|How the project demonstrates it|
|-|-|
|LLM|Understanding watch requests and analyzing changes|
|Prompt engineering|Change/relevance/investigation prompts|
|Structured output|Convert natural language into Watch definitions|
|Function/tool calling|Agent invokes monitoring tools|
|MCP|Standardized external tools|
|RAG|Historical observations and user context|
|Embeddings|Semantic similarity between observations|
|Vector search|Find related historical changes|
|Agentic workflows|AI decides what to investigate|
|Multi-step reasoning|Detect → investigate → assess → report|
|Human-in-the-loop|User approves sensitive actions|
|Long-running agents|Watches execute over days/weeks|
|Event-driven architecture|Changes trigger AI workflows|
|Semantic diffing|Understand meaning rather than raw text|
|Confidence|Agent communicates uncertainty|
|Grounding|Notifications link to source evidence|
|Evaluation|Measure false positives/negatives|
|Observability|Track agent/tool/model behavior|
|Guardrails|Prevent unsupported conclusions/actions|

\---

## 10\. Azure technologies

Since the goal is also to demonstrate modern Azure/C# experience, keep the stack strongly Azure-oriented.

### Application

**ASP.NET Core / C#**

This demonstrates that existing backend skills remain relevant.

### AI

**Azure OpenAI / Microsoft Foundry models**

Use the LLM for:

* watch creation
* semantic classification
* investigation
* summarization
* structured extraction

### Data

**Azure Cosmos DB**

Good fit for:

* watches
* observations
* execution history
* notifications
* user preferences

### Search

**Azure AI Search**

Potentially used for:

* historical observations
* semantic search
* vector search
* RAG

### Compute

**Azure Container Apps**

for the API and background workers.

### Scheduling / messaging

Azure Functions, Container Apps Jobs, or an Azure messaging service could handle scheduled monitoring and asynchronous execution.

The exact choice can be finalized during architecture work; avoid over-engineering version 1.

### Security

Microsoft Entra ID + Managed Identity + Key Vault.

### Infrastructure

**Bicep**

Everything should be deployable from source control.

\---

## 11\. Important architectural concept: durable state

The project shouldn't be:

```text
cron
  ↓
call LLM
  ↓
send email
```

The interesting architecture is:

```text
                    ┌─────────────┐
                    │ Watch       │
                    │ Definition  │
                    └──────┬──────┘
                           │
                           ▼
                    ┌─────────────┐
                    │ Scheduler   │
                    └──────┬──────┘
                           │
                           ▼
                    ┌─────────────┐
                    │ Execution   │
                    │ Instance    │
                    └──────┬──────┘
                           │
               ┌───────────┼───────────┐
               ▼           ▼           ▼
            Fetch       Compare     AI Agent
               │           │           │
               └───────────┼───────────┘
                           ▼
                    ┌─────────────┐
                    │ Observation │
                    └──────┬──────┘
                           │
                    meaningful?
                       /       \\
                     no         yes
                     │           │
                     ▼           ▼
                   Store       Investigate
                                   │
                                   ▼
                                Notify
```

This gives you concepts like:

* stateful workflows
* asynchronous processing
* retries
* idempotency
* execution history
* failure recovery
* eventual consistency

\---

## 12\. Don't start with 20 source types

For the first version, support only **three**.

### 1\. Web pages

Very generic.

Example:

> Monitor this Microsoft documentation page.

### 2\. GitHub

Extremely useful for software developers.

Monitor:

* releases
* issues
* security advisories
* repository changes

### 3\. RSS / Atom

Provides an easy way to demonstrate monitoring news/blog/announcement feeds.

This is enough to make the system feel general without spending four weeks writing connectors.

Later:

```text
Azure Service Health
APIs
Reddit
YouTube
price APIs
email
Google Drive
etc.
```

can become extensions.

\---

## 13\. Notification example

The notification should be much more sophisticated than:

> Something changed.

For example:

### 🚨 Important change detected

**Watch:** .NET Runtime

**What changed**

A new release was published containing a security-related fix.

**Why this matters**

Your watch is configured to notify you about security fixes. The release notes identify an authentication-related vulnerability.

**Confidence**

High

**Evidence**

* GitHub release
* Microsoft security advisory

**Previous observation**

.NET 10.0.1

**Current observation**

.NET 10.0.2

This demonstrates **grounded AI output** rather than arbitrary LLM prose.

\---

## 14\. Evaluation is a major opportunity

Create a test set such as:

```text
100 historical changes

30 meaningful
70 irrelevant
```

Then measure:

```text
True positives
False positives
False negatives
Precision
Recall
Notification rate
```

For example:

> Without semantic filtering, 43 notifications were generated.
>
> With AI relevance filtering, 9 notifications were generated.
>
> 8 of those 9 were judged relevant.

Now the project isn't just:

> "I built an AI agent."

She can explain:

> "I evaluated the relevance classifier against a labeled corpus and optimized the system to reduce false-positive notifications."

That's a much stronger engineering story.

\---

# 15\. 4–6 week development plan

Target **5 weeks**, with a sixth week available for polish.

## Week 1 — Foundation + basic monitoring

### Goal

Get an end-to-end non-AI system working.

Build:

* ASP.NET Core API
* basic web UI
* Watch CRUD
* Cosmos DB
* one source connector
* scheduler
* observation storage

Example:

```text
Create watch
     ↓
Scheduler
     ↓
Fetch webpage
     ↓
Detect changed content
     ↓
Store observation
```

### AI

Very little AI initially.

Maybe only:

> Convert natural-language watch request → structured watch configuration.

### Deliverable

A working system that can say:

> "This webpage changed since the last check."

\---

## Week 2 — Semantic change detection

### Goal

Make the system actually intelligent.

Implement:

* content normalization
* document snapshots
* basic textual diff
* semantic comparison
* LLM classification
* structured AI output

Example:

```json
{
  "changed": true,
  "meaningful": true,
  "category": "breaking\_change",
  "confidence": 0.91,
  "summary": "...",
  "evidence": \["..."]
}
```

### Deliverable

The system can distinguish:

```text
page changed
```

from:

```text
something important changed
```

\---

## Week 3 — Agent + MCP

### Goal

Turn the system into an actual agent.

Introduce MCP tools.

The agent can:

```text
fetch source
search for related information
retrieve previous observations
inspect GitHub
compare documents
```

Implement an investigation flow:

```text
Change detected
       ↓
Agent decides:
"Do I need more information?"
       ↓
Tool call
       ↓
Analyze result
       ↓
Another tool call if necessary
       ↓
Final assessment
```

Add GitHub as the second source type.

### Deliverable

A genuine multi-step AI agent using tools/MCP.

This is probably the **most important week** for the AI portion of the project.

\---

## Week 4 — RAG + production workflow

### Goal

Make the agent remember context.

Add:

* embeddings
* vector search
* Azure AI Search
* historical observations
* RAG
* user interests/preferences
* notification history

Now the agent can answer:

> "Is this change similar to something I've seen before?"

and:

> "Does this matter based on what this user asked me to watch?"

Also add:

* retries
* timeout handling
* idempotency
* execution state
* dead-letter/error handling

### Deliverable

A persistent, stateful monitoring agent.

\---

## Week 5 — Evaluation + productionization

This week should **not** be skipped.

### Evaluation

Create a dataset of known changes and expected classifications.

Measure:

* precision
* recall
* false positives
* false negatives
* latency
* token usage

### Observability

Track:

```text
Watch execution
Tool calls
LLM calls
Tokens
Latency
Failures
Notifications
```

Use Azure Application Insights / OpenTelemetry.

### Security

Add:

* Entra authentication
* Managed Identity
* Key Vault
* secret management
* authorization

### Deployment

Build:

```text
GitHub
   ↓
CI/CD
   ↓
Azure
```

with Bicep.

### Deliverable

A publicly deployable application.

\---

## Week 6 — Polish / optional stretch

If there is a sixth week, **don't add five new features**.

Make the existing product excellent.

### UI polish

Dashboard:

```text
My Watches

┌─────────────────────────────────────┐
│ .NET Releases                 🟢    │
│ Last checked: 12 min ago            │
│ No important changes                │
└─────────────────────────────────────┘

┌─────────────────────────────────────┐
│ Cosmos DB Vector Search        🟡   │
│ Important change detected           │
│ 2 hours ago                         │
└─────────────────────────────────────┘
```

### Execution history

Show:

```text
12:01 Fetch
12:02 Change detected
12:02 AI analysis
12:03 GitHub investigation
12:03 Relevance assessment
12:03 Notification
```

### Demo scenarios

Prepare 3 excellent demos rather than 20 mediocre ones.

\---

# 16\. Suggested final demo

## Demo 1 — Simple monitoring

> "Watch this GitHub repository for new releases."

System creates the watch.

A release occurs.

Agent detects it and reports the change.

\---

## Demo 2 — Intelligent filtering

Create:

> "Watch .NET releases, but only notify me about security issues or breaking changes."

Generate several historical releases.

Some are irrelevant.

The agent ignores them.

A relevant one triggers an investigation and notification.

This demonstrates **semantic filtering**.

\---

## Demo 3 — Agentic investigation

A meaningful change occurs.

The agent autonomously:

```text
Detects change
       ↓
Searches GitHub
       ↓
Reads release information
       ↓
Retrieves previous observations
       ↓
Determines relevance
       ↓
Generates grounded report
       ↓
Notifies user
```

This is the demo an interviewer should see.

\---

# 17\. What she can say she learned

At the end of the project, the interview story can legitimately cover:

### Software engineering

* ASP.NET Core
* C#
* REST APIs
* distributed systems
* asynchronous processing
* stateful workflows
* retries/idempotency
* cloud deployment
* observability
* infrastructure as code

### AI engineering

* LLM integration
* structured outputs
* function/tool calling
* MCP
* RAG
* embeddings
* vector search
* semantic similarity
* agentic workflows
* prompt engineering
* AI evaluation
* hallucination/grounding mitigation
* human-in-the-loop workflows

### Azure

* Azure OpenAI / Microsoft Foundry
* Azure AI Search
* Cosmos DB
* Container Apps / Functions
* Entra ID
* Managed Identity
* Key Vault
* Application Insights
* Bicep

This is a credible modern **backend + AI engineering** project.

\---

# 18\. Design principle

The project should follow this rule:

> \*\*Use conventional software engineering whenever conventional software is sufficient; use AI where understanding ambiguity or meaning is actually difficult.\*\*

For example:

**Don't use an LLM for:**

```text
Has the URL changed?
```

Use hashes/diffs.

**Do use an LLM for:**

```text
Does this change materially affect what the user asked me to monitor?
```

Similarly:

**Don't use RAG because "AI projects need RAG."**

Use it because:

> "The agent needs historical observations and user-specific context to assess a new event."

That design philosophy makes the project feel more like **good engineering with AI** rather than an AI technology showcase.

\---

# 19\. Recommended v1 scope

> \*\*A persistent AI agent that monitors websites, GitHub repositories, and RSS feeds, detects semantically meaningful changes, investigates important changes using MCP tools, uses historical context through RAG, and sends grounded notifications to the user.\*\*

This is a substantial but achievable **4–6 week project**, particularly given an existing C#/backend background.

