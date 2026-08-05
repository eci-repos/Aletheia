using System.Text.Json;
using Aletheia.RAGS.Abstractions.Models;
using Microsoft.JSInterop;

namespace Aletheia.Web.Services;

public sealed class CopilotStateService
{
    private const string StorageKey = "aletheia.copilot.session.v1";
    private const double DefaultPanelWidth = 360;
    private const double MinPanelWidth = 280;
    private const double MaxPanelWidth = 720;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime _jsRuntime;
    private bool _restored;

    public CopilotStateService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
    }

    public CopilotClientState State { get; private set; } = new();

    private const string RecentSessionsKey = "aletheia.copilot.recentSessions.v1";
    private const int MaxRecentSessions = 10;
    private const int MaxStoredMessagesPerSession = 40;

    /// <summary>Raised when the user opens a past conversation from the Chats panel.</summary>
    public event Action<ChatSession>? SessionOpened;

    /// <summary>Raised when the recent-session list changes.</summary>
    public event Action? RecentSessionsChanged;

    public IReadOnlyList<ChatSession> RecentSessions { get; private set; } = new List<ChatSession>();

    public async Task<IReadOnlyList<ChatSession>> LoadRecentSessionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", cancellationToken, RecentSessionsKey).ConfigureAwait(false);
            RecentSessions = DeserializeRecent(json);
        }
        catch
        {
            RecentSessions = new List<ChatSession>();
        }

        return RecentSessions;
    }

    public async Task SaveCurrentSessionToHistoryAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        if (session is null || session.Messages.Count == 0)
        {
            return;
        }

        var stored = new ChatSession
        {
            Id = session.Id,
            Title = string.IsNullOrWhiteSpace(session.Title) ? "New Chat" : session.Title,
            CreatedAt = session.CreatedAt,
            LastActivity = session.LastActivity
        };
        stored.Messages.AddRange(session.Messages.TakeLast(MaxStoredMessagesPerSession));

        var list = new List<ChatSession>(RecentSessions);
        list.RemoveAll(s => s.Id == stored.Id);
        list.Insert(0, stored);
        if (list.Count > MaxRecentSessions)
        {
            list.RemoveRange(MaxRecentSessions, list.Count - MaxRecentSessions);
        }

        RecentSessions = list;
        RecentSessionsChanged?.Invoke();

        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                cancellationToken,
                RecentSessionsKey,
                JsonSerializer.Serialize(RecentSessions, JsonOptions)).ConfigureAwait(false);
        }
        catch
        {
            // History persistence is best-effort.
        }
    }

    public async Task RemoveRecentSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var list = new List<ChatSession>(RecentSessions);
        list.RemoveAll(s => s.Id == sessionId);
        RecentSessions = list;
        RecentSessionsChanged?.Invoke();

        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                cancellationToken,
                RecentSessionsKey,
                JsonSerializer.Serialize(RecentSessions, JsonOptions)).ConfigureAwait(false);
        }
        catch
        {
            // History persistence is best-effort.
        }
    }

    public async Task ClearRecentSessionsAsync(CancellationToken cancellationToken = default)
    {
        RecentSessions = new List<ChatSession>();
        RecentSessionsChanged?.Invoke();

        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.removeItem",
                cancellationToken,
                RecentSessionsKey).ConfigureAwait(false);
        }
        catch
        {
            // History persistence is best-effort.
        }
    }

    public void OpenSession(ChatSession session)
    {
        SessionOpened?.Invoke(session);
    }

    private static IReadOnlyList<ChatSession> DeserializeRecent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ChatSession>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ChatSession>>(json, JsonOptions) ?? new List<ChatSession>();
        }
        catch
        {
            return new List<ChatSession>();
        }
    }

    public async Task<CopilotClientState> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_restored)
        {
            return Clone(State);
        }

        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey).ConfigureAwait(false);
            State = Deserialize(json);
        }
        catch
        {
            State = new CopilotClientState();
        }

        _restored = true;
        return Clone(State);
    }

    public async Task SaveAsync(CopilotClientState state, CancellationToken cancellationToken = default)
    {
        State = Clone(state);
        _restored = true;

        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.setItem",
                cancellationToken,
                StorageKey,
                JsonSerializer.Serialize(State, JsonOptions)).ConfigureAwait(false);
        }
        catch
        {
            // Copilot state is convenience state. Navigation within the current Web session still uses memory.
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        State = new CopilotClientState();
        _restored = true;

        try
        {
            await _jsRuntime.InvokeVoidAsync(
                "localStorage.removeItem",
                cancellationToken,
                StorageKey).ConfigureAwait(false);
        }
        catch
        {
            // Clearing state is best-effort. The in-memory state is already reset.
        }
    }

    public static CopilotClientState Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new CopilotClientState();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<CopilotClientState>(json, JsonOptions));
        }
        catch
        {
            return new CopilotClientState();
        }
    }

    public static CopilotClientState Clone(CopilotClientState? state)
    {
        return Normalize(state);
    }

    private static CopilotClientState Normalize(CopilotClientState? state)
    {
        if (state is null)
        {
            return new CopilotClientState();
        }

        return new CopilotClientState
        {
            Session = CloneSession(state.Session),
            UserInput = state.UserInput ?? string.Empty,
            OutputFormat = string.IsNullOrWhiteSpace(state.OutputFormat) ? "auto" : state.OutputFormat,
            PendingPlan = state.PendingPlan,
            Progress = state.Progress,
            Telemetry = state.Telemetry,
            ActiveJobId = state.ActiveJobId,
            IsBusy = state.IsBusy,
            IsCancelling = state.IsCancelling,
            PlanStatusMessage = state.PlanStatusMessage ?? string.Empty,
            ChatError = state.ChatError ?? string.Empty,
            ExecutionPanelCollapsed = state.ExecutionPanelCollapsed,
            ExecutionPanelWidth = ClampPanelWidth(state.ExecutionPanelWidth)
        };
    }

    private static ChatSession CloneSession(ChatSession? session)
    {
        if (session is null)
        {
            return new ChatSession();
        }

        return new ChatSession
        {
            Id = session.Id,
            Title = string.IsNullOrWhiteSpace(session.Title) ? "New Chat" : session.Title,
            CreatedAt = session.CreatedAt,
            LastActivity = session.LastActivity,
            Messages = session.Messages
                .Select(message => new ChatMessage
                {
                    Id = message.Id,
                    Role = message.Role,
                    Content = message.Content,
                    Timestamp = message.Timestamp,
                    Stats = message.Stats
                })
                .ToList()
        };
    }

    public static double ClampPanelWidth(double width)
    {
        if (double.IsNaN(width) || width <= 0)
        {
            return DefaultPanelWidth;
        }

        return Math.Clamp(width, MinPanelWidth, MaxPanelWidth);
    }
}

public sealed class CopilotClientState
{
    public ChatSession Session { get; set; } = new();

    public string UserInput { get; set; } = string.Empty;

    public string OutputFormat { get; set; } = "auto";

    public ChatPlanRecord? PendingPlan { get; set; }

    public ChatProgressRecord? Progress { get; set; }

    public ChatExecutionTelemetry? Telemetry { get; set; }

    public Guid? ActiveJobId { get; set; }

    public bool IsBusy { get; set; }

    public bool IsCancelling { get; set; }

    public string PlanStatusMessage { get; set; } = string.Empty;

    public string ChatError { get; set; } = string.Empty;

    public bool ExecutionPanelCollapsed { get; set; }

    public double ExecutionPanelWidth { get; set; } = 360;
}
