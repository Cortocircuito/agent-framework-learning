using System.Collections.Concurrent;

namespace _10_medical_agent_api.Services;

/// <summary>
/// Thread-safe registry of active <see cref="CoordinatedAgentGroupChat"/> sessions.
///
/// Each session is identified by a short string key (provided by the client or
/// auto-generated). Sessions are created lazily on first use and persist in memory
/// until explicitly removed via <see cref="Remove"/>.
///
/// Key concept: conversation isolation — every session owns its own agent instances
/// and <see cref="Microsoft.Agents.AI.AgentThread"/>, so multiple concurrent API
/// clients never share history.
/// </summary>
public class SessionManager
{
    private readonly ConcurrentDictionary<string, CoordinatedAgentGroupChat> _sessions = new();
    private readonly AgentFactory _factory;

    public SessionManager(AgentFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Returns the existing session for <paramref name="sessionId"/>, or creates a new one.
    /// If <paramref name="sessionId"/> is null or whitespace a short UUID is generated.
    /// </summary>
    /// <param name="sessionId">Caller-supplied session identifier (may be null).</param>
    /// <param name="actualSessionId">The identifier actually used (echoed back to the client).</param>
    public CoordinatedAgentGroupChat GetOrCreate(string? sessionId, out string actualSessionId)
    {
        actualSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? Guid.NewGuid().ToString("N")[..8]
            : sessionId;

        return _sessions.GetOrAdd(actualSessionId, _ => _factory.CreateGroupChat());
    }

    /// <summary>
    /// Clears the conversation history of a session without deleting the session itself.
    /// Returns <c>false</c> if the session does not exist.
    /// </summary>
    public bool Reset(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var chat))
        {
            chat.Reset();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes a session entirely. Returns <c>false</c> if the session does not exist.
    /// </summary>
    public bool Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    /// <summary>The identifiers of all active sessions.</summary>
    public IEnumerable<string> ActiveSessionIds => _sessions.Keys;

    /// <summary>The number of active sessions.</summary>
    public int Count => _sessions.Count;
}
