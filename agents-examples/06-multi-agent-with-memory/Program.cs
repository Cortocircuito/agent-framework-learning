using System.ClientModel;
using _06_multi_agent_with_memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

// Configuration Constants
const string lmStudioEndpoint = "http://localhost:1234/v1";
const string modelId = "qwen2.5-7b-instruct";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Multi-Agent Medical System with PDF Export ===");
Console.ResetColor();

try
{
    // 1. Setup the local LLM Client (LM Studio)
    var client = new OpenAIClient(
        new ApiKeyCredential("lm-studio"),
        new OpenAIClientOptions { Endpoint = new Uri(lmStudioEndpoint) });

    // 2. Build the ChatClient with Function Invocation enabled
    // This allows the system to bridge the LLM with your C# code
    var openAiChatClient = client.GetChatClient(modelId);
    var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    // 3. Instantiate our local tools
    var exporter = new MedicalReportExporter();

    // 4. Create the Specialized Agents

    // The Clinical Specialist: Focuses on technical medical data extraction
    AIAgent medicalSpecialist = chatClient.CreateAIAgent(
        name: "DrHouse",
        instructions: """
                      You are a senior medical specialist. 
                      Your only task is to extract diagnoses, symptoms, and treatments from messy clinical notes.
                      Always provide a technical summary focused on the medical facts.
                      """
    );

    // The Administrative Assistant: Focuses on formatting and EXPORTING the file
    AIAgent medicalAdmin = chatClient.CreateAIAgent(
        name: "MedicalSecretary",
        instructions: """
                      You are a hospital administrator.
                      Take the information from DrHouse and format it into a professional report.

                      IMPORTANT INSTRUCTIONS:
                      1. Extract the patient's full name from the conversation (e.g., "Juan Palomo", "Herbert Heartstone").
                      2. Once the report is ready, call the 'SaveReportToPdf' tool EXACTLY ONCE.
                      3. Pass the patient's actual name to the tool. If no name was mentioned, use "Unknown_Patient".
                      4. After saving, inform the user that the file has been created.
                      5. Do NOT call the tool multiple times for the same report.
                      """,
        tools: [AIFunctionFactory.Create(exporter.SaveReportToPdf)] // Registering the tool
    );

    const string historyFile = "chat_history.json";

    // 5. Initialize the Group Chat
    AgentGroupChat groupChat = new(medicalSpecialist, medicalAdmin);

    // NEW: Load existing history if file exists
    if (File.Exists(historyFile))
    {
        Console.WriteLine("--- Loading previous session history... ---");
        string savedJson = File.ReadAllText(historyFile);
        groupChat.LoadHistory(savedJson, medicalSpecialist);
    }

    Console.WriteLine("System ready. Enter patient notes (Type 'exit' to quit):");

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\nInput: ");
        Console.ResetColor();

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            // Save history before exiting
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

            break;
        }

        // 6. Execute the collaborative workflow
        // Agents take turns in round-robin fashion until task completion
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
                    _ => ConsoleColor.Yellow
                };

                Console.WriteLine($"\n--- [{message.AuthorName}] ---");
                Console.ResetColor();
                currentAgent = message.AuthorName;
            }

            // Display message content
            if (message.isStreaming)
            {
                // Stream tokens in real-time
                Console.Write(message.Text);
            }
            else if (message.isComplete)
            {
                // Complete message already shown via streaming, just add newline
                Console.WriteLine();
            }
            else
            {
                // Non-streaming message (e.g., User input)
                Console.WriteLine(message.Text);
            }
        }

        Console.WriteLine(); // Final spacing

        // Save history after each conversation completes
        try
        {
            var jsonToSave = groupChat.ExportHistory();
            File.WriteAllText(historyFile, jsonToSave);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not save history: {ex.Message}");
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
    Console.ResetColor();
}
