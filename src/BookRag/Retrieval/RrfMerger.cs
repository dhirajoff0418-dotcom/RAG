using BookRag.Models;

namespace BookRag.Retrieval;

/// <summary>
/// Reciprocal Rank Fusion (RRF) merges multiple ranked lists into a single
/// combined ranking without requiring score normalisation.
///
/// Formula per document:
///   RRF(d) = Σ  1 / (k + rank_i(d))
///
/// where rank_i(d) is the 1-based position of document d in list i,
/// and k is a constant that reduces the impact of top-ranked outliers
/// (default 60, as in the original Cormack et al. 2009 paper).
///
/// Documents that do not appear in a list simply contribute 0 from that list.
/// </summary>
public static class RrfMerger
{
    /// <summary>
    /// Merges any number of ranked chunk lists and returns the top
    /// <paramref name="topK"/> unique chunks by descending RRF score.
    ///
    /// <paramref name="rankedLists"/> is a sequence of (chunk, originalScore)
    /// lists, each already ordered from best to worst.
    /// </summary>
    public static List<(Chunk Chunk, double RrfScore)> Merge(
        IEnumerable<IEnumerable<(Chunk Chunk, double Score)>> rankedLists,
        int    topK = 20,
        double k    = 60)
    {
        // Accumulate RRF scores keyed on chunk Id so duplicates are merged.
        var rrfScores = new Dictionary<int, (Chunk Chunk, double Score)>();

        foreach (var list in rankedLists)
        {
            int rank = 1;
            foreach (var (chunk, _) in list)
            {
                double contribution = 1.0 / (k + rank);

                if (rrfScores.TryGetValue(chunk.Id, out var existing))
                    rrfScores[chunk.Id] = (existing.Chunk, existing.Score + contribution);
                else
                    rrfScores[chunk.Id] = (chunk, contribution);

                rank++;
            }
        }

        return rrfScores.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => (x.Chunk, x.Score))
            .ToList();
    }
}
