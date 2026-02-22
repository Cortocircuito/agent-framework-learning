using _10_medical_agent_api;
using _10_medical_agent_api.Infrastructure;
using _10_medical_agent_api.Models;
using _10_medical_agent_api.Services;
using SmartComponents.LocalEmbeddings;
using System.Text.Json;

const string LmStudioEndpoint = "http://localhost:1234/v1";
const string ModelId          = "qwen2.5-7b-instruct";

// ── Service Registration ──────────────────────────────────────────────────────
var builder = WebApplication.CreateBuilder(args);

// Singleton embedding model — loads the ONNX model once into memory
builder.Services.AddSingleton<LocalEmbedder>();

// Semantic vector search, backed by the shared embedder
builder.Services.AddSingleton<SemanticMedicalSearch>();

// Domain services (all stateless / thread-safe)
builder.Services.AddSingleton<PatientRegistry>();
builder.Services.AddSingleton<MedicalReportExporter>();

// Agent factory holds one shared IChatClient; creates new agent instances per session
builder.Services.AddSingleton<AgentFactory>(sp => new AgentFactory(
    sp.GetRequiredService<SemanticMedicalSearch>(),
    sp.GetRequiredService<PatientRegistry>(),
    sp.GetRequiredService<MedicalReportExporter>(),
    LmStudioEndpoint,
    ModelId));

// Session manager keeps a ConcurrentDictionary of active conversations
builder.Services.AddSingleton<SessionManager>();

// Built-in ASP.NET Core OpenAPI support (available at /openapi/v1.json)
builder.Services.AddOpenApi();

var app = builder.Build();

// ── Startup Initialization ────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Medical Agent REST API (10-medical-agent-api) ===");
Console.ResetColor();

Console.WriteLine("Loading local embedding model...");
var semanticSearch  = app.Services.GetRequiredService<SemanticMedicalSearch>();
var patientRegistry = app.Services.GetRequiredService<PatientRegistry>();

semanticSearch.Initialize(SemanticMedicalSearch.GetDefaultAcronymsPath());
patientRegistry.Initialize();

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"\nAPI ready. OpenAPI spec at: /openapi/v1.json\n");
Console.ResetColor();

// ── OpenAPI ───────────────────────────────────────────────────────────────────
app.MapOpenApi();

// ══════════════════════════════════════════════════════════════════════════════
// Chat Endpoints — Server-Sent Events (SSE) streaming
// ══════════════════════════════════════════════════════════════════════════════

// POST /api/chat
// Free-form message → coordinator auto-routes to specialists → SSE stream
app.MapPost("/api/chat", async (ChatRequest req, SessionManager sessions, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Message is required"));
        return;
    }

    SetSseHeaders(ctx);

    var chat = sessions.GetOrCreate(req.SessionId, out var sessionId);
    await WriteEventAsync(ctx, new { type = "session", sessionId });

    await foreach (var msg in chat.RunAsync(req.Message).WithCancellation(ctx.RequestAborted))
        await WriteEventAsync(ctx, new { type = "message", author = msg.AuthorName, text = msg.Text, isStreaming = msg.isStreaming, isComplete = msg.isComplete });

    await WriteEventAsync(ctx, new { type = "done" });
})
.WithName("Chat")
.WithSummary("Send a free-form message to the medical agent system.")
.WithDescription(
    "The coordinator analyses the request and automatically routes to the appropriate " +
    "specialists (ClinicalDataExtractor, MedicalSecretary). " +
    "The response is streamed as Server-Sent Events. " +
    "Each SSE 'data' payload is a JSON object with fields: type, author, text, isStreaming, isComplete.");

