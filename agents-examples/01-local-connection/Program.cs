using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

// 1. Configure OpenAI client to point to LM Studio
var client = new OpenAIClient(
    new ApiKeyCredential("lm-studio"),
    new OpenAIClientOptions { Endpoint = new Uri("http://localhost:1234/v1") }
);

// 2. Convert OpenAI client to IChatClient (required by Agent Framework)
// The model ID must match exactly with the one loaded in LM Studio
var openAiChatClient = client.GetChatClient("lmstudio-community/Llama-3.2-3B-Instruct-GGUF");
var chatClient = openAiChatClient.AsIChatClient();

// 3. Create AI Agent with medical assistant personality
AIAgent medicalAgent = chatClient.CreateAIAgent(
    name: "MedicalAssistant",
    instructions: """
                  You are a medical assistant expert in writing weekly reports. 
                  Your goal is to help organize disorganized clinical notes into a structured format.
                  Be professional, concise, and ensure medical terminology is accurate.
                  """
);

// 4. Create conversation thread (maintains chat history automatically)
AgentThread thread = medicalAgent.GetNewThread();

Console.WriteLine("=== Medical Agent (MAF + LM Studio) ===");
Console.WriteLine("Type 'exit' to quit\n");

while (true)
{
    Console.Write("You: ");
    var userInput = Console.ReadLine();

    if (String.IsNullOrWhiteSpace(userInput) ||
        userInput.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    try
    {
        // Send message to agent and get response (thread history is sent automatically)
        var response = await medicalAgent.RunAsync(userInput, thread);

        Console.WriteLine($"\nAssistant: {response.Text}\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}