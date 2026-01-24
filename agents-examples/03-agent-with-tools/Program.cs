using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using OpenAI;
using System.ClientModel;
using _03_agent_with_tools;

// 1. Base client
var client = new OpenAIClient(
    new ApiKeyCredential("lm-studio"),
    new OpenAIClientOptions { Endpoint = new Uri("http://localhost:1234/v1") });

// 2. KEY CONFIGURATION: Add "Function Invocation"
// This allows the client to understand it can call C# methods
var openAiChatClient = client.GetChatClient("openai/gpt-oss-20b");
var chatClient = new ChatClientBuilder(openAiChatClient.AsIChatClient())
    .UseFunctionInvocation()
    .Build();

// 3. Create the Agent passing the instance of our tools
var medicalTools = new MedicalTools();
AIAgent medicalAgent = chatClient.CreateAIAgent(
    name: "MedicalAssistant",
    instructions: """
                  You are a medical assistant. You have access to a tool to search for patient histories.
                  If the user asks you about a patient, use the 'GetPatientHistory' tool.
                  Use the information obtained to write the report.
                  """,
    tools:
    [
        AIFunctionFactory.Create(medicalTools.GetPatientHistory)
    ] // <--- Register the tool
);

AgentThread thread = medicalAgent.GetNewThread();

Console.WriteLine("=== Medical Agent with Tools (MAF + LM Studio) ===");
Console.WriteLine("Type 'exit' to quit\n");

while (true)
{
    Console.Write("You: ");
    var userInput = Console.ReadLine();

    if (String.IsNullOrWhiteSpace(userInput) ||
        userInput.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    try
    {
        // Send message to agent and get streaming response (thread history is sent automatically)
        Console.Write("\nAssistant: ");
        var responseStream = medicalAgent.RunStreamingAsync(userInput, thread);
        await foreach (var update in responseStream)
        {
            Console.Write(update.Text);
        }

        Console.WriteLine("\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}
