using System.ComponentModel;

namespace _08_medical_rag_system.Infrastructure;

/// <summary>
/// RAG-based medical knowledge base service for standardized medical terminology.
/// Uses a local text file (acronyms.txt) for lightweight keyword-based search.
/// Follows SOLID principles with dependency inversion for potential future extensions.
/// </summary>
public class MedicalKnowledgeBase
{
    private readonly string _acronymsFilePath;

    public MedicalKnowledgeBase()
    {
        // Path alignment for 08-medical-rag-system
        _acronymsFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "MedicalDocuments", 
            "acronyms.txt"
        );
        
        // Ensure directory exists for safety
        var dir = Path.GetDirectoryName(_acronymsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <summary>
    /// Searches the local knowledge base for standardized medical acronyms and naming conventions.
    /// Uses streaming file reading for memory efficiency (KISS principle).
    /// </summary>
    /// <param name="query">The medical term to verify (e.g., 'Atrial Fibrillation', 'Hypertension')</param>
    /// <returns>Formatted string with matching acronyms or guidance message</returns>
    [Description("Searches the local knowledge base for standardized medical acronyms and naming conventions. Use this tool BEFORE writing any medical condition acronyms to ensure consistency with the hospital's terminology standards.")]
    public string SearchMedicalKnowledge(
        [Description("The medical term to verify (e.g., 'Atrial Fibrillation', 'Hypertension', 'Diabetes Mellitus')")] 
        string query)
    {
        // Input validation
        if (string.IsNullOrWhiteSpace(query))
            return "Error: No query provided. Please specify a medical term to search.";

        try
        {
            // Check if knowledge base exists
            if (!File.Exists(_acronymsFilePath))
            {
                return $"Knowledge base 'acronyms.txt' not found at {_acronymsFilePath}. " +
                       "Use standard clinical terms without acronyms.";
            }

            // Senior performance: Stream lines instead of loading the whole file
            // This is memory-efficient for large knowledge bases
            var matches = File.ReadLines(_acronymsFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && 
                              line.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(5) // Limit to top 5 matches to avoid overwhelming the LLM
                .ToList();

            if (matches.Any())
            {
                return $"✓ Standardized naming found:\n{string.Join("\n", matches)}";
            }
            else
            {
                return $"No specific acronym found for '{query}' in the knowledge base. " +
                       "Use the full clinical description instead of inventing an acronym.";
            }
        }
        catch (IOException ex)
        {
            return $"RAG Error: Unable to read knowledge base file. {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"RAG Error: Access denied to knowledge base file. {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"RAG Error: Unexpected error during search. {ex.Message}";
        }
    }

    /// <summary>
    /// Gets the path to the acronyms file for external seeding/validation.
    /// </summary>
    public string GetAcronymsFilePath() => _acronymsFilePath;
}
