using System.ComponentModel;
using SmartComponents.LocalEmbeddings;

namespace _10_medical_guidelines_rag.Infrastructure;

/// <summary>
/// Full-document semantic retrieval engine for clinical guidelines.
///
/// Unlike <see cref="SemanticMedicalSearch"/> — which maps individual terms to acronyms
/// via a single embedding per entry — this service chunks long guideline documents into
/// overlapping word-windows and retrieves the most relevant passages for a clinical query.
///
/// Key concepts demonstrated (new in Project 10):
///   1. <b>Document Chunking</b>: long text → overlapping word-windows → List&lt;string&gt;.
///   2. <b>Chunk Embedding</b>: every chunk gets its own <see cref="EmbeddingF32"/> vector.
///   3. <b>Top-K Retrieval</b>: query embedded → cosine scored → top-K chunks returned.
///   4. <b>Agentic RAG</b>: the ClinicalAdvisor agent calls this tool and grounds its
///      recommendations in the retrieved passages.
///
/// Threshold Protocol:
///   Score >= 0.60 → Include in context returned to the agent.
///   Score  &lt; 0.60 → Discard (considered irrelevant noise).
/// </summary>
public class ClinicalGuidelinesSearch : IDisposable
{
    // ── Constants ────────────────────────────────────────────────────────────
    private const float RelevanceThreshold = 0.60f;
    private const int   ChunkSizeWords     = 80;
    private const int   ChunkOverlapWords  = 20;
    private const int   MaxResultChunks    = 3;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly LocalEmbedder    _embedder;
    private List<string>       _chunks           = [];
    private List<EmbeddingF32> _chunkEmbeddings  = [];
    private bool               _initialized;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="ClinicalGuidelinesSearch"/> backed by the given shared embedder.
    /// The embedder must be created once as a singleton and injected here.
    /// </summary>
    public ClinicalGuidelinesSearch(LocalEmbedder embedder)
    {
        _embedder = embedder;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the clinical guidelines document, splits it into overlapping chunks,
    /// and builds an in-memory vector index.
    ///
    /// Chunking strategy:
    ///   - Markdown headers and blank lines are stripped.
    ///   - Text is split into words and grouped into windows of <see cref="ChunkSizeWords"/>
    ///     with a <see cref="ChunkOverlapWords"/>-word overlap so that sentence boundaries
    ///     are less likely to cut off important clinical context.
    /// </summary>
    public void Initialize(string guidelinesFilePath)
    {
        if (_initialized)
            return;

        if (!File.Exists(guidelinesFilePath))
            throw new FileNotFoundException(
                $"Clinical guidelines file not found at: {guidelinesFilePath}. " +
                "Ensure the file exists in the MedicalDocuments directory.",
                guidelinesFilePath);

        var text = File.ReadAllText(guidelinesFilePath);
        _chunks = SplitIntoChunks(text, ChunkSizeWords, ChunkOverlapWords);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[ClinicalGuidelinesSearch] Indexing {_chunks.Count} document chunks...");
        Console.ResetColor();

        foreach (var chunk in _chunks)
        {
            _chunkEmbeddings.Add(_embedder.Embed(chunk));
        }

        _initialized = true;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[ClinicalGuidelinesSearch] Index ready — {_chunks.Count} chunks.\n");
        Console.ResetColor();
    }

    // ── AI Tool ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the clinical guidelines knowledge base and returns the most relevant passages.
    ///
    /// Returns up to <see cref="MaxResultChunks"/> passages whose cosine similarity
    /// to the query exceeds <see cref="RelevanceThreshold"/>, ranked by relevance.
    /// If no passage meets the threshold, returns a "no guidelines found" notice so
    /// the agent can fall back to standard clinical judgment.
    /// </summary>
    [Description(
        "Searches the clinical guidelines knowledge base for evidence-based treatment recommendations. " +
        "Call this tool with the patient's current diagnosis or clinical question to retrieve relevant " +
        "guideline passages that should ground your recommendations. " +
        "RESULT HANDLING: " +
        "- [CLINICAL GUIDELINES] → cite the retrieved passages to support your recommendations. " +
        "- [NO RELEVANT GUIDELINES] → proceed with standard clinical judgment; do NOT invent guidelines.")]
    public string SearchClinicalGuidelines(
        [Description("The clinical query — a diagnosis, symptom, or treatment question " +
                     "(e.g., 'hypertension management', 'antibiotic therapy for pneumonia', " +
                     "'heart failure treatment protocol')")]
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "[NO RELEVANT GUIDELINES] — Empty query provided.";

        if (!_initialized || _chunks.Count == 0)
            return "[NO RELEVANT GUIDELINES] — Guidelines index not initialized. Contact system administrator.";

        try
        {
            var queryEmbedding = _embedder.Embed(query.Trim());

            var scored = _chunkEmbeddings
                .Select((emb, i) => (Index: i, Score: queryEmbedding.Similarity(emb)))
                .Where(x => x.Score >= RelevanceThreshold)
                .OrderByDescending(x => x.Score)
                .Take(MaxResultChunks)
                .ToList();

            // ── Debug logging ─────────────────────────────────────────────────
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n  [GuidelinesSearch] Query: \"{query}\" → {scored.Count} relevant chunk(s) found");
            Console.ResetColor();

            if (scored.Count == 0)
                return $"[NO RELEVANT GUIDELINES] — No guidelines found for query: '{query}'. Proceed with standard clinical judgment.";

            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"[CLINICAL GUIDELINES — {scored.Count} relevant passage(s)]");
            builder.AppendLine();

            for (int i = 0; i < scored.Count; i++)
            {
                var (idx, score) = scored[i];
                builder.AppendLine($"--- Passage {i + 1} (relevance: {(int)(score * 100)}%) ---");
                builder.AppendLine(_chunks[idx].Trim());
                builder.AppendLine();
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            return $"[NO RELEVANT GUIDELINES] — Search error: {ex.Message}";
        }
    }

    // ── Public Utilities ──────────────────────────────────────────────────────

    /// <summary>Returns the default path for the clinical guidelines document.</summary>
    public static string GetDefaultGuidelinesPath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MedicalDocuments", "clinical-guidelines.md");

    /// <summary>Returns the total number of indexed document chunks.</summary>
    public int IndexedChunkCount => _chunks.Count;

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Splits plain text into overlapping word-window chunks.
    ///
    /// Algorithm:
    ///   1. Strip markdown headers (lines starting with '#') and blank lines.
    ///   2. Join remaining lines into a single token stream.
    ///   3. Slide a window of <paramref name="chunkSizeWords"/> words,
    ///      advancing by (<paramref name="chunkSizeWords"/> - <paramref name="overlapWords"/>)
    ///      at each step so adjacent chunks share context words.
    ///   4. Discard trailing windows shorter than 10 words.
    /// </summary>
    private static List<string> SplitIntoChunks(string text, int chunkSizeWords, int overlapWords)
    {
        var words = text
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l))
            .SelectMany(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        var chunks = new List<string>();
        int step = Math.Max(1, chunkSizeWords - overlapWords);

        for (int i = 0; i < words.Length; i += step)
        {
            var chunkWords = words.Skip(i).Take(chunkSizeWords).ToArray();
            if (chunkWords.Length < 10)
                break; // Skip very short trailing fragments

            chunks.Add(string.Join(' ', chunkWords));
        }

        return chunks;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        // LocalEmbedder is owned by the caller (Program.cs) — do NOT dispose it here.
        GC.SuppressFinalize(this);
    }
}
