using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace _06_multi_agent_with_memory;

/// <summary>
/// Round-robin collaborative orchestrator for multiple AI agents.
/// Agents share conversation history and take turns responding until task completion.
/// </summary>
public class AgentGroupChat
{
    private readonly AIAgent[] _agents;
    private readonly int _maxTurns;
    private AgentThread? _thread;

    /// <summary>
    /// Creates a new agent group chat orchestrator.
    /// </summary>
    /// <param name="maxTurns">Maximum number of turns before forcing termination (default: 10)</param>
    /// <param name="agents">The agents participating in the conversation</param>
    public AgentGroupChat(int maxTurns = 10, params AIAgent[] agents)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _maxTurns = maxTurns;

        if (_agents.Length == 0)
            throw new ArgumentException("At least one agent is required", nameof(agents));
    }

    /// <summary>
    /// Convenience constructor with default max turns.
    /// </summary>
    public AgentGroupChat(params AIAgent[] agents) : this(10, agents)
    {
    }

    /// <summary>
    /// Runs the collaborative conversation with streaming responses.
    /// </summary>
    /// <param name="input">Initial user input</param>
    /// <returns>Stream of agent messages</returns>
    public async IAsyncEnumerable<AgentMessage> RunAsync(string input)
    {
        // Initialize shared thread on first run
        _thread ??= _agents[0].GetNewThread();

        int turnCount = 0;
        int agentIndex = 0;
        bool shouldTerminate = false;

        // Add initial user message to conversation history
        yield return new AgentMessage("User", input);

        while (!shouldTerminate && turnCount < _maxTurns)
        {
            var currentAgent = _agents[agentIndex];

            // Stream the agent's response
            var responseBuilder = new System.Text.StringBuilder();
            bool hasToolInvocation = false;

            await foreach (var update in currentAgent.RunStreamingAsync(input, _thread))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    responseBuilder.Append(update.Text);

                    // Yield each streaming chunk
                    yield return new AgentMessage(
                        currentAgent.Name ?? "Agent",
                        update.Text,
                        isStreaming: true
                    );
                }

                // Check if agent invoked a tool (indicates task completion)
                if (update.Contents?.Any(c => c is FunctionCallContent) == true)
                {
                    hasToolInvocation = true;
                }
            }

            // Signal end of this agent's turn
            var fullResponse = responseBuilder.ToString();
            if (!string.IsNullOrEmpty(fullResponse))
            {
                yield return new AgentMessage(
                    currentAgent.Name ?? "Agent",
                    fullResponse,
                    isStreaming: false,
                    isComplete: true
                );
            }

            // Check termination conditions
            if (hasToolInvocation)
            {
                // Tool was invoked (e.g., PDF export), task is likely complete
                shouldTerminate = true;
            }
            else if (ContainsTerminationKeyword(fullResponse))
            {
                // Agent explicitly signaled completion
                shouldTerminate = true;
            }

            // Move to next agent (round-robin)
            agentIndex = (agentIndex + 1) % _agents.Length;
            turnCount++;

            // For subsequent turns, use a placeholder since context is in thread history
            // We can't use empty string as it causes "Argument is whitespace" error
            input = "continue";
        }

        if (turnCount >= _maxTurns)
        {
            yield return new AgentMessage(
                "System",
                $"[Conversation terminated: Maximum turns ({_maxTurns}) reached]"
            );
        }
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
            "task is complete",
            "successfully created",
            "file has been",
            "report saved",
            "pdf saved"
        };

        return terminationPhrases.Any(phrase =>
            response.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resets the conversation history while keeping the same agents.
    /// </summary>
    public void Reset()
    {
        _thread = null;
    }

    // New: Method to export the thread history to a JSON string
    public string ExportHistory()
    {
        if (_thread == null) return string.Empty;

        // Serialize() returns a JsonElement which we need to serialize properly to JSON
        var element = _thread.Serialize(null);
        return JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
    }

    // New: Method to load history into a new thread
    public void LoadHistory(string jsonHistory, AIAgent agent)
    {
        if (string.IsNullOrWhiteSpace(jsonHistory)) return;

        try 
        {
            var element = JsonSerializer.Deserialize<JsonElement>(jsonHistory);
            _thread = agent.DeserializeThread(element, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load history: {ex.Message}");
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