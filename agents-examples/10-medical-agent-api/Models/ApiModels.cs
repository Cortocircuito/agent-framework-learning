namespace _10_medical_agent_api.Models;

/// <summary>
/// Request payload for free-form chat messages.
/// </summary>
/// <param name="Message">The user's message to the medical agent system.</param>
/// <param name="SessionId">Optional session identifier. A new session is created if omitted.</param>
public record ChatRequest(string Message, string? SessionId = null);

/// <summary>
/// Request payload for processing clinical documentation notes.
/// </summary>
/// <param name="Notes">The clinical notes to process and document.</param>
/// <param name="SessionId">Optional session identifier.</param>
public record DocumentRequest(string Notes, string? SessionId = null);

/// <summary>
/// Request payload for querying a patient's medical record.
/// </summary>
/// <param name="PatientName">The full name of the patient to query.</param>
/// <param name="SessionId">Optional session identifier.</param>
public record QueryRequest(string PatientName, string? SessionId = null);

/// <summary>
/// Error response returned when a request fails validation.
/// </summary>
/// <param name="Error">Human-readable description of the error.</param>
public record ErrorResponse(string Error);
