using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenAgent.Model.Contracts;

namespace OpenAgent.Model;

public class MockModelProvider : IModelProvider
{
    private readonly Queue<ChatCompletionResponse> _responses = new();
    public int CallCount { get; private set; }
    public TimeSpan DelayPerCall { get; set; } = TimeSpan.Zero;
    public List<ChatCompletionRequest> RecordedRequests { get; } = [];

    public void EnqueueResponse(ChatCompletionResponse response)
    {
        _responses.Enqueue(response);
    }

    public void EnqueueAssistantReply(string replyText)
    {
        _responses.Enqueue(new ChatCompletionResponse
        {
            Choices = [
                new ChatChoice
                {
                    Index = 0,
                    Message = ChatMessage.Assistant(replyText),
                    FinishReason = "stop"
                }
            ]
        });
    }

    public void EnqueueToolCall(string toolName, string argumentsJson, string callId = "call_mock")
    {
        _responses.Enqueue(new ChatCompletionResponse
        {
            Choices = [
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        ToolCalls = [
                            new ToolCall
                            {
                                Id = callId,
                                Type = "function",
                                Function = new FunctionCall
                                {
                                    Name = toolName,
                                    Arguments = argumentsJson
                                }
                            }
                        ]
                    },
                    FinishReason = "tool_calls"
                }
            ]
        });
    }

    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        RecordedRequests.Add(request);

        if (DelayPerCall > TimeSpan.Zero)
        {
            await Task.Delay(DelayPerCall, cancellationToken).ConfigureAwait(false);
        }

        if (_responses.Count > 0)
        {
            return _responses.Dequeue();
        }

        return new ChatCompletionResponse
        {
            Choices = [
                new ChatChoice
                {
                    Index = 0,
                    Message = ChatMessage.Assistant("Risposta mock predefinita."),
                    FinishReason = "stop"
                }
            ]
        };
    }

    public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public void Reset()
    {
        _responses.Clear();
        RecordedRequests.Clear();
        CallCount = 0;
        DelayPerCall = TimeSpan.Zero;
    }
}
