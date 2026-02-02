using System.ClientModel;
using _04_multi_agent_system;
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

    // 5. Initialize the Group Chat (Orchestrator)
    AgentGroupChat groupChat = new(medicalSpecialist, medicalAdmin);

    Console.WriteLine("System ready. Enter patient notes (Type 'exit' to quit):");

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("\nInput: ");
        Console.ResetColor();

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        // 6. Execute the collaborative workflow
        // Each agent will take turns until the secretary decides to use the tool
        await foreach (var message in groupChat.RunAsync(input))
        {
            Console.WriteLine($"\n--- [{message.AuthorName}] ---");
            Console.WriteLine(message.Text);
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
    Console.ResetColor();
}
