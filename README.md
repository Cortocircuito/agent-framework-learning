# Agent Framework Learning - Medical Agents Examples

Learning repository using Microsoft Agent Framework with LM Studio and Llama 3.2 model. This project demonstrates how to build AI agents using the latest Microsoft AI abstractions.

## 📂 Projects Overview

| Project | Description | Key Feature |
|---------|-------------|-------------|
| **[01-local-connection](agents-examples/01-local-connection)** | Basic agent connection | Thread management & History |
| **[02-local-connection-streaming](agents-examples/02-local-connection-streaming)** | Streaming responses | Real-time text generation |
| **[03-agent-with-tools](agents-examples/03-agent-with-tools)** | Agent with Function Calling | External tool integration |
| **[04-multi-agent-system](agents-examples/04-multi-agent-system)** | Multi-agent collaboration | Sequential pipeline orchestration |
| **[05-multi-agent-system-advance](agents-examples/05-multi-agent-system-advance)** | Advanced multi-agent system | Round-robin with streaming |
| **[06-multi-agent-with-memory](agents-examples/06-multi-agent-with-memory)** | Multi-agent with persistence | Shared conversation memory |
| **[07-shared-state-memory](agents-examples/07-shared-state-memory)** | Coordinator pattern with database | SQLite persistence & intelligent routing |
| **[08-medical-rag-system](agents-examples/08-medical-rag-system)** | RAG-based standardization | Local knowledge base & acronym normalization |
| **[09-medical-semantic-rag](agents-examples/09-medical-semantic-rag)** | Semantic RAG with vector embeddings | Cosine similarity & confidence thresholds |
| **[10-medical-agent-api](agents-examples/10-medical-agent-api)** | Medical Agent REST API | ASP.NET Core Minimal API + SSE streaming |

## 📘 Project Docs

- [Project 08 README](agents-examples/08-medical-rag-system/README.md)
- [Project 09 README](agents-examples/09-medical-semantic-rag/README.md)

## 🎯 What You'll Learn

- How to configure an OpenAI client to point to LM Studio
- Converting OpenAI clients to `IChatClient` for Agent Framework
- Creating an AI Agent with custom instructions (personality)
- Managing conversation threads with automatic history
- Building an interactive console chat loop with streaming support
- Implementing **Function Calling** (Tools) to extend agent capabilities
- **Multi-agent orchestration** with sequential and round-robin patterns
- **Collaborative workflows** where agents work together on complex tasks
- **Shared memory** and conversation persistence across agent interactions
- **Real-time streaming** in multi-agent scenarios
- **Coordinator pattern** for intelligent agent routing and task delegation
- **Database integration** with SQLite for persistent patient records
- **PDF report generation** from structured medical data
- **RAG (Retrieval-Augmented Generation)** for validating and standardizing inputs
- **Local Knowledge Base** integration without external vector DB dependencies
- **Semantic Search** with vector embeddings and cosine similarity for robust term matching
- **REST API integration** with ASP.NET Core Minimal API and Server-Sent Events (SSE) streaming

## 📋 Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [LM Studio](https://lmstudio.ai/) running on `http://localhost:1234`
- Model loaded in LM Studio: `lmstudio-community/Llama-3.2-3B-Instruct-GGUF` or any OpenAI-compatible model.

## 🚀 Running the Projects

1. **Start LM Studio** and ensure the local server is running on port 1234
2. **Load the model** (e.g., `Llama-3.2-3B-Instruct-GGUF`) in LM Studio
3. **Navigate to a project folder**, for example:
   ```bash
   cd agents-examples/01-local-connection
   ```
4. **Restore dependencies (if needed):**
   ```bash
   dotnet restore
   ```
5. **Run the project:**
   ```bash
   dotnet run
   ```
6. **Interact with the agent:**
   - Type your messages and press Enter
   - Use `exit` to quit

