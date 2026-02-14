using System.ClientModel;
using _07_shared_state_memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

// Configuration Constants
const string lmStudioEndpoint = "http://localhost:1234/v1";
const string modelId = "qwen2.5-7b-instruct";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Coordinator-Based Multi-Agent Medical System ===");
Console.ResetColor();

try
{
    // 1. Setup the local LLM Client (LM Studio)
    var client = new OpenAIClient(
        new ApiKeyCredential("lm-studio"),
        new OpenAIClientOptions { Endpoint = new Uri(lmStudioEndpoint) });

    // 2. Build the ChatClient with Function Invocation enabled
    var openAiChatClient = client.GetChatClient(modelId);
    var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    // 3. Instantiate our local tools
    var exporter = new MedicalReportExporter();
    var patientRegistry = new PatientRegistry();

    patientRegistry.Initialize();
    Console.WriteLine("Database initialized (hospital.db).");

    // 4. Create the Coordinator Agent
    AIAgent coordinator = chatClient.CreateAIAgent(
        name: "MedicalCoordinator",
        instructions: AgentInstructions.Coordinator
    );

    // 5. Create the Specialist Agents
    AIAgent medicalDataAnalyst = chatClient.CreateAIAgent(
        name: "ClinicalDataExtractor",
        instructions: AgentInstructions.ClinicalDataExtractor
    );

    AIAgent medicalAdmin = chatClient.CreateAIAgent(
        name: "MedicalSecretary",
        instructions: AgentInstructions.MedicalSecretary,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData),
            AIFunctionFactory.Create(patientRegistry.UpsertPatientRecord),
            AIFunctionFactory.Create(exporter.SaveReportToPdf)
        ]
    );

    // 6. Initialize the Coordinator Group Chat
    var primarySpecialists = new Dictionary<string, AIAgent>
    {
        { "ClinicalDataExtractor", medicalDataAnalyst },
        { "MedicalSecretary", medicalAdmin }
    };

    const string historyFile = "chat_history_coordinator.json";

    // Initialize with primary specialists (for /document and queries)
    CoordinatedAgentGroupChat groupChat = new(
        coordinator: coordinator,
        specialists: primarySpecialists,
        maxTurns: 20
    );

    // Load existing chat history if file exists
    if (File.Exists(historyFile))
    {
        Console.WriteLine("--- Loading previous session history... ---");
        string savedJson = File.ReadAllText(historyFile);
        groupChat.LoadHistory(savedJson, coordinator);
    }

    Console.WriteLine("\n=== COMMANDS ===");
    Console.WriteLine("/query <patient>     - Query patient information (fast)");
    Console.WriteLine("/document <notes>    - Process new clinical notes (sequential)");
    Console.WriteLine("/list                - List all patients in database");
    Console.WriteLine("/reset               - Clear conversation history");
    Console.WriteLine("/help                - Show this help");
    Console.WriteLine("/exit                - Save and exit");
    Console.WriteLine("\nOr enter free-form input (coordinator will auto-route)");

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\n> ");
        Console.ResetColor();

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            continue;

        // Handle commands
        if (input.StartsWith("/"))
        {
            var parts = input.Split(' ', 2);
            var command = parts[0].ToLower();
            var commandArgs = parts.Length > 1 ? parts[1] : "";

            switch (command)
            {
                case "/exit":
                case "/quit":
                    try
                    {
                        var jsonToSave = groupChat.ExportHistory();
                        if (!string.IsNullOrWhiteSpace(jsonToSave))
                        {
                            File.WriteAllText(historyFile, jsonToSave);
                            Console.WriteLine("History saved. Goodbye!");
                        }
                        else
                        {
                            Console.WriteLine("No history to save. Goodbye!");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not save history: {ex.Message}");
                    }

                    return;

                case "/help":
                    Console.WriteLine("\n=== AVAILABLE COMMANDS ===");
                    Console.WriteLine("/query <patient>     - Simple patient lookup");
                    Console.WriteLine("/document <notes>    - Standard documentation workflow");
                    Console.WriteLine("/list                - Show all patients");
                    Console.WriteLine("/reset               - Clear history");
                    Console.WriteLine("/help                - This help");
                    Console.WriteLine("/exit                - Save and exit");
                    continue;

                case "/reset":
                    groupChat.Reset();
                    Console.WriteLine("✓ Conversation history cleared.");
                    continue;

                case "/list":
                    try
                    {
                        var patients = patientRegistry.ListAllPatients();
                        Console.WriteLine(patients);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: {ex.Message}");
                    }

                    continue;

                case "/query":
                    if (string.IsNullOrWhiteSpace(commandArgs))
                    {
                        Console.WriteLine("Usage: /query <patient name>");
                        continue;
                    }

                    // Direct query: bypass coordinator, MedicalSecretary only
                    string? queryAgent = null;
                    await foreach (var message in groupChat.RunQueryAsync(commandArgs))
                    {
                        if (queryAgent != message.AuthorName)
                        {
                            if (queryAgent != null) Console.WriteLine();
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"\n--- [{message.AuthorName}] ---");
                            Console.ResetColor();
                            queryAgent = message.AuthorName;
                        }

                        if (message.isStreaming)       Console.Write(message.Text);
                        else if (message.isComplete)   Console.WriteLine();
                        else                           Console.WriteLine(message.Text);
                    }
                    Console.WriteLine();
                    continue;

                case "/document":
                    if (string.IsNullOrWhiteSpace(commandArgs))
                    {
                        Console.WriteLine("Usage: /document <clinical notes>");
                        continue;
                    }

                    input = $"DOCUMENT: Process these clinical notes: {commandArgs}";
                    break;

                default:
                    Console.WriteLine($"Unknown command: {command}. Type /help for available commands.");
                    continue;
            }
        }

        // Execute the coordinated workflow
        string? currentAgent = null;

        await foreach (var message in groupChat.RunAsync(input))
        {
            // Print agent header when switching agents or starting
            if (currentAgent != message.AuthorName)
            {
                if (currentAgent != null)
                {
                    Console.WriteLine(); // Add spacing between agents
                }

                Console.ForegroundColor = message.AuthorName switch
                {
                    "User" => ConsoleColor.Green,
                    "System" => ConsoleColor.DarkGray,
                    "MedicalCoordinator" => ConsoleColor.Magenta,
                    _ => ConsoleColor.Yellow
                };

                Console.WriteLine($"\n--- [{message.AuthorName}] ---");
                Console.ResetColor();
                currentAgent = message.AuthorName;
            }

            // Display message content
            if (message.isStreaming)
            {
                Console.Write(message.Text);
            }
            else if (message.isComplete)
            {
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine(message.Text);
            }
        }

        Console.WriteLine(); // Final spacing
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
    Console.ResetColor();
}
