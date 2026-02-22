using System.ClientModel;
using _10_medical_agent_api.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace _10_medical_agent_api.Services;

/// <summary>
/// Factory that creates <see cref="CoordinatedAgentGroupChat"/> instances for new sessions.
///
/// Design decisions:
/// - A single <see cref="IChatClient"/> is created once and shared across all sessions.
///   The underlying HTTP client is thread-safe; sharing it avoids connection-pool exhaustion.
/// - Each call to <see cref="CreateGroupChat"/> produces fresh <see cref="AIAgent"/> instances
///   and an empty conversation thread, giving every session full isolation.
/// - Domain services (SemanticMedicalSearch, PatientRegistry, MedicalReportExporter) are
///   injected as singletons and shared across sessions safely.
/// </summary>
public class AgentFactory
{
    private readonly IChatClient _chatClient;
    private readonly SemanticMedicalSearch _semanticSearch;
    private readonly PatientRegistry _patientRegistry;
    private readonly MedicalReportExporter _reportExporter;

    public AgentFactory(
        SemanticMedicalSearch semanticSearch,
        PatientRegistry patientRegistry,
        MedicalReportExporter reportExporter,
        string lmStudioEndpoint,
        string modelId)
    {
        _chatClient = new ChatClientBuilder(
                new OpenAIClient(
                        new ApiKeyCredential("lm-studio"),
                        new OpenAIClientOptions { Endpoint = new Uri(lmStudioEndpoint) })
                    .GetChatClient(modelId)
                    .AsIChatClient())
            .UseFunctionInvocation()
            .Build();

        _semanticSearch  = semanticSearch;
        _patientRegistry = patientRegistry;
        _reportExporter  = reportExporter;
    }

    /// <summary>
    /// Creates a new <see cref="CoordinatedAgentGroupChat"/> with its own agent instances
    /// and an empty conversation thread.
    /// </summary>
    public CoordinatedAgentGroupChat CreateGroupChat()
    {
        AIAgent coordinator = _chatClient.CreateAIAgent(
            name: "MedicalCoordinator",
            instructions: AgentInstructions.Coordinator);

        AIAgent clinicalExtractor = _chatClient.CreateAIAgent(
            name: "ClinicalDataExtractor",
            instructions: AgentInstructions.ClinicalDataExtractor,
            tools: [AIFunctionFactory.Create(_semanticSearch.SearchMedicalKnowledge)]);

        AIAgent secretary = _chatClient.CreateAIAgent(
            name: "MedicalSecretary",
            instructions: AgentInstructions.MedicalSecretary,
            tools:
            [
                AIFunctionFactory.Create(_patientRegistry.GetPatientData),
                AIFunctionFactory.Create(_patientRegistry.UpsertPatientRecord),
                AIFunctionFactory.Create(_reportExporter.SaveReportToPdf)
            ]);

        return new CoordinatedAgentGroupChat(
            coordinator: coordinator,
            specialists: new Dictionary<string, AIAgent>
            {
                { "ClinicalDataExtractor", clinicalExtractor },
                { "MedicalSecretary",      secretary }
            },
            maxTurns: 20);
    }
}
