using System.ClientModel;
using _09_medical_semantic_rag;
using _09_medical_semantic_rag.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using SmartComponents.LocalEmbeddings;

// Configuration Constants
const string lmStudioEndpoint = "http://localhost:1234/v1";
const string modelId = "qwen2.5-7b-instruct";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Semantic RAG Multi-Agent Medical System (09-semantic-rag) ===");
Console.ResetColor();

try
{
    // ── 1. Initialize the Local Embedding Engine (Singleton) ─────────────────
    // LocalEmbedder is expensive to construct — it loads the ONNX model into
    // memory. We create it once here and share it across the application.
    Console.WriteLine("Loading local embedding model...");
    using var embedder = new LocalEmbedder();
    Console.WriteLine("✓ Embedding model loaded (in-process, CPU).");

    // ── 2. Seed the pipe-delimited acronyms knowledge base ───────────────────
    var semanticSearch = new SemanticMedicalSearch(embedder);
    var acronymsPath = SemanticMedicalSearch.GetDefaultAcronymsPath();

    if (!File.Exists(acronymsPath))
    {
        var dir = Path.GetDirectoryName(acronymsPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Pipe-delimited format: Main Term | Acronym | Synonym1, Synonym2, ...
        // The embedding for each entry uses a "rich semantic string" built
        // from the main term and all synonyms combined.
        await File.WriteAllTextAsync(acronymsPath, """
            # Medical Knowledge Base — Pipe-delimited format
            # Format: Main Term | Acronym | Synonym1, Synonym2, ...
            # Empty synonym column is allowed.
            Hypertension | HTA | Arterial Hypertension, High Blood Pressure, blood pressure high, elevated blood pressure
            Diabetes Mellitus Type 2 | DM2 | Diabetes, Type 2 Diabetes, sugar disease, adult-onset diabetes
            Diabetes Mellitus Type 1 | DM1 | Juvenile Diabetes, Type 1 Diabetes, insulin-dependent diabetes
            Chronic Obstructive Pulmonary Disease | COPD | Emphysema, Chronic Bronchitis, smoker's lung
            Congestive Heart Failure | ICC | Heart Failure, Cardiac Insufficiency, CHF, cardiac failure
            Dyslipidemia | DL | Hyperlipidemia, High Cholesterol, elevated lipids, high fats in blood
            Atrial Fibrillation | FA | Auricular Fibrillation, AFib, irregular heartbeat, heart flutter
            Coronary Artery Disease | CAD | Ischemic Heart Disease, coronary disease, blocked arteries
            Chronic Kidney Disease | CKD | Renal Insufficiency, kidney failure, chronic renal failure
            Asthma | Asthma | Bronchial Asthma, reactive airway disease, wheezing disease
            Obesity | Obesity | Morbid Obesity, overweight condition, BMI high
            Depression | Depression | Major Depressive Disorder, clinical depression, depressive disorder
            Anxiety | Anxiety | Generalized Anxiety Disorder, anxiety disorder, panic disorder
            Stroke | CVA | Cerebrovascular Accident, brain stroke, brain infarction, ischemic stroke
            Myocardial Infarction | MI | Heart Attack, cardiac arrest, coronary thrombosis
            """);

        Console.WriteLine($"✓ Default medical knowledge base created: {acronymsPath}");
    }
    else
    {
        Console.WriteLine($"✓ Medical knowledge base found: {acronymsPath}");
    }

    // ── 3. Build the Vector Index ─────────────────────────────────────────────
    // Reads the file and pre-computes all embeddings — happens once at startup.
    semanticSearch.Initialize(acronymsPath);

    // ── 4. Setup the Local LLM Client (LM Studio) ────────────────────────────
    var client = new OpenAIClient(
        new ApiKeyCredential("lm-studio"),
        new OpenAIClientOptions { Endpoint = new Uri(lmStudioEndpoint) });

    var openAiChatClient = client.GetChatClient(modelId);
    var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    // ── 5. Instantiate persistence and export tools ───────────────────────────
    var exporter = new MedicalReportExporter();
    var patientRegistry = new PatientRegistry();

    patientRegistry.Initialize();
    Console.WriteLine("✓ Database initialized (hospital.db).");

    // ── 6. Create Agents ──────────────────────────────────────────────────────
    AIAgent coordinator = chatClient.CreateAIAgent(
        name: "MedicalCoordinator",
        instructions: AgentInstructions.Coordinator
    );

    AIAgent medicalDataAnalyst = chatClient.CreateAIAgent(
        name: "ClinicalDataExtractor",
        instructions: AgentInstructions.ClinicalDataExtractor,
        tools:
        [
            // Semantic search tool — backed by LocalEmbedder (in-process)
            AIFunctionFactory.Create(semanticSearch.SearchMedicalKnowledge)
        ]
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

    // ── 7. Wire up the Coordinator Group Chat ────────────────────────────────
    var primarySpecialists = new Dictionary<string, AIAgent>
    {
        { "ClinicalDataExtractor", medicalDataAnalyst },
        { "MedicalSecretary", medicalAdmin }
    };

    const string historyFile = "chat_history_coordinator.json";

    CoordinatedAgentGroupChat groupChat = new(
        coordinator: coordinator,
        specialists: primarySpecialists,
        maxTurns: 20
    );

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

    // ── 8. Main REPL loop ─────────────────────────────────────────────────────
    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\n> ");
        Console.ResetColor();

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
            continue;

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

        // ── Execute the coordinated workflow ──────────────────────────────────
        string? currentAgent = null;

        await foreach (var message in groupChat.RunAsync(input))
        {
            if (currentAgent != message.AuthorName)
            {
                if (currentAgent != null) Console.WriteLine();

                Console.ForegroundColor = message.AuthorName switch
                {
                    "User"               => ConsoleColor.Green,
                    "System"             => ConsoleColor.DarkGray,
                    "MedicalCoordinator" => ConsoleColor.Magenta,
                    _                    => ConsoleColor.Yellow
                };

                Console.WriteLine($"\n--- [{message.AuthorName}] ---");
                Console.ResetColor();
                currentAgent = message.AuthorName;
            }

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

        Console.WriteLine();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
    Console.ResetColor();
}
