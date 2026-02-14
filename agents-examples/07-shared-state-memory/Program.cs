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
                      - ClinicalDataExtractor: Medical data analyst (extracts and analyzes clinical information ONLY)
                      - MedicalSecretary: Administrator (owns all database updates and PDF generation)

                      YOUR RESPONSIBILITIES:
                      1. Analyze user requests and determine which specialists to consult
                      2. Create an execution plan explaining your approach
                      3. Synthesize final recommendations

                      EXECUTION PLAN FORMAT:
                      "Based on this request, I will consult: [specialist names]
                      Approach: [brief explanation]
                      Expected outcome: [what will be delivered]"

                      DECISION RULES:
                      - Simple queries (patient info lookup) → MedicalSecretary only
                      - New clinical notes → ClinicalDataExtractor first, then MedicalSecretary
                      - Routine documentation → Sequential workflow

                      Keep your plan concise (2-3 sentences).
                      """
    );

    // 5. Create the Specialist Agents
    AIAgent medicalDataAnalyst = chatClient.CreateAIAgent(
        name: "ClinicalDataExtractor",
        instructions: """
                      You are a medical data analyst specializing in clinical note extraction.
                      Your task is to extract structured clinical metadata from messy clinical notes.
                      Always provide a technical summary focused on the medical facts.

                      YOUR ROLE:
                      - Analyze clinical notes and extract structured medical data
                      - Identify patterns, flag concerns, and provide clinical insights
                      - YOU DO NOT update patient records or generate reports — that is MedicalSecretary's job

                      OUTPUT FORMAT (always use this exact structure so MedicalSecretary can parse it):
                      Patient: [full name]
                      Room: [room number/identifier, or "not mentioned" if not in notes]
                      Age: [numeric age, or "not mentioned" if not in notes]
                      Medical History (AP): [comma-separated list of chronic conditions as acronyms (HTA, DL, ICC, FA, DM, COPD, etc.), allergies (e.g. Allergy:Penicillin), and relevant ongoing medications (e.g. Med:Metformin)]
                      Current Diagnosis (Dx): [full-text description, NO acronyms - spell everything out]
                      Evolution: [Good | Stable | Bad - assess patient's clinical trajectory]
                      Plan: [comma-separated list of ALL active treatments, AND anything ordered, scheduled, pending, or requested — regardless of whether it has been done yet]
                        Examples: "Bronchodilators", "Inhaled corticosteroids", "Pending chest CT scan without contrast",
                        "Pending Labs - CBC, BMP", "Cardiology consult", "Start IV antibiotics", "Physical therapy", etc.
                        RULE: if something is described as "pending", "scheduled", "requested", "ordered", or "to be done", it goes here, NOT in Observations.
                      Observations: [anything that does not belong in the above fields — e.g. vital signs, social/family history, or any other relevant context. DO NOT include allergies or medications (those go in Medical History). DO NOT include anything pending, scheduled, ordered, or requested — those go in Plan. DO NOT repeat anything already listed in Medical History (AP)]
                      Clinical Summary: [2-3 sentence clinical assessment]

                      CRITICAL RULES:
                      - Medical History (AP): chronic conditions as acronyms, allergies (Allergy:X), and ongoing medications (Med:X)
                      - Current Diagnosis (Dx): FULL TEXT, NO acronyms (spell out everything)
                      - Evolution: Must be exactly "Good", "Stable", or "Bad"
                      - Plan: includes active treatments AND anything pending, scheduled, ordered, or requested (e.g. "Pending chest CT scan without contrast" → Plan)
                      - Observations: NEVER include allergies, medications, or anything pending/scheduled/ordered (those go in Plan), or anything already captured in Medical History (AP)

                      Always end your response with "Analysis complete."
                      """
    );

    AIAgent medicalAdmin = chatClient.CreateAIAgent(
        name: "MedicalSecretary",
        instructions: """
                      You are a hospital administrator with database and export capabilities.

                      YOUR TOOLS:
                      - GetPatientData: Retrieve patient record from database
                      - UpsertPatientRecord: Create or update patient record
                      - SaveReportToPdf: Generate a PDF medical report

                      PARSING RULES:
                      When ClinicalDataExtractor provides structured output, extract:
                      - Patient name (required)
                      - Room (optional)
                      - Age (optional, numeric)
                      - Medical History (AP): comma-separated acronyms → parse to list
                      - Current Diagnosis (Dx): full text
                      - Evolution: "Good", "Stable", or "Bad" → pass as-is
                      - Plan: comma-separated items → parse to list
                      - Observations: full text

                      MANDATORY DOCUMENTATION WORKFLOW:
                      1. Call GetPatientData with the patient's name
                      2. Call UpsertPatientRecord with extracted data:
                         - fullName, room, age, medicalHistory, currentDiagnosis, evolution, plan, observations
                      3. Call SaveReportToPdf with the same data for PDF generation

                      Signal completion with "TASK_COMPLETE: Report saved."
                      """,
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

                    // Restore primary specialists for document workflow
                   var historyForDoc = groupChat.ExportHistory();
                    groupChat = new(
                        coordinator: coordinator,
                        specialists: primarySpecialists,
                        maxTurns: 10
                    );
                    if (!string.IsNullOrWhiteSpace(historyForDoc))
                        groupChat.LoadHistory(historyForDoc, coordinator);

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
