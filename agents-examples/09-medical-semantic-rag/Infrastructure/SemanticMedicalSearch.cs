using System.ComponentModel;
using SmartComponents.LocalEmbeddings;

namespace _09_medical_semantic_rag.Infrastructure;

/// <summary>
/// Vector-based semantic search engine for medical terminology standardization.
/// Replaces the previous keyword-based MedicalKnowledgeBase with cosine similarity search.
///
/// Design:
/// - Receives a shared LocalEmbedder singleton (no per-call model loading overhead).
/// - Builds an in-memory vector index at startup from the pipe-delimited acronyms.txt file.
/// - Applies a strict threshold protocol to avoid diagnostic errors.
///
/// Threshold Protocol:
///   Score >= 0.85 → [CONFIRMED MATCH]   : safe to use the acronym.
///   Score 0.60–0.84 → [UNCERTAIN]       : use doctor's original text.
///   Score  < 0.60  → [NO MATCH]         : use doctor's original text.
/// </summary>
public class SemanticMedicalSearch : IDisposable
{
    // ── Constants ────────────────────────────────────────────────────────────
    private const float ConfirmedThreshold = 0.85f;
    private const float UncertainThreshold = 0.60f;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly LocalEmbedder _embedder;
    private List<MedicalEntry> _entries = [];
    private List<EmbeddingF32> _entryEmbeddings = [];
    private bool _initialized;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a SemanticMedicalSearch engine backed by the given shared embedder.
    /// The embedder should be created once as a singleton and injected here.
    /// </summary>
    public SemanticMedicalSearch(LocalEmbedder embedder)
    {
        _embedder = embedder;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and indexes the pipe-delimited acronyms.txt file.
    /// Must be called once at startup, after the file has been seeded.
    ///
    /// Expected line format:
    ///   Main Term | Acronym | Synonym1, Synonym2, ...
    ///
    /// The embedding vector for each entry is built from a "rich semantic string":
    ///   "{MainTerm} {Synonym1} {Synonym2} ..."
    /// This ensures both canonical names and colloquialisms are captured in the
    /// same vector space.
    /// </summary>
    public void Initialize(string acronymsFilePath)
    {
        if (_initialized)
            return;

        if (!File.Exists(acronymsFilePath))
            throw new FileNotFoundException(
                $"Acronyms file not found at: {acronymsFilePath}. " +
                "Ensure Program.cs seeds the file before calling Initialize().",
                acronymsFilePath);

        _entries = [];
        _entryEmbeddings = [];

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n[SemanticMedicalSearch] Building vector index...");
        Console.ResetColor();

        int lineNumber = 0;
        foreach (var rawLine in File.ReadLines(acronymsFilePath))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split('|');
            if (parts.Length < 2)
            {
                Console.WriteLine($"  [WARN] Line {lineNumber} skipped (invalid format): '{line}'");
                continue;
            }

            var mainTerm = parts[0].Trim();
            var acronym  = parts[1].Trim();
            var synonyms = parts.Length > 2
                ? parts[2].Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray()
                : [];

            if (string.IsNullOrEmpty(mainTerm) || string.IsNullOrEmpty(acronym))
            {
                Console.WriteLine($"  [WARN] Line {lineNumber} skipped (empty term or acronym).");
                continue;
            }

            var entry = new MedicalEntry(mainTerm, acronym, synonyms);

            // Build rich semantic string: main term + all synonyms combined
            var richText = BuildRichSemanticString(entry);
            var embedding = _embedder.Embed(richText);

            _entries.Add(entry);
            _entryEmbeddings.Add(embedding);

            Console.WriteLine($"  ✓ Indexed: [{acronym}] \"{mainTerm}\" ({synonyms.Length} synonyms)");
        }

        _initialized = true;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[SemanticMedicalSearch] Index ready — {_entries.Count} entries.\n");
        Console.ResetColor();
    }

