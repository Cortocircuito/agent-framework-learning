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

            # ── Cardiovascular ───────────────────────────────────────────────────────────
            Hypertension | HTA | Arterial Hypertension, High Blood Pressure, elevated blood pressure, hypertensive disease, blood pressure high, HTA
            Atrial Fibrillation | FA | Auricular Fibrillation, AFib, AF, irregular heartbeat, heart flutter, paroxysmal atrial fibrillation, FA
            Coronary Artery Disease | CAD | Ischemic Heart Disease, coronary disease, coronary insufficiency, blocked coronary arteries, atherosclerotic heart disease
            Congestive Heart Failure | ICC | Heart Failure, Cardiac Insufficiency, CHF, cardiac failure, decompensated heart failure, left heart failure, right heart failure, ICC
            Myocardial Infarction | MI | Heart Attack, acute cardiac event, coronary thrombosis, cardiac infarction, AMI, STEMI, NSTEMI
            Acute Myocardial Infarction | AMI | Acute Heart Attack, ST-elevation MI, STEMI, acute MI, heart attack, coronary occlusion
            Acute Coronary Syndrome | ACS | Heart Attack Syndrome, unstable angina, NSTEMI, STEMI, acute cardiac ischemia
            Cardiomyopathy | CMP | Heart Muscle Disease, dilated cardiomyopathy, hypertrophic cardiomyopathy, heart muscle dysfunction, myocardiopathy
            Endocarditis | IE | Infective Endocarditis, bacterial endocarditis, heart valve infection, subacute bacterial endocarditis, SBE
            Ventricular Tachycardia | VT | Fast Heart Rhythm, ventricular arrhythmia, VT storm, rapid ventricular rhythm
            Supraventricular Tachycardia | SVT | Rapid Heartbeat, paroxysmal SVT, AVNRT, AVRT, narrow complex tachycardia
            Ventricular Fibrillation | VF | Cardiac Arrest Rhythm, VFib, ventricular flutter, fatal arrhythmia, cardiac arrest
            Aortic Stenosis | AS | Aortic Valve Stenosis, aortic valve narrowing, calcific aortic stenosis, aortic valve disease
            Mitral Regurgitation | MR | Mitral Valve Insufficiency, mitral incompetence, mitral valve regurgitation, mitral leak
            Mitral Stenosis | MS | Mitral Valve Stenosis, mitral valve narrowing, rheumatic mitral stenosis
            Pulmonary Hypertension | PH | Lung High Blood Pressure, pulmonary arterial hypertension, PAH, pulmonary vascular hypertension
            Peripheral Arterial Disease | PAD | Peripheral Vascular Disease, PVD, limb ischemia, arterial insufficiency, claudication
            Deep Vein Thrombosis | DVT | Blood Clot in Leg, venous thrombosis, leg thrombosis, venous clot, phlebothrombosis
            Pulmonary Embolism | PE | Lung Embolism, lung clot, pulmonary thromboembolism, PE, thromboembolism
            Transient Ischemic Attack | TIA | Mini Stroke, transient neurological deficit, warning stroke, mini-stroke
            Peripheral Neuropathy | PN | Nerve Damage, distal neuropathy, sensorimotor neuropathy, polyneuropathy

            # ── Metabolic & Endocrine ─────────────────────────────────────────────────────
            Diabetes Mellitus Type 2 | DM2 | Diabetes, Type 2 Diabetes, adult-onset diabetes, non-insulin-dependent diabetes, sugar disease, T2DM
            Diabetes Mellitus Type 1 | DM1 | Juvenile Diabetes, Type 1 Diabetes, insulin-dependent diabetes, autoimmune diabetes, T1DM
            Dyslipidemia | DL | Hyperlipidemia, High Cholesterol, elevated lipids, high blood fats, hypercholesterolemia, hypertriglyceridemia, mixed dyslipidemia
            Obesity | Obesity | Morbid Obesity, overweight, adiposity, BMI high, extreme obesity, class III obesity
            Thyroid Disease | TD | Thyroid disorder, thyroid pathology, thyroid dysfunction
            Hypothyroidism | Hypothyroidism | Underactive thyroid, low thyroid, thyroid deficiency, myxedema, thyroid hypofunction
            Hyperthyroidism | Hyperthyroidism | Overactive thyroid, Graves disease, thyrotoxicosis, thyroid overfunction, high thyroid
            Anemia | Anemia | Low hemoglobin, low red blood cells, anaemia, blood deficiency
            Iron Deficiency Anemia | IDA | Iron anemia, iron-poor blood, ferropenic anemia, sideropenic anemia
            Diabetic Neuropathy | DN | Diabetes Nerve Damage, diabetic peripheral neuropathy, diabetic sensory neuropathy
            Polycystic Ovary Syndrome | PCOS | Ovarian Syndrome, polycystic ovaries, PCOS, ovarian cysts, hormonal ovarian disorder

            # ── Pulmonary & Respiratory ───────────────────────────────────────────────────
            Chronic Obstructive Pulmonary Disease | COPD | Emphysema, Chronic Bronchitis, smoker's lung, obstructive lung disease, COPD
            Asthma | Asthma | Bronchial Asthma, reactive airway disease, wheezing disease, allergic asthma, exercise-induced asthma
            Obstructive Sleep Apnea | OSA | Sleep Apnea, OSAS, sleep disordered breathing, nocturnal apnea, apnea-hypopnea syndrome
            Pneumonia | PNA | Lung Infection, pulmonary infection, lobar pneumonia, bronchopneumonia, community pneumonia
            Community Acquired Pneumonia | CAP | Pneumonia Acquired Outside Hospital, out-of-hospital pneumonia, ambulatory pneumonia
            Hospital Acquired Pneumonia | HAP | Nosocomial Pneumonia, ventilator-associated pneumonia, VAP, healthcare-associated pneumonia
            Acute Respiratory Distress Syndrome | ARDS | Respiratory Distress, adult respiratory distress, ARDS, acute lung injury, ALI
            Pulmonary Tuberculosis | TB | Tuberculosis, TB, lung TB, mycobacterium tuberculosis, Koch's disease

            # ── Renal ─────────────────────────────────────────────────────────────────────
            Chronic Kidney Disease | CKD | Renal Insufficiency, kidney failure, chronic renal failure, CRF, renal impairment
            Acute Kidney Injury | AKI | Acute Renal Failure, ARF, acute kidney failure, renal failure, acute renal insufficiency
            End Stage Renal Disease | ESRD | Kidney Failure, terminal renal failure, end-stage kidney disease, renal replacement therapy needed
            Glomerulonephritis | GN | Kidney Inflammation, glomerular nephritis, nephritic syndrome, kidney glomerular disease
            Nephrotic Syndrome | NS | Protein Loss Kidney Disease, heavy proteinuria, nephrosis, protein-losing nephropathy

            # ── Hepatic & GI ─────────────────────────────────────────────────────────────
            Gastroesophageal Reflux Disease | GERD | Acid Reflux, heartburn, acid regurgitation, esophageal reflux, GERD
            Inflammatory Bowel Disease | IBD | Crohn's Disease, IBD, intestinal inflammation, chronic bowel disease
            Ulcerative Colitis | UC | Colitis, chronic ulcerative colitis, pancolitis, inflammatory colitis
            Irritable Bowel Syndrome | IBS | Spastic Colon, functional bowel disorder, IBS, nervous bowel, functional colitis
            Chronic Liver Disease | CLD | Liver Cirrhosis, hepatic cirrhosis, chronic hepatic disease, liver fibrosis
            Non-Alcoholic Fatty Liver Disease | NAFLD | Fatty Liver, hepatic steatosis, non-alcoholic steatohepatitis, NASH
            Hepatitis C | HCV | Hepatitis C Virus, HCV infection, viral hepatitis C, chronic hepatitis C
            Hepatitis B | HBV | Hepatitis B Virus, HBV infection, viral hepatitis B, chronic hepatitis B

            # ── Infectious & Immunological ────────────────────────────────────────────────
            Human Immunodeficiency Virus | HIV | HIV Infection, HIV positive, retroviral infection, AIDs virus
            Acquired Immunodeficiency Syndrome | AIDS | AIDS, stage 3 HIV, advanced HIV disease, HIV/AIDS
            Systemic Lupus Erythematosus | SLE | Lupus, SLE, systemic lupus, autoimmune lupus, disseminated lupus
            Rheumatic Fever | RF | Acute Rheumatic Fever, streptococcal rheumatic disease, rheumatic heart disease precursor
            Rheumatoid Arthritis | RA | Autoimmune Arthritis, inflammatory arthritis, RA, erosive arthritis, polyarthritis
            Urinary Tract Infection | UTI | Bladder Infection, urinary infection, cystitis, urinary bacterial infection

            # ── Musculoskeletal ───────────────────────────────────────────────────────────
            Osteoarthritis | OA | Degenerative Joint Disease, joint wear, articular degeneration, osteoarthrosis, wear-and-tear arthritis

            # ── Neurological ──────────────────────────────────────────────────────────────
            Stroke | CVA | Cerebrovascular Accident, brain stroke, brain infarction, ischemic stroke, hemorrhagic stroke, CVA
            Multiple Sclerosis | MS | Demyelinating Disease, MS, autoimmune demyelination, relapsing-remitting MS, RRMS
            Amyotrophic Lateral Sclerosis | ALS | Lou Gehrig's Disease, motor neuron disease, ALS, lateral sclerosis
            Parkinson's Disease | PD | Parkinsonism, neurodegenerative movement disorder, Parkinson, resting tremor disease
            Alzheimer's Disease | AD | Dementia, senile dementia, neurodegenerative dementia, memory loss disease, AD

            # ── Psychiatric & Psychological ───────────────────────────────────────────────
            Depression | Depression | Major Depressive Disorder, clinical depression, depressive disorder, MDD, unipolar depression
            Anxiety | Anxiety | Generalized Anxiety Disorder, anxiety disorder, panic disorder, anxious disorder, nervousness
            Post-Traumatic Stress Disorder | PTSD | Trauma Disorder, PTSD, post-trauma stress, combat stress disorder
            Attention Deficit Hyperactivity Disorder | ADHD | Hyperactivity Disorder, ADHD, attention deficit, ADD, hyperkinetic disorder
            Bipolar Disorder | BD | Manic Depression, bipolar, manic-depressive disorder, BD, mood cycling disorder
            Schizophrenia | Schizophrenia | psychotic disorder, schizoaffective disorder, paranoid schizophrenia, psychosis
            Major Depressive Disorder | MDD | Clinical Depression, unipolar depression, severe depression, MDD
            Generalized Anxiety Disorder | GAD | Chronic Anxiety, GAD, generalized anxiety, persistent anxiety disorder
            Obsessive Compulsive Disorder | OCD | OCD, obsessional neurosis, obsessive disorder, compulsive disorder
            Seasonal Affective Disorder | SAD | Winter Depression, SAD, seasonal depression, winter blues
            Eating Disorder | ED | anorexia, bulimia, food disorder, disordered eating
            Anorexia Nervosa | AN | anorexia, restrictive eating disorder, self-starvation, extreme food restriction
            Bulimia Nervosa | BN | bulimia, binge-purge disorder, purging disorder, binge eating with purging

            # ── Urological ────────────────────────────────────────────────────────────────
            Benign Prostatic Hyperplasia | BPH | Enlarged Prostate, BPH, prostate enlargement, benign prostate hypertrophy
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