// POST /api/patients/document
// Process clinical notes through the full Extractor → Secretary pipeline
app.MapPost("/api/patients/document", async (DocumentRequest req, SessionManager sessions, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.Notes))
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse("Notes are required"));
        return;
    }

    SetSseHeaders(ctx);

    var chat = sessions.GetOrCreate(req.SessionId, out var sessionId);
    await WriteEventAsync(ctx, new { type = "session", sessionId });

    var prompt = $"DOCUMENT: Process these clinical notes: {req.Notes}";
    await foreach (var msg in chat.RunAsync(prompt).WithCancellation(ctx.RequestAborted))
        await WriteEventAsync(ctx, new { type = "message", author = msg.AuthorName, text = msg.Text, isStreaming = msg.isStreaming, isComplete = msg.isComplete });

    await WriteEventAsync(ctx, new { type = "done" });
})
.WithName("DocumentPatient")
.WithSummary("Process clinical documentation notes.")
.WithDescription(
    "Triggers the full ClinicalDataExtractor → MedicalSecretary pipeline with " +
    "Semantic RAG standardization. Extracts patient data, standardizes medical terminology " +
    "using vector embeddings, saves to the database, and generates a PDF report.");

// POST /api/patients/query
// Direct patient lookup — bypasses the coordinator for speed
app.MapPost("/api/patients/query", async (QueryRequest req, SessionManager sessions, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.PatientName))
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new ErrorResponse("PatientName is required"));
        return;
    }

    SetSseHeaders(ctx);

    var chat = sessions.GetOrCreate(req.SessionId, out var sessionId);
    await WriteEventAsync(ctx, new { type = "session", sessionId });

    await foreach (var msg in chat.RunQueryAsync(req.PatientName).WithCancellation(ctx.RequestAborted))
        await WriteEventAsync(ctx, new { type = "message", author = msg.AuthorName, text = msg.Text, isStreaming = msg.isStreaming, isComplete = msg.isComplete });

    await WriteEventAsync(ctx, new { type = "done" });
})
.WithName("QueryPatient")
.WithSummary("Query a patient's medical record.")
.WithDescription(
    "Directly retrieves patient data from the database via MedicalSecretary, " +
    "bypassing the coordinator for faster lookups.");

// ══════════════════════════════════════════════════════════════════════════════
// Patient Endpoints — JSON (no streaming needed)
// ══════════════════════════════════════════════════════════════════════════════

// GET /api/patients
app.MapGet("/api/patients", (PatientRegistry registry) =>
{
    try   { return Results.Ok(new { patients = registry.ListAllPatients() }); }
    catch (Exception ex) { return Results.Problem(ex.Message); }
})
.WithName("ListPatients")
.WithSummary("List all patients in the database.");

// GET /api/patients/{name}
app.MapGet("/api/patients/{name}", (string name, PatientRegistry registry) =>
{
    try
    {
        var record = registry.GetPatientRecord(name);
        return record is null
            ? Results.NotFound(new ErrorResponse($"No patient found with name '{name}'"))
            : Results.Ok(record);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
})
.WithName("GetPatient")
.WithSummary("Get a specific patient's complete medical record.");

// ══════════════════════════════════════════════════════════════════════════════
// Session Management Endpoints
// ══════════════════════════════════════════════════════════════════════════════

// GET /api/sessions
app.MapGet("/api/sessions", (SessionManager sessions) =>
    Results.Ok(new { count = sessions.Count, sessionIds = sessions.ActiveSessionIds }))
.WithName("ListSessions")
.WithSummary("List all active session identifiers.");

// DELETE /api/sessions/{sessionId}
app.MapDelete("/api/sessions/{sessionId}", (string sessionId, SessionManager sessions) =>
{
    return sessions.Remove(sessionId)
        ? Results.Ok(new { message = $"Session '{sessionId}' removed." })
        : Results.NotFound(new ErrorResponse($"Session '{sessionId}' not found."));
})
.WithName("DeleteSession")
.WithSummary("Remove a session and its conversation history.");

app.Run();

// ── Local Helpers ─────────────────────────────────────────────────────────────

/// Sets the HTTP headers required for a Server-Sent Events response.
static void SetSseHeaders(HttpContext ctx)
{
    ctx.Response.ContentType          = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";
}

/// Serialises <paramref name="payload"/> and writes it as a single SSE event.
static async Task WriteEventAsync(HttpContext ctx, object payload)
{
    await ctx.Response.WriteAsync(
        $"data: {JsonSerializer.Serialize(payload)}\n\n",
        ctx.RequestAborted);
    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
}
