using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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
    private readonly bool _enableDiscussionMode;

    /// <summary>
    /// Creates a new coordinator-based agent group chat.
    /// </summary>
    /// <param name="coordinator">The coordinator agent that orchestrates the conversation</param>
    /// <param name="specialists">Dictionary of specialist agents (key = agent name for routing)</param>
    /// <param name="maxTurns">Maximum discussion turns (default: 15 for multi-turn discussions)</param>
    /// <param name="enableDiscussionMode">Allow extended multi-turn discussions (default: false)</param>
    public CoordinatedAgentGroupChat(
        AIAgent coordinator,
        Dictionary<string, AIAgent> specialists,
        int maxTurns = 15,
        bool enableDiscussionMode = false)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _specialists = specialists ?? throw new ArgumentNullException(nameof(specialists));
        _maxTurns = maxTurns;
        _enableDiscussionMode = enableDiscussionMode;

        if (_specialists.Count == 0)
            throw new ArgumentException("At least one specialist agent is required", nameof(specialists));
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

            // Execute specialist task
            await foreach (var message in ExecuteSpecialistTurn(specialist, currentContext))
            {
                yield return message;

                // Update context with specialist's response
                if (message.isComplete && !string.IsNullOrEmpty(message.Text))
                {
                    currentContext = message.Text;
                }

                // Check for tool invocations (indicates task completion)
                if (message.Text.Contains("SaveReportToPdf", StringComparison.OrdinalIgnoreCase) ||
                    message.Text.Contains("successfully created", StringComparison.OrdinalIgnoreCase))
                {
                    shouldTerminate = true;
                }
            }

            turnCount++;

            if (shouldTerminate || turnCount >= _maxTurns)
                break;
        }

        // Phase 3: Multi-turn discussion mode (if enabled)
        if (_enableDiscussionMode && !shouldTerminate && requiredSpecialists.Count > 1)
        {
            yield return new AgentMessage(
                "System",
                "[Enabling multi-turn discussion mode...]",
                isStreaming: false
            );

            await foreach (var message in RunDiscussionMode(currentContext, turnCount))
            {
                yield return message;

                // Check termination in discussion messages
                if (message.isComplete && ContainsTerminationKeyword(message.Text))
                {
                    shouldTerminate = true;
                    break;
                }

                if (turnCount >= _maxTurns)
                    break;
            }
        }

        // Phase 4: Coordinator synthesis (optional final summary)
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
    /// Runs multi-turn discussion mode where specialists collaborate.
    /// </summary>
    private async IAsyncEnumerable<AgentMessage> RunDiscussionMode(
        string initialContext,
        int startingTurnCount)
    {
        int discussionTurns = 0;
        int maxDiscussionTurns = _maxTurns - startingTurnCount;
        string currentContext = initialContext;
        bool discussionComplete = false;

        // Round-robin discussion between specialists
        var specialistList = _specialists.Values.ToList();
        int specialistIndex = 0;

        while (!discussionComplete && discussionTurns < maxDiscussionTurns)
        {
            var currentSpecialist = specialistList[specialistIndex];

            // Specialist responds to current context
            var responseBuilder = new System.Text.StringBuilder();

            await foreach (var update in currentSpecialist.RunStreamingAsync(
                $"Based on the discussion so far: {currentContext}\n\nProvide your input:",
                _thread))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    responseBuilder.Append(update.Text);

                    yield return new AgentMessage(
                        currentSpecialist.Name ?? "Specialist",
                        update.Text,
                        isStreaming: true
                    );
                }
            }

            var fullResponse = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(fullResponse))
            {
                yield return new AgentMessage(
                    currentSpecialist.Name ?? "Specialist",
                    fullResponse,
                    isStreaming: false,
                    isComplete: true
                );

                currentContext = fullResponse;
            }

            // Check if discussion should end
            if (ContainsTerminationKeyword(fullResponse) ||
                ContainsUserQuestion(fullResponse))
            {
                discussionComplete = true;
            }

            specialistIndex = (specialistIndex + 1) % specialistList.Count;
            discussionTurns++;
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
    /// Expected format: "I will consult: DrHouse, MedicalSecretary"
    /// </summary>
    private List<string> ParseRequiredSpecialists(string coordinatorPlan)
    {
        var specialists = new List<string>();

        // Simple heuristic: check if specialist names appear in the plan
        foreach (var specialist in _specialists.Keys)
        {
            if (coordinatorPlan.Contains(specialist, StringComparison.OrdinalIgnoreCase))
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
            "analysis complete",
            "information retrieved",
            "successfully created",
            "report saved",
            "pdf saved"
        };

        return terminationPhrases.Any(phrase =>
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if the response contains a question for the user.
    /// </summary>
    private static bool ContainsUserQuestion(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var questionIndicators = new[]
        {
            "please confirm",
            "do you want",
            "would you like",
            "should i"
        };

        return questionIndicators.Any(phrase =>
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
            var element = _thread.Serialize(null);
            return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error exporting history: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Loads conversation history from JSON.
    /// </summary>
    public void LoadHistory(string jsonHistory, AIAgent agent)
    {
        if (string.IsNullOrWhiteSpace(jsonHistory))
            return;

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(jsonHistory);
            _thread = agent.DeserializeThread(element, null);
            Console.WriteLine("Successfully loaded conversation history.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load history: {ex.Message}");
        }
    }
}
