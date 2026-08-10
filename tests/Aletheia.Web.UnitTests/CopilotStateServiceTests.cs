using System.Text.Json;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Web.Services;
using Microsoft.JSInterop;

namespace Aletheia.Web.UnitTests;

public class CopilotStateServiceTests
{
    [Fact]
    public void Deserialize_restores_session_plan_progress_and_layout()
    {
        var planId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var jobId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var state = new CopilotClientState
        {
            Session = new ChatSession
            {
                Title = "RFP summary",
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Summarize registered RFPs" },
                    new() { Role = "assistant", Content = "Two RFPs found." }
                }
            },
            UserInput = "draft follow-up",
            OutputFormat = "table",
            PendingPlan = new ChatPlanRecord
            {
                PlanId = planId,
                Prompt = "Summarize registered RFPs",
                Mode = ChatExecutionMode.CorpusAnalysis,
                EstimatedSecondsMin = 20,
                EstimatedSecondsMax = 120
            },
            Progress = new ChatProgressRecord
            {
                JobId = jobId,
                PlanId = planId,
                Status = ChatJobStatus.Running,
                PercentComplete = 35
            },
            Telemetry = new ChatExecutionTelemetry
            {
                JobId = jobId,
                PlanId = planId,
                RetrievalCount = 2,
                CitationCount = 2
            },
            ActiveJobId = jobId,
            PlanStatusMessage = "Review the plan.",
            ExecutionPanelCollapsed = true,
            ExecutionPanelWidth = 480
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var restored = CopilotStateService.Deserialize(json);

        Assert.Equal("RFP summary", restored.Session.Title);
        Assert.Equal(2, restored.Session.Messages.Count);
        Assert.Equal("draft follow-up", restored.UserInput);
        Assert.Equal("table", restored.OutputFormat);
        Assert.Equal(planId, restored.PendingPlan?.PlanId);
        Assert.Equal(jobId, restored.Progress?.JobId);
        Assert.Equal(2, restored.Telemetry?.RetrievalCount);
        Assert.Equal(jobId, restored.ActiveJobId);
        Assert.True(restored.ExecutionPanelCollapsed);
        Assert.Equal(480, restored.ExecutionPanelWidth);
    }

    [Fact]
    public void Deserialize_returns_default_state_for_invalid_json()
    {
        var restored = CopilotStateService.Deserialize("{not valid");

        Assert.NotNull(restored.Session);
        Assert.Empty(restored.Session.Messages);
        Assert.Equal("auto", restored.OutputFormat);
        Assert.Equal(360, restored.ExecutionPanelWidth);
    }

    [Theory]
    [InlineData(0, 360)]
    [InlineData(120, 280)]
    [InlineData(500, 500)]
    [InlineData(900, 720)]
    public void ClampPanelWidth_enforces_operator_bounds(double input, double expected)
    {
        Assert.Equal(expected, CopilotStateService.ClampPanelWidth(input));
    }

    [Fact]
    public async Task ClearAsync_resets_memory_state_and_removes_browser_state()
    {
        var jsRuntime = new CapturingJsRuntime();
        var service = new CopilotStateService(jsRuntime);
        await service.SaveAsync(new CopilotClientState
        {
            Session = new ChatSession
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "hello" }
                }
            },
            UserInput = "draft",
            PendingPlan = new ChatPlanRecord { PlanId = Guid.NewGuid(), Prompt = "hello" },
            ActiveJobId = Guid.NewGuid(),
            ExecutionPanelCollapsed = true,
            ExecutionPanelWidth = 500
        });

        await service.ClearAsync();

        Assert.Empty(service.State.Session.Messages);
        Assert.Equal("auto", service.State.OutputFormat);
        Assert.False(service.State.ActiveJobId.HasValue);
        Assert.False(service.State.ExecutionPanelCollapsed);
        Assert.Equal("aletheia.copilot.session.v2", jsRuntime.RemovedKey);
    }

    [Fact]
    public async Task SaveCurrentSessionToHistoryAsync_persists_session_and_caps_at_ten()
    {
        var jsRuntime = new StoringJsRuntime();
        var service = new CopilotStateService(jsRuntime);

        for (var i = 0; i < 12; i++)
        {
            var session = new ChatSession
            {
                Id = Guid.NewGuid(),
                Title = $"Chat {i}",
                Messages = new List<ChatMessage> { new() { Role = "user", Content = $"message {i}" } }
            };
            await service.SaveCurrentSessionToHistoryAsync(session);
        }

        Assert.Equal(10, service.RecentSessions.Count);
        Assert.Equal("Chat 11", service.RecentSessions[0].Title);

        var restored = await service.LoadRecentSessionsAsync();
        Assert.Equal(10, restored.Count);
    }

    [Fact]
    public async Task RemoveRecentSessionAsync_and_ClearRecentSessionsAsync_manage_history()
    {
        var jsRuntime = new StoringJsRuntime();
        var service = new CopilotStateService(jsRuntime);
        var first = new ChatSession
        {
            Id = Guid.NewGuid(),
            Title = "First",
            Messages = new List<ChatMessage> { new() { Role = "user", Content = "a" } }
        };
        var second = new ChatSession
        {
            Id = Guid.NewGuid(),
            Title = "Second",
            Messages = new List<ChatMessage> { new() { Role = "user", Content = "b" } }
        };
        await service.SaveCurrentSessionToHistoryAsync(first);
        await service.SaveCurrentSessionToHistoryAsync(second);
        Assert.Equal(2, service.RecentSessions.Count);

        await service.RemoveRecentSessionAsync(first.Id);
        Assert.Single(service.RecentSessions);
        Assert.Equal(second.Id, service.RecentSessions[0].Id);

        await service.ClearRecentSessionsAsync();
        Assert.Empty(service.RecentSessions);
        Assert.Equal("aletheia.copilot.recentSessions.v1", jsRuntime.RemovedKey);
    }

    [Fact]
    public void OpenSession_raises_event_with_session()
    {
        var jsRuntime = new StoringJsRuntime();
        var service = new CopilotStateService(jsRuntime);
        ChatSession? opened = null;
        service.SessionOpened += session => opened = session;
        var session = new ChatSession { Title = "T" };

        service.OpenSession(session);

        Assert.Same(session, opened);
    }

    private sealed class StoringJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _store = new();

        public string? RemovedKey { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "localStorage.setItem" && args is { Length: >= 2 })
            {
                _store[args[0]!.ToString()!] = args[1]!.ToString()!;
            }
            else if (identifier == "localStorage.removeItem" && args is { Length: >= 1 })
            {
                RemovedKey = args[0]?.ToString();
                _store.Remove(RemovedKey!);
            }
            else if (identifier == "localStorage.getItem" && args is { Length: >= 1 } && _store.TryGetValue(args[0]!.ToString()!, out var value))
            {
                return ValueTask.FromResult((TValue)(object)value);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class CapturingJsRuntime : IJSRuntime
    {
        public string? RemovedKey { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "localStorage.removeItem")
            {
                RemovedKey = args?.FirstOrDefault()?.ToString();
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
