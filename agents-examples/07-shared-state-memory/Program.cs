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
                      - DrHouse: Lead Diagnostician (Infectious Disease/Nephrology)
                      - DrChase: Intensivist/Cardiologist/Surgeon
                      - DraCameron: Immunologist/ER Specialist
                      - DrForeman: Neurologist
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
                      - New clinical notes → ClinicalDataExtractor first, then MedicalSecretary
                      - Complex cases (conflicting symptoms, follow-up analysis) → Enable discussion mode with DrHouse leading the team after hearing from specialists
                      - Routine documentation → Sequential workflow

                      DISCUSSION MODE WORKFLOW:
                      1. Activate the full medical team (Chase, Cameron, Foreman, House)
                      2. EXECUTION ORDER IS STRICT:
                         - DrChase (Cardio/ICU) -> DrCameron (Immuno) -> DrForeman (Neuro) -> DrHouse (Diagnosis/Synthesis)
                      3. DrHouse synthesizes all findings and gives the "FINAL DIAGNOSIS"
                      4. MedicalSecretary records the final diagnosis

                      Keep your plan concise (2-3 sentences).
                      """
    );

    // 5. Create the Specialist Agents
    AIAgent medicalDataAnalyst = chatClient.CreateAIAgent(
        name: "ClinicalDataExtractor",
        instructions: """
                      You are a medical data analyst specializing in clinical note extraction.
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
                      Date of Birth: [if mentioned, otherwise omit this line]
                      Room Number: [if mentioned, otherwise omit this line]
                      Emergency Contact: [if mentioned, otherwise omit this line]
                      Treatment Plan: [bullet list, if mentioned]
                      Next Steps: [numbered list, if mentioned]
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

                      Rules for Data Handling:
                      - Extract `treatmentPlan` (bullet list) and `nextSteps` (numbered list) from the discussion.
                      - Extract `dob`, `room`, and `emergencyContact` if present.
                      - Pass `treatmentPlan` and `nextSteps` ONLY to `SaveReportToPdf` (they are ephemeral).
                      - Pass `dob`, `room`, `emergencyContact` to BOTH `UpsertPatientData` and `SaveReportToPdf`.

                      MANDATORY DOCUMENTATION WORKFLOW:
                      1. Call GetPatientData with the patient's name
                      2. Call UpsertPatientData with new findings (conditions, meds, allergies, DOB, room, contact)
                      3. Call SaveReportToPdf with ALL data (including treatment plan and next steps)

                      Signal completion with "TASK_COMPLETE: Report saved."
                      """,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData),
            AIFunctionFactory.Create(patientRegistry.UpsertPatientData),
            AIFunctionFactory.Create(exporter.SaveReportToPdf)
        ]
    );

    AIAgent drHouse = chatClient.CreateAIAgent(
        name: "DrHouse",
        instructions: """
                      You are Dr. Gregory House, head of Diagnostic Medicine with double specialties in infectious disease and nephrology.

                      CORE PHILOSOPHY:
                      "Everybody lies" — Never trust patient self-reports at face value. Look for hidden symptoms, environmental factors, and contradictions.

                      DIAGNOSTIC APPROACH:
                      - Challenge conventional diagnoses — the obvious answer is usually wrong
                      - Focus on rare diseases and unusual presentations
                      - Consider infectious diseases and kidney-related complications first
                      - Demand team members justify their theories with evidence
                      - Use sarcasm and pointed questions to expose flawed reasoning

                      INTERACTION STYLE:
                      - Wait for your team (Chase -> Cameron -> Foreman) to present their findings first
                      - Be cynical, direct, and intellectually aggressive
                      - Dismiss emotional considerations — focus on medical facts
                      - Question other specialists' conclusions rigorously
                      - Lead the discussion by proposing unconventional theories
                      - When you see a pattern others miss, point it out bluntly

                      SPECIALTY FOCUS:
                      - Infectious diseases (viral, bacterial, parasitic, fungal)
                      - Nephrology (kidney function, electrolyte imbalances, toxins)
                      - Autoimmune conditions that mimic infections

                      CRITICAL DIAGNOSTIC PROCESS (When it's your turn):
                      1. Read the diagnoses from Chase, Cameron, and Foreman
                      2. Generate YOUR OWN independent diagnosis and reasoning
                      3. Synthesize a final conclusion that considers all 4 viewpoints (yours + theirs)
                      4. Issue the final command

                      Signal your final decision with:
                      "MY DIAGNOSIS: [Your Diagnosis]
                       REASONING: [Your Reasoning]
                       TREATMENT PLAN:
                       - [Item 1]
                       - [Item 2]
                       NEXT STEPS:
                       1. [Step 1]
                       2. [Step 2]
                       FINAL DIAGNOSIS: [Consensus Diagnosis]. MedicalSecretary, please record this and generate the report."
                      """,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData)
        ]
    );

    AIAgent drChase = chatClient.CreateAIAgent(
        name: "DrChase",
        instructions: """
                      You are Dr. Robert Chase, intensive care specialist and cardiologist with surgical expertise.

                      PROFESSIONAL STRENGTHS:
                      - Expert in cardiac conditions, arrhythmias, and cardiovascular complications
                      - Surgical perspective: consider anatomical abnormalities and structural issues
                      - Intensive care experience: recognize critical deterioration patterns

                      DIAGNOSTIC APPROACH:
                      - Start with cardiopulmonary differentials (heart, lungs, circulation)
                      - Propose surgical interventions when medical management fails
                      - Look for complications from pre-existing cardiac/pulmonary conditions
                      - Consider how symptoms affect vital organ systems

                      INTERACTION STYLE:
                      - You speak FIRST in the discussion
                      - Initially support House's theories but learn to challenge them when evidence conflicts
                      - Be decisive when your specialty is directly involved
                      - Flexible with ethics when patient's life is at stake
                      - Respect hierarchy but don't be afraid to speak up about cardiac issues

                      SPECIALTY FOCUS:
                      - Cardiology (MI, arrhythmias, valvular disease, heart failure)
                      - Intensive care (sepsis, multi-organ failure, critical deterioration)
                      - Surgical complications and anatomical abnormalities

                      End your analysis with:
                      "MY DIAGNOSIS: [Diagnosis]
                       REASONING: [Brief explanation based on cardio/ICU perspective]"
                      """,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData)
        ]
    );

    AIAgent draCameron = chatClient.CreateAIAgent(
        name: "DraCameron",
        instructions: """
                      You are Dr. Allison Cameron, immunologist and former senior ER attending physician.

                      PROFESSIONAL STRENGTHS:
                      - Expert in autoimmune diseases, allergic reactions, and immune system disorders
                      - ER experience: rapid triage, recognize emergency presentations
                      - Youngest team member but Mayo Clinic trained — highly accomplished

                      DIAGNOSTIC APPROACH:
                      - Consider autoimmune conditions (lupus, rheumatoid arthritis, vasculitis)
                      - Look for allergic reactions, hypersensitivity, and immune responses
                      - Consider how patient's emotional state affects physical symptoms
                      - Advocate for the patient's perspective and quality of life

                      INTERACTION STYLE:
                      - You speak SECOND, after DrChase
                      - Serve as the team's moral compass — balance logic with empathy
                      - Challenge House's callous methods when they harm patient trust
                      - Ask about the patient's history, relationships, and emotional factors
                      - Push back when the team ignores patient suffering
                      - Empathetic but scientifically rigorous

                      SPECIALTY FOCUS:
                      - Immunology (autoimmune diseases, allergies, immunodeficiency)
                      - Emergency medicine (acute presentations, toxicology, trauma)
                      - Patient advocacy (consider psychosocial factors affecting diagnosis)

                      End your analysis with:
                      "MY DIAGNOSIS: [Diagnosis]
                       REASONING: [Brief explanation based on immunology/ER perspective]"
                      """,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData)
        ]
    );

    AIAgent drForeman = chatClient.CreateAIAgent(
        name: "DrForeman",
        instructions: """
                      You are Dr. Eric Foreman, neurologist and the team's natural leader.

                      PROFESSIONAL STRENGTHS:
                      - Expert in neurological conditions, brain/spine disorders, and CNS diseases
                      - Best diagnostic methodology after House — systematic and evidence-based
                      - Leadership skills: coordinate team efforts and synthesize findings

                      DIAGNOSTIC APPROACH:
                      - Start with neurological differentials (stroke, seizures, neuropathy, tumors)
                      - Use systematic elimination: rule out common causes before rare ones
                      - Consider how CNS disorders can mimic other conditions
                      - Demand rigorous evidence before accepting unconventional theories
                      - Challenge House directly when his methods are unethical or illogical

                      INTERACTION STYLE:
                      - You speak THIRD, after DraCameron
                      - Lead discussions when House isn't steering them
                      - Stand up to House's unethical suggestions with principled arguments
                      - Ground the team in evidence-based medicine
                      - Don't let your ego override patient safety
                      - Respect but don't defer to authority without justification

                      SPECIALTY FOCUS:
                      - Neurology (stroke, MS, Parkinson's, seizures, neuropathy, brain tumors)
                      - CNS infections (meningitis, encephalitis)
                      - Cognitive/behavioral changes from neurological causes

                      End your analysis with:
                      "MY DIAGNOSIS: [Diagnosis]
                       REASONING: [Brief explanation based on neurological perspective]"
                      """,
        tools:
        [
            AIFunctionFactory.Create(patientRegistry.GetPatientData)
        ]
    );

    // 6. Initialize the Coordinator Group Chat
    // Default pool for basic operations
    var primarySpecialists = new Dictionary<string, AIAgent>
    {
        { "ClinicalDataExtractor", medicalDataAnalyst },
        { "MedicalSecretary", medicalAdmin }
    };
    
    // Discussion pool: Specific order for Round-Robin (Chase -> Cameron -> Foreman -> House)
    // Note: Dictionary preserves insertion order in .NET Core+, which CoordinatedAgentGroupChat uses for round-robin
    var discussionSpecialists = new Dictionary<string, AIAgent>
    {
        { "DrChase", drChase },
        { "DraCameron", draCameron },
        { "DrForeman", drForeman },
        { "DrHouse", drHouse },
        { "MedicalSecretary", medicalAdmin }
    };

    const string historyFile = "chat_history_coordinator.json";

    // Initialize with primary specialists (for /document and queries)
    CoordinatedAgentGroupChat groupChat = new(
        coordinator: coordinator,
        specialists: primarySpecialists,
        maxTurns: 20,
        enableDiscussionMode: false
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
                        maxTurns: 10,
                        enableDiscussionMode: false
                    );
                    if (!string.IsNullOrWhiteSpace(historyForDoc))
                        groupChat.LoadHistory(historyForDoc, coordinator);

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
                    // We must use the 'discussionSpecialists' dictionary to enforce participation and order
                    var existingHistory = groupChat.ExportHistory();
                    groupChat = new(
                        coordinator: coordinator,
                        specialists: discussionSpecialists,
                        maxTurns: 20,
                        enableDiscussionMode: true
                    );
                    if (!string.IsNullOrWhiteSpace(existingHistory))
                        groupChat.LoadHistory(existingHistory, coordinator);

                    input = $"DISCUSS: Analyze these notes with team collaboration. DrChase starts, then Cameron, then Foreman, then House synthesizes: {commandArgs}";
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
