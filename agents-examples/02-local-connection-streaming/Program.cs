using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

// Configuration Constants
const string LmStudioEndpoint = "http://localhost:1234/v1";
const string ModelId = "mistralai/mistral-nemo-instruct-2407";

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("=== Medical Agent (Streaming) (MAF + LM Studio) ===");
Console.ResetColor();
Console.WriteLine($"Connecting to: {LmStudioEndpoint}");
Console.WriteLine($"Using model: {ModelId}\n");

try
{
    // 1. Configure OpenAI client to point to LM Studio
    var client = new OpenAIClient(
        new ApiKeyCredential("lm-studio"),
        new OpenAIClientOptions { Endpoint = new Uri(LmStudioEndpoint) }
    );

    // 2. Convert OpenAI client to IChatClient (required by Agent Framework)
    var openAiChatClient = client.GetChatClient(ModelId);
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

    Console.WriteLine("System ready (Streaming enabled). Type 'exit' to quit.\n");

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
            Console.WriteLine($"\nError during streaming: {ex.Message}");
            Console.ResetColor();
        }
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\nCRITICAL ERROR: Could not initialize the agent.");
    Console.WriteLine($"Make sure LM Studio is running at {LmStudioEndpoint}");
    Console.WriteLine($"Details: {ex.Message}");
    Console.ResetColor();
}
