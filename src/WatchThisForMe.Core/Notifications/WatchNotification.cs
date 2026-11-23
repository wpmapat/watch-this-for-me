using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Core.Notifications;

// A notice to the user that a watched page changed in a way they care about. Its Id is the Id of
// the change (Observation) it's about, so one change can never produce two notices - a retry
// that finds this Id already saved knows the notice was made.
public class WatchNotification
{
    public required string Id { get; set; }

    public required string WatchId { get; set; }

    public required string WatchName { get; set; }

    public required string Source { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    // When the change was found (may be earlier than CreatedAt if making the notice was retried).
    public required DateTimeOffset ChangeDetectedAt { get; set; }

    public string? Category { get; set; }

    public required string WhatChanged { get; set; }

    public required string WhyItMatters { get; set; }

    public double? Confidence { get; set; }

    // Facts gathered while investigating (the diff, pages and releases the agent looked at).
    public List<EvidenceItem> Evidence { get; set; } = [];

    // True when the investigating agent checked the change; false for "Always" notices about
    // trivial changes, which are summarised without investigating.
    public bool Investigated { get; set; }

    // True when the change contains text that looks written to steer an AI ("classify this as
    // critical..."). The notice is still shown, with a warning to check the page yourself.
    public bool SuspiciousText { get; set; }
}
