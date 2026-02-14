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

                      ADMISSION vs PRIOR HISTORY RULE:
                      The following phrases all signal the CURRENT DIAGNOSIS (the reason the patient came to the hospital):
                        Spanish: "ingresa por", "ingresa con", "llega con", "llega por", "acude por", "acude con", "motivo de ingreso"
                        English: "admitted for", "admitted with", "presented with", "presents with", "came in with", "reason for admission"
                      - The condition described after any of these phrases → CURRENT DIAGNOSIS
                      - All other pathologies mentioned (pre-existing, personal history, "antecedentes personales") → MEDICAL HISTORY, not Current Diagnosis
                      - NEVER invent conditions not mentioned in the notes

                      OUTPUT FORMAT (always use this exact structure so MedicalSecretary can parse it):
                      Patient: [full name]
                      Room: [room number/identifier, or "not mentioned" if not in notes]
                      Age: [numeric age, or "not mentioned" if not in notes]
                      Medical History (AP): [comma-separated list of ALL pre-existing conditions, allergies, and ongoing medications.
                        - Use the standard acronym if one is widely recognized (e.g. HTA, DM, COPD, ICC, DL, FA, EPOC)
                        - If no standard acronym exists or the condition is uncommon, write the full word (e.g. Obesity, Depression)
                        - Allergies: Allergy:X (e.g. Allergy:Penicillin, Allergy:Cat hair)
                        - Ongoing medications: Med:X (e.g. Med:Metformin)]
                      Current Diagnosis (Dx): [the condition(s) stated as the reason for the current admission — full text, NO acronyms]
                      Evolution: [Good | Stable | Bad - assess patient's clinical trajectory]
                      Plan: [comma-separated list of ALL active treatments and any action that is ongoing, ordered, scheduled, pending, or requested.
                        Includes any of the following categories (in Spanish or English):
                        - Active medication / Medicación activa (e.g. "Bronchodilators", "Corticoides inhalados")
                        - Medication adjustment / Ajuste de medicación (e.g. "Adjust insulin dose", "Ajustar medicación")
                        - Pending lab work / Analíticas pendientes (e.g. "Pending CBC and BMP", "Revisar analíticas pendientes")
                        - Pending microbiology / Microbiología pendiente (e.g. "Pending blood cultures", "Revisar pruebas microbiológicas")
                        - Pending tests or procedures / Pruebas o procedimientos pendientes:
                            endoscopy / endoscopia, radiology / pruebas radiológicas (X-ray, CT, MRI, ultrasound / TAC, RMN, ecografía),
                            radiological intervention / intervencionismo radiológico, central line / vía central
                        - Surgery / Cirugía (e.g. "Scheduled cholecystectomy", "Cirugía pendiente")
                        - Rehabilitation / Rehabilitación (e.g. "Physical therapy", "Rehabilitación respiratoria")
                        - Specialist consultation / Valoración de especialidad (e.g. "Cardiology consult", "Valoración por cardiología")
                        - Patient discharge / Alta del paciente (e.g. "Discharge planned", "Alta a domicilio")
                        - Transfer to another centre / Derivación a otro centro
                        - Patient repatriation / Repatriación del paciente a su país
                        RULE: if something is described as "pending", "scheduled", "requested", "ordered", or "to be done", it goes here, NOT in Observations.
                      Observations: [ONLY information that genuinely does not fit in any other field — e.g. vital signs, social/family history, relevant clinical context not covered above.
                        BEFORE writing anything here, ask yourself: "Is this already in Medical History, Current Diagnosis, or Plan?" If yes → leave it out.
                        Observations MUST be empty ("None") if everything in the notes has already been captured in the other fields.
                        WRONG EXAMPLE: "Noted dyspnea and hypoxemia, viral infection, bronchoospasm, suspected asthma; ruled out cardiac cause." ← this repeats Current Diagnosis — DO NOT do this.
                        CORRECT: if the cardiology evaluation result adds context not in Current Diagnosis, write only that new information (e.g. "Cardiac origin ruled out by cardiology").
                        DO NOT include: allergies or medications (Medical History), anything pending/scheduled/ordered (Plan), anything already in Medical History (AP) or Current Diagnosis (Dx).]
                      Clinical Summary: [2-3 sentence clinical assessment]

                      CRITICAL RULES:
                      - Medical History (AP): pre-existing conditions using standard acronym when it exists, full word otherwise; allergies as Allergy:X; medications as Med:X
                      - Current Diagnosis (Dx): ONLY the reason for the current admission — FULL TEXT, NO acronyms
                      - Evolution: Must be exactly "Good", "Stable", or "Bad"
                      - Plan: includes active treatments AND anything pending, scheduled, ordered, or requested (e.g. "Pending chest CT scan without contrast" → Plan)
                      - Observations: ONLY genuinely new context not captured elsewhere. If nothing qualifies, write "None". NEVER repeat Medical History, Current Diagnosis, or Plan content.

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
