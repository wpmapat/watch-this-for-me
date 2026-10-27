using WatchThisForMe.Core.Observations;

namespace WatchThisForMe.Infrastructure.Embeddings;

public static class EmbeddingSimilarity
{
    public static IReadOnlyList<SimilarObservation> FindTopMatches(
        float[] query, IEnumerable<Observation> candidates, int topN)
    {
        return candidates
            .Where(o => o.Embedding is not null && o.ClassificationSummary is not null)
            .Select(o => new SimilarObservation(
                CheckedAt: o.CheckedAt,
                Summary: o.ClassificationSummary!,
                Meaningful: o.Meaningful ?? false,
                Category: o.Category ?? "unknown",
                SimilarityScore: CosineSimilarity(query, o.Embedding!)))
            .OrderByDescending(s => s.SimilarityScore)
            .Take(topN)
            .ToList();
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magnitudeA = 0, magnitudeB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
