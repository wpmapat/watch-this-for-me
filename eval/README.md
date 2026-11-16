# Evaluation dataset

`dataset.json` is the ground-truth answer key for measuring how good
`IChangeClassifier` actually is — built Mon Nov 2 (Week 5, "don't skip this
week"). Tomorrow's evaluation run feeds each entry's `diffSummary` and
`meaningfulChangeCriteria` into the real classifier and compares its answer
against `expectedMeaningful`/`expectedCategory` here.

## Why authored, not pulled from our own Cosmos history

We checked first: only 8 real diffs exist in Cosmos (the rest were pruned by
the existing 10-per-watch cap), and all 8 turned out to be the same category
(`new_release`) with the same label (`meaningful: true`) — no variety, and
not enough to measure anything. Worse: inspecting the actual diff text showed
every one of them is 100% GitHub navigation-menu boilerplate getting
reshuffled — the diff contains zero lines that actually show what changed
about the release. A human reading just the diff text couldn't honestly
label these; the classifier's past verdicts on them were likely inferred
from background knowledge about .NET versioning, not from the diff it was
given. Using them as ground truth would be circular — testing the classifier
against its own past guesses proves nothing. (Separately worth revisiting:
our web-page diffing doesn't handle GitHub's release pages cleanly.)

So every entry in this dataset is authored: a realistic `diffSummary` in the
same compact `+`/`-` line format `TextDiffer` actually produces, paired with
a plausible `meaningfulChangeCriteria` for the kind of watch it'd belong to.
Each one was labeled by reading the diff text alone — the same honest
constraint a human labeler should hold to — not by asking "what would the
AI probably say."

## Schema

Each entry:

| Field | Meaning |
|---|---|
| `id` | Stable identifier, e.g. `gh-release-03` |
| `domain` | Category of source this example simulates (`github-releases`, `pricing-page`, `security-advisory`, `api-docs`, `job-listings`, `company-blog`, `status-page`, `general`) |
| `watchContext` | Short description of the fictional watch this belongs to |
| `meaningfulChangeCriteria` | The exact string that would be fed to the classifier as the watch's own criteria |
| `diffSummary` | The exact diff text that would be fed to the classifier |
| `expectedMeaningful` | Ground truth: `true`/`false` |
| `expectedCategory` | Ground truth category label |
| `rationale` | One sentence explaining the label |

## Coverage (97 entries)

- **55 meaningful / 42 not** — deliberately mixed, not skewed to one answer
- **8 domains**: GitHub release notes (24), pricing pages (12), security
  advisories (10), API docs/changelogs (16), job listings (10), company
  blog (10), status pages (9), general/unrelated (6)
- **10 categories**: `new_release`, `breaking_change`, `security_fix`,
  `pricing_change`, `new_feature`, `new_posting`, `new_announcement`,
  `new_incident`, `minor_wording`, `unrelated`
- Each domain includes both genuinely meaningful examples *and* deliberately
  similar-looking-but-not examples (e.g. a release-notes typo fix sitting
  right next to a real breaking change), so the evaluation actually tests
  the classifier's judgment at the boundary, not just the obvious cases

## Review

This was drafted by Claude and needs your sign-off before it's treated as
ground truth — if you disagree with any label, that's exactly the point of
review: fix it here before tomorrow's evaluation run uses it to score the
classifier.
