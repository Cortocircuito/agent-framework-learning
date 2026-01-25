using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using OpenAI;
using System.ClientModel;
using _03_agent_with_tools;

// Configuration Constants
const string lmStudioEndpoint = "http://localhost:1234/v1";
const string modelId = "openai/gpt-oss-20b";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Medical Agent with Tools (MAF + LM Studio) ===");
Console.ResetColor();
Console.WriteLine($"Connecting to: {lmStudioEndpoint}");
Console.WriteLine($"Using model: {modelId}\n");

try
{
    // 1. Base client
    var client = new OpenAIClient(
        new ApiKeyCredential("lm-studio"),
        new OpenAIClientOptions { Endpoint = new Uri(lmStudioEndpoint) });

    // 2. KEY CONFIGURATION: Add "Function Invocation"
    // This allows the client to understand it can call C# methods
    var openAiChatClient = client.GetChatClient(modelId);
    var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
        .UseFunctionInvocation()
        .Build();

    // 3. Create the Agent passing the instance of our tools
    var medicalTools = new MedicalTools();
    AIAgent medicalAgent = chatClient.CreateAIAgent(
        name: "MedicalAssistant",
        instructions: """
                      You are a medical assistant expert in writing reports. 
                      You have access to a tool to search for patient histories.
                      If the user asks you about a patient (like 'Juan Perez' or 'Maria Garcia'), use the 'GetPatientHistory' tool.
                      Use the information obtained to write a professional summary.
                      """,
        tools:
        [
            AIFunctionFactory.Create(medicalTools.GetPatientHistory)
        ] // <--- Register the tool
    );

    AgentThread thread = medicalAgent.GetNewThread();

    Console.WriteLine("System ready (Tools enabled). Type 'exit' to quit.\n");

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("You: ");
        Console.ResetColor();
        
        var userInput = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(userInput) ||
            userInput.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

        try
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("\nAssistant: ");
            
            // Send message to agent and get streaming response (thread history is sent automatically)
            var responseStream = medicalAgent.RunStreamingAsync(userInput, thread);
            await foreach (var update in responseStream)
            {
                Console.Write(update.Text);
            }

            Console.WriteLine("\n");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError during execution: {ex.Message}");
            Console.ResetColor();
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: Could not initialize the agent.");
    Console.WriteLine($"Make sure LM Studio is running at {lmStudioEndpoint}");
    Console.WriteLine($"Details: {ex.Message}");
    Console.ResetColor();
}
