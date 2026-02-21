using System.ClientModel;
using _09_medical_semantic_rag;
using _09_medical_semantic_rag.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using SmartComponents.LocalEmbeddings;

const string LmStudioEndpoint = "http://localhost:1234/v1";
const string ModelId          = "qwen2.5-7b-instruct";
const string HistoryFile      = "chat_history_coordinator.json";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Semantic RAG Multi-Agent Medical System (09-semantic-rag) ===");
Console.ResetColor();

try
{
    // 1. Embedding model — singleton; loads the ONNX model into memory once
    Console.WriteLine("Loading local embedding model...");
    using var embedder = new LocalEmbedder();
    Console.WriteLine("✓ Embedding model loaded (in-process, CPU).");

    // 2. Build vector index from the knowledge base document
    var semanticSearch = new SemanticMedicalSearch(embedder);
    var acronymsPath   = SemanticMedicalSearch.GetDefaultAcronymsPath();
    semanticSearch.Initialize(acronymsPath);

    // 3. LLM client (LM Studio)
    var chatClient = new ChatClientBuilder(
            new OpenAIClient(
                    new ApiKeyCredential("lm-studio"),
                    new OpenAIClientOptions { Endpoint = new Uri(LmStudioEndpoint) })
                .GetChatClient(ModelId)
                .AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    // 4. Domain services
    var exporter        = new MedicalReportExporter();
    var patientRegistry = new PatientRegistry();
    patientRegistry.Initialize();
    Console.WriteLine("✓ Database initialized (hospital.db).");

    // 5. Agents
    AIAgent coordinator = chatClient.CreateAIAgent(
        name: "MedicalCoordinator",
        instructions: AgentInstructions.Coordinator);

    AIAgent clinicalExtractor = chatClient.CreateAIAgent(
        name: "ClinicalDataExtractor",
        instructions: AgentInstructions.ClinicalDataExtractor,
        tools: [AIFunctionFactory.Create(semanticSearch.SearchMedicalKnowledge)]);

    AIAgent secretary = chatClient.CreateAIAgent(
        name: "MedicalSecretary",
        instructions: AgentInstructions.MedicalSecretary,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData),
            AIFunctionFactory.Create(patientRegistry.UpsertPatientRecord),
            AIFunctionFactory.Create(exporter.SaveReportToPdf)
        ]);

    // 6. Orchestrator
    CoordinatedAgentGroupChat groupChat = new(
        coordinator: coordinator,
        specialists: new Dictionary<string, AIAgent>
        {
            { "ClinicalDataExtractor", clinicalExtractor },
            { "MedicalSecretary",      secretary }
        },
        maxTurns: 20);

    if (File.Exists(HistoryFile))
    {
        Console.WriteLine("--- Loading previous session history... ---");
        groupChat.LoadHistory(File.ReadAllText(HistoryFile), coordinator);
    }

    PrintHelp();

    // 7. REPL
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\n> ");
        Console.ResetColor();

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input)) continue;

        // Free-form input — coordinator auto-routes
        if (!input.StartsWith('/'))
        {
            await PrintMessagesAsync(groupChat.RunAsync(input), GetAgentColor);
            continue;
        }

        var parts      = input.Split(' ', 2);
        var command    = parts[0].ToLower();
        var cmdArgs    = parts.Length > 1 ? parts[1] : string.Empty;

        switch (command)
        {
            case "/exit":
            case "/quit":
                SaveHistory(groupChat, HistoryFile);
                return;

            case "/help":
                PrintHelp();
                break;

            case "/reset":
                groupChat.Reset();
                Console.WriteLine("✓ Conversation history cleared.");
                break;

            case "/list":
                try   { Console.WriteLine(patientRegistry.ListAllPatients()); }
                catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
                break;

            case "/query":
                if (string.IsNullOrWhiteSpace(cmdArgs)) { Console.WriteLine("Usage: /query <patient name>"); break; }
                await PrintMessagesAsync(groupChat.RunQueryAsync(cmdArgs));
                break;

            case "/document":
                if (string.IsNullOrWhiteSpace(cmdArgs)) { Console.WriteLine("Usage: /document <clinical notes>"); break; }
                await PrintMessagesAsync(
                    groupChat.RunAsync($"DOCUMENT: Process these clinical notes: {cmdArgs}"),
                    GetAgentColor);
                break;

            default:
                Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
                break;
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
    Console.ResetColor();
}

// ── Local Functions ───────────────────────────────────────────────────────────

/// Renders a stream of agent messages to the console.
/// Accepts an optional color selector; defaults to Yellow for every author.
static async Task PrintMessagesAsync(
    IAsyncEnumerable<AgentMessage> stream,
    Func<string, ConsoleColor>?    colorSelector = null)
{
    string? currentAuthor = null;

    await foreach (var message in stream)
    {
        if (currentAuthor != message.AuthorName)
        {
            if (currentAuthor != null) Console.WriteLine();
            Console.ForegroundColor = colorSelector?.Invoke(message.AuthorName) ?? ConsoleColor.Yellow;
            Console.WriteLine($"\n--- [{message.AuthorName}] ---");
            Console.ResetColor();
            currentAuthor = message.AuthorName;
        }

        if (message.isStreaming)     Console.Write(message.Text);
        else if (message.isComplete) Console.WriteLine();
        else                         Console.WriteLine(message.Text);
    }

    Console.WriteLine();
}

static ConsoleColor GetAgentColor(string authorName) => authorName switch
{
    "User"               => ConsoleColor.Green,
    "System"             => ConsoleColor.DarkGray,
    "MedicalCoordinator" => ConsoleColor.Magenta,
    _                    => ConsoleColor.Yellow
};

static void SaveHistory(CoordinatedAgentGroupChat chat, string path)
{
    try
    {
        var json = chat.ExportHistory();
        if (string.IsNullOrWhiteSpace(json))
        {
            Console.WriteLine("No history to save. Goodbye!");
            return;
        }
        File.WriteAllText(path, json);
        Console.WriteLine("History saved. Goodbye!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Could not save history: {ex.Message}");
    }
}

static void PrintHelp()
{
    Console.WriteLine("\n=== COMMANDS ===");
    Console.WriteLine("/query <patient>     - Query patient information (fast)");
    Console.WriteLine("/document <notes>    - Process new clinical notes (sequential)");
    Console.WriteLine("/list                - List all patients in database");
    Console.WriteLine("/reset               - Clear conversation history");
    Console.WriteLine("/help                - Show this help");
    Console.WriteLine("/exit                - Save and exit");
    Console.WriteLine("\nOr enter free-form input (coordinator will auto-route)");
}

