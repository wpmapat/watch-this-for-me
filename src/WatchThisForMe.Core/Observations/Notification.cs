namespace WatchThisForMe.Core.Observations;

public record Notification(
    string WhatChanged,
    string WhyItMatters,
    double Confidence,
    IReadOnlyList<EvidenceItem> Evidence);
