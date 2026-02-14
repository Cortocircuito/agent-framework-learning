using System.Text.Json;
using Microsoft.Agents.AI;

namespace _07_shared_state_memory;

/// <summary>
/// Coordinator-based orchestrator for multi-agent medical system.
/// Uses a coordinator agent to moderate discussions between specialists.
/// </summary>
public class CoordinatedAgentGroupChat
{
    private readonly AIAgent _coordinator;
    private readonly Dictionary<string, AIAgent> _specialists;
    private readonly int _maxTurns;
    private AgentThread? _thread;

    /// <summary>
    /// Creates a new coordinator-based agent group chat.
    /// </summary>
    /// <param name="coordinator">The coordinator agent that orchestrates the conversation</param>
    /// <param name="specialists">Dictionary of specialist agents (key = agent name for routing)</param>
    /// <param name="maxTurns">Maximum turns before forced termination (default: 15)</param>
    public CoordinatedAgentGroupChat(
        AIAgent coordinator,
        Dictionary<string, AIAgent> specialists,
        int maxTurns = 15)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _specialists = specialists ?? throw new ArgumentNullException(nameof(specialists));
        _maxTurns = maxTurns;

        if (_specialists.Count == 0)
            throw new ArgumentException("At least one specialist agent is required", nameof(specialists));
    }

    /// <summary>
    /// Direct patient data query — bypasses the coordinator and routes only to MedicalSecretary.
    /// Use this for simple lookups that don't require medical analysis.
    /// </summary>
    /// <param name="patientName">Name of the patient to look up</param>
    /// <returns>Stream of agent messages from MedicalSecretary</returns>
    public async IAsyncEnumerable<AgentMessage> RunQueryAsync(string patientName)
    {
        _thread ??= _coordinator.GetNewThread();

        if (!_specialists.TryGetValue("MedicalSecretary", out var secretary))
        {
            yield return new AgentMessage("System", "[Error: MedicalSecretary not available]");
            yield break;
        }

        var query = $"QUERY: Retrieve and display the complete medical record for patient '{patientName}'. " +
                    $"Call GetPatientData and present the information in a clear, readable format. " +
                    $"Do NOT call UpsertPatientRecord or SaveReportToPdf.";

        yield return new AgentMessage("User", query);

        await foreach (var message in ExecuteSpecialistTurn(secretary, query))
        {
            yield return message;
        }
    }

    /// <summary>
    /// Runs the coordinated conversation with specialist agents.
    /// </summary>
    /// <param name="input">Initial user input</param>
    /// <returns>Stream of agent messages</returns>
    public async IAsyncEnumerable<AgentMessage> RunAsync(string input)
    {
        // Initialize shared thread on first run
        _thread ??= _coordinator.GetNewThread();

        int turnCount = 0;
        bool shouldTerminate = false;
        string currentContext = input;

        // Add initial user message to conversation history
        yield return new AgentMessage("User", input);

        // Phase 1: Coordinator analyzes request and creates execution plan
        yield return new AgentMessage("System", "[Coordinator analyzing request...]", isStreaming: false);

        var coordinatorPlan = await GetCoordinatorPlan(input);

        yield return new AgentMessage(
            _coordinator.Name ?? "Coordinator",
            coordinatorPlan,
            isStreaming: false,
            isComplete: true
        );

        // Phase 2: Execute specialist workflow based on coordinator's plan
        var requiredSpecialists = ParseRequiredSpecialists(coordinatorPlan);
        string? previousSpecialist = null;

        foreach (var specialistName in requiredSpecialists)
        {
            if (!_specialists.TryGetValue(specialistName, out var specialist))
            {
                yield return new AgentMessage(
                    "System",
                    $"[Warning: Specialist '{specialistName}' not found, skipping]"
                );
                continue;
            }

            // When MedicalSecretary follows ClinicalDataExtractor, inject an explicit task directive
            // so the LLM knows it must call the database and PDF tools
            string contextForSpecialist = currentContext;
            if (specialistName == "MedicalSecretary" &&
                previousSpecialist == "ClinicalDataExtractor")
            {
                contextForSpecialist =
                    $"ClinicalDataExtractor has completed the medical analysis. " +
                    $"You MUST now execute these steps in order:\n" +
                    $"1. Call GetPatientData with the patient's name\n" +
                    $"2. Call UpsertPatientRecord to save the new findings\n" +
                    $"3. Call SaveReportToPdf to generate the PDF report\n\n" +
                    $"ClinicalDataExtractor's analysis:\n{currentContext}";
            }

            // Execute specialist task
            await foreach (var message in ExecuteSpecialistTurn(specialist, contextForSpecialist))
            {
                yield return message;

                // Update context with specialist's response
                if (message.isComplete && !string.IsNullOrEmpty(message.Text))
                {
                    currentContext = message.Text;
                }

                // Check for task completion signal (reuse shared termination logic)
                if (message.isComplete && ContainsTerminationKeyword(message.Text))
                {
                    shouldTerminate = true;
                }
            }

            previousSpecialist = specialistName;
            turnCount++;

            if (shouldTerminate || turnCount >= _maxTurns)
                break;
        }

        // Phase 3: Coordinator synthesis (optional final summary)
        if (!shouldTerminate && turnCount < _maxTurns)
        {
            yield return new AgentMessage(
                "System",
                "[Coordinator synthesizing findings...]",
                isStreaming: false
            );

            var summary = await GetCoordinatorSummary();
            yield return new AgentMessage(
                _coordinator.Name ?? "Coordinator",
                summary,
                isStreaming: false,
                isComplete: true
            );
        }

        if (turnCount >= _maxTurns)
        {
            yield return new AgentMessage(
                "System",
                $"[Discussion terminated: Maximum turns ({_maxTurns}) reached]"
            );
        }
    }

    /// <summary>
    /// Gets the coordinator's execution plan for the request.
    /// </summary>
    private async Task<string> GetCoordinatorPlan(string input)
    {
        var planBuilder = new System.Text.StringBuilder();

        await foreach (var update in _coordinator.RunStreamingAsync(input, _thread))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                planBuilder.Append(update.Text);
            }
        }

        return planBuilder.ToString();
    }

    /// <summary>
    /// Executes a single specialist agent turn.
    /// </summary>
    private async IAsyncEnumerable<AgentMessage> ExecuteSpecialistTurn(
        AIAgent specialist,
        string context)
    {
        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var update in specialist.RunStreamingAsync(context, _thread))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                responseBuilder.Append(update.Text);

                yield return new AgentMessage(
                    specialist.Name ?? "Specialist",
                    update.Text,
                    isStreaming: true
                );
            }
        }

        var fullResponse = responseBuilder.ToString();
        if (!string.IsNullOrEmpty(fullResponse))
        {
            yield return new AgentMessage(
                specialist.Name ?? "Specialist",
                fullResponse,
                isStreaming: false,
                isComplete: true
            );
        }
    }

    /// <summary>
    /// Gets coordinator's final summary of the discussion.
    /// </summary>
    private async Task<string> GetCoordinatorSummary()
    {
        var summaryBuilder = new System.Text.StringBuilder();

        await foreach (var update in _coordinator.RunStreamingAsync(
            "Provide a brief summary of the findings and actions taken.",
            _thread))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                summaryBuilder.Append(update.Text);
            }
        }

        return summaryBuilder.ToString();
    }

    /// <summary>
    /// Parses specialist names from coordinator's plan.
    /// Matches the full key (e.g., "DraCameron") OR the last camelCase word as a fuzzy alias
    /// (e.g., "Cameron"), so the coordinator doesn't have to reproduce exact camelCase identifiers.
    /// </summary>
    private List<string> ParseRequiredSpecialists(string coordinatorPlan)
    {
        var specialists = new List<string>();

        foreach (var specialist in _specialists.Keys)
        {
            if (SpecialistMentionedInPlan(coordinatorPlan, specialist))
            {
                specialists.Add(specialist);
            }
        }

        // Fallback: if no specialists mentioned, use all in order
        if (specialists.Count == 0)
        {
            specialists.AddRange(_specialists.Keys);
        }

        return specialists;
    }

    /// <summary>
    /// Returns true if the plan references a specialist by its exact key or by the last
    /// significant word in its camelCase name (e.g., "Cameron" for "DraCameron").
    /// Words shorter than 4 characters are skipped to avoid false positives from prefixes like "Dr".
    /// </summary>
    private static bool SpecialistMentionedInPlan(string plan, string specialistKey)
    {
        if (plan.Contains(specialistKey, StringComparison.OrdinalIgnoreCase))
            return true;

        // Derive the last camelCase word as a fuzzy alias (e.g., "DraCameron" → "Cameron")
        var lastWord = System.Text.RegularExpressions.Regex.Matches(specialistKey, @"[A-Z][a-z]+")
            .LastOrDefault()?.Value;

        return lastWord != null
            && lastWord.Length >= 4
            && plan.Contains(lastWord, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if the response contains termination keywords.
    /// </summary>
    private static bool ContainsTerminationKeyword(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var terminationPhrases = new[]
        {
            "task complete",
            "discussion complete",
            "information retrieved",
            "report saved",
            "pdf saved",
            "TASK_COMPLETE" // Explicit signal for MedicalSecretary
        };

        return terminationPhrases.Any(phrase =>
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resets the conversation history.
    /// </summary>
    public void Reset()
    {
        _thread = null;
    }

    /// <summary>
    /// Exports conversation history to JSON.
    /// </summary>
    public string ExportHistory()
    {
        if (_thread == null)
            return string.Empty;

        try
        {
            var element = _thread.Serialize();
            return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting history: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Loads conversation history from JSON, trimming to the last maxMessages to limit context size.
    /// </summary>
    public void LoadHistory(string jsonHistory, AIAgent agent)
    {
        if (string.IsNullOrWhiteSpace(jsonHistory))
            return;

        try
        {
            jsonHistory = TrimHistory(jsonHistory);
            var element = JsonSerializer.Deserialize<JsonElement>(jsonHistory);
            _thread = agent.DeserializeThread(element);
            Console.WriteLine("Successfully loaded conversation history.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load history: {ex.Message}");
        }
    }

    /// <summary>
    /// Trims the serialized thread history to the last <paramref name="maxMessages"/> messages,
    /// always starting at a user message to avoid broken tool-call sequences.
    /// </summary>
    private static string TrimHistory(string jsonHistory, int maxMessages = 50)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonHistory);
            var root = doc.RootElement;

            if (!root.TryGetProperty("storeState", out var storeState) ||
                !storeState.TryGetProperty("messages", out var messagesElement))
                return jsonHistory;

            var messages = messagesElement.EnumerateArray().ToList();

            if (messages.Count <= maxMessages)
                return jsonHistory;

            // Walk back from the trim point until we land on a user message
            var startIndex = messages.Count - maxMessages;
            while (startIndex < messages.Count &&
                   !(messages[startIndex].TryGetProperty("role", out var r) && r.GetString() == "user"))
            {
                startIndex++;
            }

            var trimmed = messages.Skip(startIndex).ToList();

            // Rebuild the JSON, preserving every property except replacing "messages"
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            foreach (var rootProp in root.EnumerateObject())
            {
                if (rootProp.Name != "storeState") { rootProp.WriteTo(writer); continue; }

                writer.WritePropertyName("storeState");
                writer.WriteStartObject();
                foreach (var storeProp in storeState.EnumerateObject())
                {
                    if (storeProp.Name != "messages") { storeProp.WriteTo(writer); continue; }

                    writer.WritePropertyName("messages");
                    writer.WriteStartArray();
                    foreach (var msg in trimmed) msg.WriteTo(writer);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.Flush();

            var trimmedJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            Console.WriteLine($"History trimmed: {messages.Count} → {trimmed.Count} messages loaded.");
            return trimmedJson;
        }
        catch
        {
            return jsonHistory; // If trimming fails, return original unchanged
        }
    }
}

/// <summary>
/// Represents a message from an agent in the group chat.
/// </summary>
/// <param name="AuthorName">Name of the agent or user who sent the message</param>
/// <param name="Text">Message content</param>
/// <param name="isStreaming">True if this is a streaming chunk, false if complete message</param>
/// <param name="isComplete">True if this marks the end of an agent's turn</param>
public record AgentMessage(
    string AuthorName,
    string Text,
    bool isStreaming = false,
    bool isComplete = false
);
