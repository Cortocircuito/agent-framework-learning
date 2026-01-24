# Agent Framework Learning - Medical Assistant Examples

Learning repository using Microsoft Agent Framework with LM Studio and Llama 3.2 model. This project demonstrates how to build AI agents using the latest Microsoft AI abstractions.

## 📂 Projects Overview

| Project | Description | Key Feature |
|---------|-------------|-------------|
| **[01-local-connection](file:///d:/dev/aprendizaje-agent-framework/agents-examples/01-local-connection)** | Basic agent connection | Thread management & History |
| **[02-local-connection-streaming](file:///d:/dev/aprendizaje-agent-framework/agents-examples/02-local-connection-streaming)** | Streaming responses | Real-time text generation |
| **[03-agent-with-tools](file:///d:/dev/aprendizaje-agent-framework/agents-examples/03-agent-with-tools)** | Agent with Function Calling | External tool integration |

## 🎯 What You'll Learn

- How to configure an OpenAI client to point to LM Studio
- Converting OpenAI clients to `IChatClient` for Agent Framework
- Creating an AI Agent with custom instructions (personality)
- Managing conversation threads with automatic history
- Building an interactive console chat loop with streaming support
- Implementing **Function Calling** (Tools) to extend agent capabilities

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

## 🔧 Key Concepts

### AIAgent
Represents an intelligent agent with:
- **Name**: Identifier for the agent
- **Instructions**: System prompt that defines behavior.

### AgentThread
Maintains conversation history automatically without manual `List<ChatMessage>` management.

### IChatClient
Abstraction from `Microsoft.Extensions.AI` that allows switching between LLM providers (OpenAI, Azure, LM Studio, Ollama).

## 🎓 Sample Interaction
```text
=== Medical Agent (MAF + LM Studio) ===
Type 'exit' to quit

You: Patient shows symptoms of hypertension
Assistant: I'll help structure that information. Could you provide the specific blood pressure readings?
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