## 📦 NuGet Packages

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Agents.AI.OpenAI` | `1.0.0-preview.*` | Agent Framework for building AI agents |
| `Microsoft.Extensions.AI` | `10.*` | Core AI abstractions (`IChatClient`, etc.) |
| `OpenAI` | Latest | OpenAI SDK (used to connect to LM Studio) |

## 🏗️ Code Structure

- **Program.cs**: Main entry point containing:
	1. Client Configuration (OpenAI -> IChatClient)
	2. Agent Creation (Personality & Instructions)
	3. Tool Registration (if applicable)
	4. Conversation Loop (Interactive CLI)
- **MedicalTools.cs** (in Project 03): Contains the C# methods exposed as tools.
- **AgentGroupChat.cs** (in Projects 04-06): Orchestrator for multi-agent collaboration:
	- **Project 04**: Sequential pipeline pattern
	- **Project 05**: Round-robin with streaming support
	- **Project 06**: Round-robin with persistent memory
- **CoordinatedAgentGroupChat.cs** (in Project 07): Advanced coordinator-based orchestration:
	- Intelligent agent routing based on task analysis
	- Context-aware specialist selection
	- Multi-turn discussion support
- **PatientRegistry.cs** (in Project 07): SQLite database manager for patient records.
- **MedicalReportExporter.cs** (in Projects 04-07): PDF export tool for medical reports.
- **MedicalKnowledgeBase.cs** (in Project 08): RAG service for searching local medical acronyms.
- **SemanticMedicalSearch.cs** (in Projects 09-10): Vector-based semantic search engine using local embeddings.
- **Services/AgentFactory.cs** (in Project 10): Creates per-session agent instances sharing one `IChatClient`.
- **Services/SessionManager.cs** (in Project 10): Thread-safe in-memory session registry (ConcurrentDictionary).

## 🔧 Key Concepts

### AIAgent
Represents an intelligent agent with:
- **Name**: Identifier for the agent
- **Instructions**: System prompt that defines behavior.

### AgentThread
Maintains conversation history automatically without manual `List<ChatMessage>` management.

### IChatClient
Abstraction from `Microsoft.Extensions.AI` that allows switching between LLM providers (OpenAI, Azure, LM Studio, Ollama).

### AgentGroupChat
Orchestrator that coordinates multiple AI agents working together:
- **Sequential Pipeline**: Agents process tasks in order, each building on the previous agent's output
- **Round-Robin**: Agents take turns responding until task completion or tool invocation
- **Shared Memory**: All agents share the same conversation thread for context continuity

### Multi-Agent Patterns
- **Specialization**: Each agent has a specific role (e.g., Medical Specialist, Administrator)
- **Collaboration**: Agents work together on complex tasks requiring different expertise
- **Tool Integration**: Specific agents can be equipped with tools (e.g., PDF export, database access)
- **Termination Logic**: Conversations end when tools are invoked or termination keywords detected

### Coordinator Pattern (Project 07)
Intelligent orchestration that replaces fixed round-robin with adaptive routing:
- **Request Analysis**: Coordinator analyzes user input to determine required specialists
- **Dynamic Routing**: Only invokes necessary agents based on task complexity
- **Context Passing**: Each specialist builds on previous agent outputs
- **Synthesis**: Coordinator provides final recommendations after specialist consultation
- **Database Persistence**: Patient records stored in SQLite with full CRUD operations
- **Command System**: Specialized commands for different workflow modes (`/query`, `/document`, `/list`)

### RAG Standardization (Project 08)
Enhances the coordinator pattern by adding a verification layer:
- **Knowledge Base Lookup**: Checks `acronyms.txt` for medical terms
- **Hallucination Prevention**: Forces agents to strictly use grounded data
- **Standardization**: Converts free-text conditions into standardized acronyms (e.g., "High Blood Pressure" -> "HTA")

### REST API Integration (Project 10)
Exposes the entire multi-agent system as an ASP.NET Core Minimal API:
- **Minimal API**: Clean endpoint registration with `app.MapPost/Get/Delete`
- **Dependency Injection**: Singletons for shared services (embedder, registry, exporter) and a factory for per-session agents
- **Server-Sent Events (SSE)**: Streams agent responses token-by-token to HTTP clients
- **Session Management**: `ConcurrentDictionary`-based registry isolates each HTTP client's conversation history
- **OpenAPI**: Built-in `/openapi/v1.json` spec served without extra packages

## 🎓 Sample Interaction

### Single Agent (Projects 01-03)
```text
=== Medical Agent (MAF + LM Studio) ===
Type 'exit' to quit

