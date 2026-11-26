using System.Text.RegularExpressions;
using BookRag.Models;

namespace BookRag.Retrieval;

/// <summary>
/// BM25 (Okapi BM25) keyword search index built entirely in plain C#.
///
/// BM25 ranks documents by term frequency (TF) and inverse document
/// frequency (IDF) with length normalisation.  It complements dense
/// vector search because it excels at exact keyword matches — e.g.
/// "James Potter" or "Hagrid" — where the vector model may produce
/// similar scores for many passages.
///
/// Formula:
///   score(D, Q) = Σ IDF(q) × [ TF(q,D) × (k1+1) / (TF(q,D) + k1 × (1 - b + b × |D|/avgdl)) ]
///
/// Parameters:
///   k1 — term-frequency saturation (default 1.5; typical range 1.2–2.0)
///   b  — length normalisation (default 0.75; 0 = no normalisation)
/// </summary>
public sealed class Bm25Index
{
    // Tokenise on non-word chars; lowercase; drop tokens shorter than 2 chars.
    private static readonly Regex _tokenizer =
        new(@"[^\w]+", RegexOptions.Compiled);

    // Simple English stopwords — removing them reduces noise in IDF.
    private static readonly HashSet<string> _stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","and","or","but","in","on","at","to","for","of","with",
        "is","was","are","were","be","been","has","have","had","it","its",
        "this","that","these","those","by","from","as","up","out","if","then",
        "i","me","my","we","our","you","your","he","she","his","her","they",
        "them","their","what","which","who","when","where","how","all","not"
    };

    private readonly double _k1;
    private readonly double _b;

    // Per-document token frequencies: _termFreq[docIndex][token] = count
    private readonly List<Dictionary<string, int>> _termFreq;

    // Document lengths in tokens
    private readonly int[] _docLengths;

    // Average document length across the corpus
    private readonly double _avgDocLength;

    // IDF for each term that appears in at least one document
    private readonly Dictionary<string, double> _idf;

    // The original chunks in the same order as _termFreq
    private readonly List<Chunk> _chunks;

    public Bm25Index(List<Chunk> chunks, double k1 = 1.5, double b = 0.75)
    {
        _k1    = k1;
        _b     = b;
        _chunks = chunks;

        int n = chunks.Count;
        _termFreq   = new List<Dictionary<string, int>>(n);
        _docLengths = new int[n];

        // ── Pass 1: build per-doc term frequencies ────────────────────────
        for (int i = 0; i < n; i++)
        {
            var tokens = Tokenize(chunks[i].Text);
            var freq   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in tokens)
            {
                freq[t] = freq.TryGetValue(t, out var c) ? c + 1 : 1;
            }

            _termFreq.Add(freq);
            _docLengths[i] = tokens.Count;
        }

        _avgDocLength = n > 0 ? (double)_docLengths.Sum() / n : 1.0;

        // ── Pass 2: compute IDF for each term ─────────────────────────────
        // df[term] = number of documents containing the term
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var freq in _termFreq)
        {
            foreach (var term in freq.Keys)
                df[term] = df.TryGetValue(term, out var c) ? c + 1 : 1;
        }

        _idf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (term, docFreq) in df)
        {
            // Robertson–Spärck Jones IDF with a small floor to avoid negatives.
            _idf[term] = Math.Log((n - docFreq + 0.5) / (docFreq + 0.5) + 1);
        }
    }

    /// <summary>
    /// Scores every chunk against <paramref name="query"/> and returns the
    /// top <paramref name="topK"/> results, ordered by descending score.
    /// Results with score = 0 are omitted.
    /// </summary>
    public List<(Chunk Chunk, double Score)> Search(string query, int topK = 20)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return [];

        var scores = new double[_chunks.Count];

        for (int i = 0; i < _chunks.Count; i++)
        {
            double score = 0;
            int    dl    = _docLengths[i];
            var    freq  = _termFreq[i];

            foreach (var term in queryTokens)
            {
                if (!_idf.TryGetValue(term, out var idf)) continue;
                if (!freq.TryGetValue(term, out var tf))  continue;

                // BM25 term score
                double numerator   = tf * (_k1 + 1);
                double denominator = tf + _k1 * (1 - _b + _b * dl / _avgDocLength);
                score += idf * numerator / denominator;
            }

            scores[i] = score;
        }

        return scores
            .Select((s, i) => (_chunks[i], s))
            .Where(x => x.s > 0)
            .OrderByDescending(x => x.s)
            .Take(topK)
            .Select(x => (x.Item1, x.s))
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> Tokenize(string text)
    {
        return _tokenizer
            .Split(text.ToLowerInvariant())
            .Where(t => t.Length >= 2 && !_stopwords.Contains(t))
            .ToList();
    }
}
