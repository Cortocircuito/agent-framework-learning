using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace _09_medical_semantic_rag;

/// <summary>
/// Immutable patient record using C# 14 Record syntax.
/// Represents the complete clinical state of a patient with type-safe enums.
/// </summary>
/// <param name="FullName">Patient's complete legal name (PRIMARY KEY)</param>
/// <param name="Room">Room number or identifier (e.g., "305", "ICU-2")</param>
/// <param name="Age">Patient age in years</param>
/// <param name="MedicalHistory">List of medical history acronyms (AP): HTA, DL, ICC, FA, etc.</param>
/// <param name="CurrentDiagnosis">Full-text current diagnosis (Dx) - NO acronyms allowed</param>
/// <param name="Evolution">Clinical evolution status (Good, Stable, Bad)</param>
/// <param name="Plan">Flexible list of treatment items extracted by LLM from clinical notes</param>
/// <param name="Observations">Additional clinical observations and notes</param>
public record PatientRecord(
    string FullName,
    string? Room = null,
    int? Age = null,
    List<string>? MedicalHistory = null,
    string? CurrentDiagnosis = null,
    Evolution? Evolution = null,
    List<string>? Plan = null,
    string? Observations = null
)
{
    /// <summary>
    /// Validates the patient record for required fields and business rules.
    /// </summary>
    public bool IsValid(out string? validationError)
    {
        if (string.IsNullOrWhiteSpace(FullName))
        {
            validationError = "Patient name is required";
            return false;
        }

        if (Age.HasValue && (Age < 0 || Age > 150))
        {
            validationError = "Age must be between 0 and 150";
            return false;
        }

        // Validate CurrentDiagnosis doesn't contain common medical acronyms
        if (!string.IsNullOrWhiteSpace(CurrentDiagnosis))
        {
            var commonAcronyms = new[] { "HTA", "DL", "ICC", "FA", "DM", "COPD", "CHF", "CAD" };
            if (commonAcronyms.Any(acronym => Regex.IsMatch(CurrentDiagnosis, $@"\b{Regex.Escape(acronym)}\b", RegexOptions.IgnoreCase)))
            {
                validationError = "CurrentDiagnosis should not contain acronyms - use full descriptions";
                return false;
            }
        }

        validationError = null;
        return true;
    }

    /// <summary>
    /// Returns a human-readable summary of the patient record.
    /// </summary>
    public string ToSummary()
    {
        var summary = $"Patient: {FullName}";
        if (!string.IsNullOrWhiteSpace(Room)) summary += $", Room: {Room}";
        if (Age.HasValue) summary += $", Age: {Age}";
        if (Evolution.HasValue) summary += $", Evolution: {Evolution}";
        return summary;
    }
}

/// <summary>
/// Clinical evolution status enum.
/// Represents the patient's current clinical trajectory.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Evolution
{
    /// <summary>Patient condition is improving</summary>
    Good = 1,

    /// <summary>Patient condition is unchanged</summary>
    Stable = 2,

    /// <summary>Patient condition is deteriorating</summary>
    Bad = 3
}