You: Patient shows symptoms of hypertension
Assistant: I'll help structure that information. Could you provide the specific blood pressure readings?
```

### Multi-Agent System (Projects 04-06)
```text
=== Multi-Agent Medical System with PDF Export ===

Input: Patient John Doe, 45yo, BP 160/95, headaches, prescribed Lisinopril 10mg

--- [DrHouse] ---
Medical Analysis:
- Diagnosis: Stage 2 Hypertension
- Symptoms: Elevated BP (160/95 mmHg), chronic headaches
- Treatment: Lisinopril 10mg daily

--- [MedicalSecretary] ---
Generating professional report...
✓ Report saved to: medical_report_20260127.pdf
File successfully created and ready for review.
```

### Coordinator-Based System (Project 07)
```text
=== Coordinator-Based Multi-Agent Medical System ===

> /document Patient Maria Garcia, DOB 1985-03-15, Room 302, fever and cough

--- [MedicalCoordinator] ---
Based on this request, I will consult: ClinicalDataExtractor, MedicalSecretary
Approach: Extract clinical data, then document in database and generate report

--- [ClinicalDataExtractor] ---
Patient: Maria Garcia
Conditions: Fever, Cough
Date of Birth: 1985-03-15
Room Number: 302
Analysis complete.

--- [MedicalSecretary] ---
✓ Patient record updated in database
✓ Report saved to: medical_report_maria_garcia_20260214.pdf
TASK_COMPLETE: Report saved.

> /query Maria Garcia

--- [MedicalSecretary] ---
Patient: Maria Garcia
Conditions: Fever, Cough
Date of Birth: 1985-03-15
Room: 302


### RAG-Based System (Project 08)
```text
=== Medical RAG System ===

> /document Patient John Doe, History: Hypertension, Diabetes Type 2

--- [Coordinator] ---
Routing to ClinicalDataExtractor...

--- [ClinicalDataExtractor] ---
Searching Knowledge Base for 'Hypertension'... Found 'HTA'
Searching Knowledge Base for 'Diabetes Type 2'... Found 'DM2'

Analysis Complete:
Patient: John Doe
Medical History: HTA, DM2

--- [MedicalSecretary] ---
✓ Patient record standardized and saved.
```

### REST API System (Project 10)
```bash
# Start the API
cd agents-examples/10-medical-agent-api
dotnet run
# → API ready at https://localhost:5000

# Process clinical notes via curl (SSE stream)
curl -N -X POST https://localhost:5000/api/patients/document \
  -H "Content-Type: application/json" \
  -d '{"notes": "Patient Maria Garcia, 62F, Room 401. AP: Hypertension, DM2. Admitted for chest pain."}'

# Response (SSE stream):
data: {"type":"session","sessionId":"a1b2c3d4"}
data: {"type":"message","author":"MedicalCoordinator","text":"Routing to ClinicalDataExtractor and MedicalSecretary...","isStreaming":false,"isComplete":true}
data: {"type":"message","author":"ClinicalDataExtractor","text":"Patient: Maria Garcia\nRoom: 401...","isStreaming":true,"isComplete":false}
data: {"type":"message","author":"MedicalSecretary","text":"TASK_COMPLETE: Report saved.","isStreaming":false,"isComplete":true}
data: {"type":"done"}

# Query a patient (JSON response)
curl https://localhost:5000/api/patients/Maria%20Garcia

# List all sessions
curl https://localhost:5000/api/sessions
```

## 🐛 Troubleshooting

### Error: "Connection refused" or timeout
- ✅ Verify LM Studio is running and the server is started
- ✅ Check the endpoint is `http://localhost:1234/v1`

### Error: Model not found
- ✅ Verify the model ID in code matches exactly with the one loaded in LM Studio.

## 📖 Resources

- [Microsoft Agent Framework Docs](https://learn.microsoft.com/en-us/dotnet/ai/agent-framework)
- [Microsoft.Extensions.AI Docs](https://learn.microsoft.com/en-us/dotnet/ai/get-started)
- [LM Studio Documentation](https://lmstudio.ai/docs)
