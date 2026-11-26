using Microsoft.Extensions.Logging;
using WatchThisForMe.Core.Executions;
using WatchThisForMe.Core.Notifications;
using WatchThisForMe.Core.Observations;
using WatchThisForMe.Core.Preferences;
using WatchThisForMe.Core.Watches;
using WatchThisForMe.Infrastructure.Ai;
using WatchThisForMe.Infrastructure.Embeddings;

namespace WatchThisForMe.Infrastructure.Notifications;

// Turns saved changes into notices for the user. Two promises:
// - Never lost: a change that should be notified is marked "pending" when it's saved, and the
//   mark is only cleared once the notice exists (or is ruled out). If making it fails (e.g. the
//   AI is down), every later check retries - even though the page itself won't "change" again.
// - Never twice: the notice's Id is the change's Id, and the database refuses a second one. A
//   retry after a crash finds the existing notice and just clears the mark.
// Callers hold the per-watch check lock, so two runs never work on the same watch at once.
public class NotificationService(
    IObservationRepository observationRepository,
    INotificationRepository notificationRepository,
    IInvestigationAgent investigationAgent,
    IInterestProfileRepository interestProfileRepository,
    ILogger<NotificationService> logger)
{
    private const int ObservationsToScan = 10; // matches how many changes are kept per watch
    public const string SentOutcome = "Sent";

    // Should a just-saved change get a notice? Decided once, when the change is saved.
    public static bool ShouldNotify(Watch watch, Observation observation) =>
        !string.IsNullOrEmpty(observation.DiffSummary)                     // not a first snapshot / no real difference
        && observation.Category != ChangeCategories.PageUnavailable         // an error page is a health problem, not news
        && (watch.NotificationPolicy == NotificationPolicy.Always || observation.Meaningful == true);

    // Makes every notice this watch still owes. Never throws (except on cancellation): a failed
    // notice must not fail the check - it stays pending and is retried on the next check.
    // What happened is added to `steps` (the run's story on the history page), when given.
    public async Task NotifyPendingAsync(Watch watch, List<ExecutionStep>? steps = null, CancellationToken cancellationToken = default)
    {
        var lookupStartedAt = DateTimeOffset.UtcNow;
        IReadOnlyList<Observation> recent;
        try
        {
            recent = await observationRepository.ListAsync(watch.Id, ObservationsToScan, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't look for pending notices of watch {WatchId}; will retry on the next check", watch.Id);
            steps?.Add(ExecutionStep.EndingNow("Notice", lookupStartedAt, StepOutcome.Warning,
                $"Couldn't check for notices still owed ({ex.Message}). Will try again on the next check."));
            return;
        }

        // Oldest first, so notices appear in the order the changes happened.
        foreach (var change in recent.Where(o => o.NotificationPending == true).OrderBy(o => o.CheckedAt))
        {
            var startedAt = DateTimeOffset.UtcNow;
            try
            {
                change.NotificationOutcome = await MakeNoticeAsync(watch, change, recent, steps, cancellationToken);
                change.NotificationPending = false;
                await observationRepository.UpdateAsync(change, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't make the notice for change {ObservationId} of watch {WatchId}; will retry on the next check",
                    change.Id, watch.Id);
                steps?.Add(ExecutionStep.EndingNow("Notice", startedAt, StepOutcome.Warning,
                    $"Couldn't finish the notice ({ex.Message}). The change isn't forgotten - it will be tried again on the next check."));
            }
        }
    }

    // Returns the outcome to record on the change.
    private async Task<string> MakeNoticeAsync(
        Watch watch, Observation change, IReadOnlyList<Observation> recent, List<ExecutionStep>? steps, CancellationToken cancellationToken)
    {
        // Made on an earlier attempt that crashed before clearing the mark: don't pay the AI again.
        if (await notificationRepository.GetAsync(change.Id, watch.Id, cancellationToken) is not null)
        {
            steps?.Add(ExecutionStep.EndingNow("Notice", DateTimeOffset.UtcNow, StepOutcome.Ok, "Already sent on an earlier attempt."));
            return SentOutcome;
        }

        WatchNotification notice;
        if (change.Meaningful == true)
        {
            // Second opinion with evidence: the same agent as the "Investigate" button.
            var similarPast = change.Embedding is { } embedding
                ? EmbeddingSimilarity.FindTopMatches(embedding, recent.Where(o => o.Id != change.Id), topN: 3)
                : [];
            var interests = (await interestProfileRepository.GetAsync(cancellationToken)).Interests;
            var investigationStartedAt = DateTimeOffset.UtcNow;
            var result = await investigationAgent.InvestigateAsync(
                watch.Id, watch.Source, change.DiffSummary!, watch.MeaningfulChangeCriteria, similarPast, interests, cancellationToken);

            if (result.Notification is not { } found)
            {
                // The closer look disagreed with the quick classification - a false alarm avoided.
                steps?.Add(ExecutionStep.EndingNow("Investigation", investigationStartedAt, StepOutcome.Ok,
                    $"On a closer look it didn't seem to matter, so no notice. {result.Classification.Summary}"));
                return $"Not sent: on a closer look it didn't seem to matter. {result.Classification.Summary}";
            }

            steps?.Add(ExecutionStep.EndingNow("Investigation", investigationStartedAt, StepOutcome.Highlight,
                $"Confirmed it matters, with {Pieces(found.Evidence.Count)} of evidence."));

            notice = NewNotice(watch, change, found.WhatChanged, found.WhyItMatters, found.Confidence, [.. found.Evidence], investigated: true);
        }
        else
        {
            // "Always" policy, trivial change: summarise it without the cost of investigating.
            notice = NewNotice(watch, change,
                // The classifier's summary is AI-written from page text too, so it gets the same link check.
                PromptInjectionGuard.RemoveOffSiteLinks(change.ClassificationSummary ?? "The page changed.", watch.Source),
                "Your watch is set to tell you about every change, including minor ones.",
                change.Confidence,
                [new EvidenceItem("Page changes", change.DiffSummary!)],
                investigated: false);
        }

        var sendStartedAt = DateTimeOffset.UtcNow;
        await notificationRepository.TryCreateAsync(notice, cancellationToken); // false = already there; fine either way
        steps?.Add(ExecutionStep.EndingNow("Notice", sendStartedAt, StepOutcome.Highlight,
            (notice.Investigated ? "Sent you a notice: " : "Sent you a short notice (this watch reports every change): ") + notice.WhatChanged));
        return SentOutcome;
    }

    private static string Pieces(int n) => n == 1 ? "1 piece" : $"{n} pieces";

    private static WatchNotification NewNotice(
        Watch watch, Observation change, string whatChanged, string whyItMatters, double? confidence, List<EvidenceItem> evidence, bool investigated) => new()
    {
        Id = change.Id,
        WatchId = watch.Id,
        WatchName = watch.Name,
        Source = watch.Source,
        CreatedAt = DateTimeOffset.UtcNow,
        ChangeDetectedAt = change.CheckedAt,
        Category = change.Category,
        WhatChanged = whatChanged,
        WhyItMatters = whyItMatters,
        Confidence = confidence,
        Evidence = evidence,
        Investigated = investigated,
        SuspiciousText = PromptInjectionGuard.LooksLikeInjection(change.DiffSummary ?? "")
    };
}
