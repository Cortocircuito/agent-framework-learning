# Agent Framework Learning - Medical Agents Examples

Learning repository using Microsoft Agent Framework with LM Studio and Llama 3.2 model. This project demonstrates how to build AI agents using the latest Microsoft AI abstractions.

## 📂 Projects Overview

| Project | Description | Key Feature |
|---------|-------------|-------------|
| **[01-local-connection](file:///d:/dev/aprendizaje-agent-framework/agents-examples/01-local-connection)** | Basic agent connection | Thread management & History |
| **[02-local-connection-streaming](file:///d:/dev/aprendizaje-agent-framework/agents-examples/02-local-connection-streaming)** | Streaming responses | Real-time text generation |
| **[03-agent-with-tools](file:///d:/dev/aprendizaje-agent-framework/agents-examples/03-agent-with-tools)** | Agent with Function Calling | External tool integration |
| **[04-multi-agent-system](file:///d:/dev/aprendizaje-agent-framework/agents-examples/04-multi-agent-system)** | Multi-agent collaboration | Sequential pipeline orchestration |
| **[05-multi-agent-system-advance](file:///d:/dev/aprendizaje-agent-framework/agents-examples/05-multi-agent-system-advance)** | Advanced multi-agent system | Round-robin with streaming |
| **[06-multi-agent-with-memory](file:///d:/dev/aprendizaje-agent-framework/agents-examples/06-multi-agent-with-memory)** | Multi-agent with persistence | Shared conversation memory |

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
- **MedicalReportExporter.cs** (in Projects 04-06): PDF export tool for medical reports.

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
- **Tool Integration**: Specific agents can be equipped with tools (e.g., PDF export)
- **Termination Logic**: Conversations end when tools are invoked or termination keywords detected

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
