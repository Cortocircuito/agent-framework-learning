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
        instructions: """
                      You are a medical coordinator managing a team of specialists.

                      YOUR TEAM:
                      - DrHouse: Medical data analyst (extracts and analyzes clinical information ONLY)
                      - MedicalSecretary: Administrator (owns all database updates and PDF generation)

                      YOUR RESPONSIBILITIES:
                      1. Analyze user requests and determine which specialists to consult
                      2. Create an execution plan explaining your approach
                      3. Decide if specialists should discuss findings (for complex cases)
                      4. Synthesize final recommendations

                      EXECUTION PLAN FORMAT:
                      "Based on this request, I will consult: [specialist names]
                      Approach: [brief explanation]
                      Expected outcome: [what will be delivered]"

                      DECISION RULES:
                      - Simple queries (patient info lookup) → MedicalSecretary only
                      - New clinical notes → DrHouse first, then MedicalSecretary
                      - Complex cases (conflicting symptoms, follow-up analysis) → Enable discussion mode
                      - Routine documentation → Sequential workflow

                      Keep your plan concise (2-3 sentences).
                      """
    );

    // 5. Create the Specialist Agents
    AIAgent medicalSpecialist = chatClient.CreateAIAgent(
        name: "DrHouse",
        instructions: """
                      You are a senior medical specialist. 
                      Your only task is to extract diagnoses, symptoms, and treatments from messy clinical notes.
                      Always provide a technical summary focused on the medical facts.

                      YOUR ROLE:
                      - Analyze clinical notes and extract structured medical data
                      - Identify patterns, flag concerns, and provide clinical insights
                      - YOU DO NOT update patient records or generate reports — that is MedicalSecretary's job

                      OUTPUT FORMAT (always use this exact structure so MedicalSecretary can parse it):
                      Patient: [full name]
                      Conditions: [comma-separated diagnoses/conditions, or "none identified"]
                      Allergies: [comma-separated, or "none mentioned"]
                      Medications: [comma-separated, or "none mentioned"]
                      Blood Type: [if mentioned, otherwise omit this line]
                      Clinical Summary: [2-3 sentence clinical assessment]

                      DISCUSSION MODE:
                      - Share findings clearly using the output format above
                      - Request historical context from MedicalSecretary when needed
                      - Refine analysis based on registry data provided

                      Always end your response with "Analysis complete."
                      """
    );

    AIAgent medicalAdmin = chatClient.CreateAIAgent(
        name: "MedicalSecretary",
        instructions: """
                      You are a hospital administrator with database and export capabilities.

                      YOUR TOOLS:
                      - GetPatientData: Retrieve patient record from database
                      - UpsertPatientData: Create or update patient record
                      - SaveReportToPdf: Generate a PDF medical report

                      MANDATORY DOCUMENTATION WORKFLOW (when receiving DrHouse's analysis):
                      You MUST execute ALL three steps — skipping any step means the task is incomplete:
                      1. Call GetPatientData with the patient's name to retrieve existing record
                      2. Call UpsertPatientData to save DrHouse's findings (conditions, allergies, medications, blood type)
                      3. Call SaveReportToPdf to generate the final report

                      QUERY MODE (simple patient lookups only):
                      - Call GetPatientData and return the formatted result
                      - Do NOT call UpsertPatientData or SaveReportToPdf for simple queries

                      DISCUSSION MODE:
                      - Retrieve historical context from database when DrHouse requests it
                      - After discussion concludes, execute the full MANDATORY DOCUMENTATION WORKFLOW

                      RULES:
                      - Always extract the patient name from the context before calling any tool
                      - Always call GetPatientData BEFORE UpsertPatientData (check for conflicts)
                      - Call SaveReportToPdf exactly once per documentation session

                      Signal completion with "Task complete. Report saved."
                      """,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData),
            AIFunctionFactory.Create(patientRegistry.UpsertPatientData),
            AIFunctionFactory.Create(exporter.SaveReportToPdf)
        ]
    );

    // 6. Initialize the Coordinator Group Chat
    var specialists = new Dictionary<string, AIAgent>
    {
        { "DrHouse", medicalSpecialist },
        { "MedicalSecretary", medicalAdmin }
    };

    const string historyFile = "chat_history_coordinator.json";

    // DISCUSSION MODE: Set enableDiscussionMode=true for multi-turn collaborations
    CoordinatedAgentGroupChat groupChat = new(
        coordinator: coordinator,
        specialists: specialists,
        maxTurns: 20,
        enableDiscussionMode: false // Toggle this for /discuss command
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
    Console.WriteLine("/discuss <notes>     - Process with multi-turn discussion (deep analysis)");
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
                        File.WriteAllText(historyFile, jsonToSave);
                        Console.WriteLine("History saved. Goodbye!");
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
                    Console.WriteLine("/discuss <notes>     - Multi-turn discussion mode (deep analysis)");
                    Console.WriteLine("/list                - Show all patients");
                    Console.WriteLine("/reset               - Clear history");
                    Console.WriteLine("/help                - This help");
                    Console.WriteLine("/exit                - Save and exit");
                    Console.WriteLine("\nDISCUSS MODE: Enables specialists to collaborate over multiple turns.");
                    Console.WriteLine("Use for complex cases where cross-referencing is needed.");
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

                    input = $"QUERY: Retrieve information for patient '{commandArgs}'";
                    break;

                case "/document":
                    if (string.IsNullOrWhiteSpace(commandArgs))
                    {
                        Console.WriteLine("Usage: /document <clinical notes>");
                        continue;
                    }

                    input = $"DOCUMENT: Process these clinical notes: {commandArgs}";
                    break;

                case "/discuss":
                    if (string.IsNullOrWhiteSpace(commandArgs))
                    {
                        Console.WriteLine("Usage: /discuss <clinical notes>");
                        continue;
                    }

                    Console.WriteLine("⚙ Enabling multi-turn discussion mode...");

                    // Carry over existing history into the new discussion-mode instance
                    var existingHistory = groupChat.ExportHistory();
                    groupChat = new(
                        coordinator: coordinator,
                        specialists: specialists,
                        maxTurns: 20,
                        enableDiscussionMode: true
                    );
                    if (!string.IsNullOrWhiteSpace(existingHistory))
                        groupChat.LoadHistory(existingHistory, coordinator);

                    input = $"DISCUSS: Analyze these notes with team collaboration: {commandArgs}";
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