    // ── AI Tool ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the semantic knowledge base for a standardized medical acronym.
    ///
    /// Returns one of three structured responses based on cosine similarity score:
    ///   [CONFIRMED MATCH]: {Acronym} (Source: {MainTerm})   → score >= 0.85
    ///   [UNCERTAIN]: {MainTerm} (Confidence: XX%)            → score 0.60–0.84
    ///   [NO MATCH]                                           → score &lt; 0.60
    ///
    /// Similarity scores are always logged to the console for PoC debugging.
    /// </summary>
    [Description(
        "Searches the local semantic knowledge base for a standardized medical acronym. " +
        "Call this tool for every medical condition in the patient's Medical History (AP) " +
        "before writing any acronym. " +
        "RESULT HANDLING: " +
        "- [CONFIRMED MATCH] → use the returned acronym. " +
        "- [UNCERTAIN] → use the doctor's original text verbatim. " +
        "- [NO MATCH] → use the doctor's original text verbatim. " +
        "NEVER invent acronyms.")]
    public string SearchMedicalKnowledge(
        [Description("The medical term or phrase to standardize (e.g., 'Hypertension', 'High blood pressure', 'sugar disease')")]
        string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return "[NO MATCH] — Empty query provided.";

        if (!_initialized || _entries.Count == 0)
            return "[NO MATCH] — Semantic index not initialized. Contact system administrator.";

        try
        {
            // Embed the query
            var queryEmbedding = _embedder.Embed(query.Trim());

            // Find the best match by cosine similarity
            float bestScore = -1f;
            int bestIndex = -1;

            for (int i = 0; i < _entryEmbeddings.Count; i++)
            {
                var score = queryEmbedding.Similarity(_entryEmbeddings[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            var best = _entries[bestIndex];
            var confidencePct = (int)(bestScore * 100);

            // ── Debug logging ───────────────────────────────────────────────
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n  [SemanticSearch] Query: \"{query}\"");
            Console.WriteLine($"  [SemanticSearch] Best match: \"{best.MainTerm}\" ({best.Acronym}) → Score: {bestScore:F4} ({confidencePct}%)");
            Console.ResetColor();

            // ── Threshold Protocol ──────────────────────────────────────────
            if (bestScore >= ConfirmedThreshold)
            {
                return $"[CONFIRMED MATCH]: {best.Acronym} (Source: {best.MainTerm})";
            }
            else if (bestScore >= UncertainThreshold)
            {
                return $"[UNCERTAIN]: {best.MainTerm} (Confidence: {confidencePct}%) — Use doctor's original text verbatim.";
            }
            else
            {
                return $"[NO MATCH] — Use doctor's original text verbatim.";
            }
        }
        catch (Exception ex)
        {
            return $"[NO MATCH] — Semantic search error: {ex.Message}";
        }
    }

    // ── Public Utilities ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of indexed medical entries.
    /// Useful for health checks and tests.
    /// </summary>
    public int IndexedEntryCount => _entries.Count;

    /// <summary>
    /// Returns the path hint used during initialization (for seeding logic in Program.cs).
    /// </summary>
    public static string GetDefaultAcronymsPath() =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MedicalDocuments", "acronyms.txt");

    // ── Private Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a rich semantic string from a medical entry by concatenating the main term
    /// and all synonyms. This single vector captures the full semantic space of the entry.
    /// </summary>
    private static string BuildRichSemanticString(MedicalEntry entry)
    {
        var parts = new List<string> { entry.MainTerm };
        parts.AddRange(entry.Synonyms);
        return string.Join(' ', parts);
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        // LocalEmbedder is injected and owned by the caller (Program.cs),
        // so we do NOT dispose it here.
        GC.SuppressFinalize(this);
    }
}

// ── Domain Model ──────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single medical terminology entry from the knowledge base.
/// </summary>
/// <param name="MainTerm">The canonical medical term (e.g., "Hypertension")</param>
/// <param name="Acronym">The standard hospital acronym (e.g., "HTA")</param>
/// <param name="Synonyms">Optional synonyms and colloquialisms (e.g., "High Blood Pressure")</param>
internal record MedicalEntry(string MainTerm, string Acronym, string[] Synonyms);
