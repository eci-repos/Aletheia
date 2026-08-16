using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.RAGS.Application.Planning;
using Aletheia.Repository.Abstractions.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace RAGS.UnitTests;

public class ChatPlanApprovalServiceTests
{
    [Fact]
    public async Task CreatePlanAsync_persists_proposed_plan_with_estimates()
    {
        var service = CreateService();

        var result = await service.CreatePlanAsync("summarize all RFPs in the repository");

        Assert.True(result.IsSuccess);
        var plan = result.Value!;
        Assert.NotEqual(Guid.Empty, plan.PlanId);
        Assert.Equal(ChatPlanStatus.Proposed, plan.Status);
        Assert.True(plan.RequiresApproval);
        Assert.True(plan.EstimatedLlmCalls > 0);
        Assert.True(plan.EstimatedRetrievalCount > 0);
    }

    [Fact]
    public async Task CreatePlanAsync_user_opt_out_auto_approves()
    {
        var settings = new FakeSettingsService();
        settings.SetUserBool(ChatApprovalSettings.RequireApproval, false, "user-1");
        var service = CreateService(settings);

        var result = await service.CreatePlanAsync("summarize all RFPs in the repository", userId: "user-1");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.RequiresApproval);
    }

    [Fact]
    public async Task CreatePlanAsync_user_with_default_preference_requires_approval()
    {
        var service = CreateService(new FakeSettingsService());

        var result = await service.CreatePlanAsync("summarize all RFPs in the repository", userId: "user-1");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresApproval);
    }

    [Fact]
    public async Task CreatePlanAsync_admin_override_forces_approval_for_opted_out_user()
    {
        var settings = new FakeSettingsService();
        settings.SetUserBool(ChatApprovalSettings.RequireApproval, false, "user-1");
        settings.SetAppBool(ChatApprovalSettings.ForceApproval, true);
        var service = CreateService(settings);

        var result = await service.CreatePlanAsync("summarize all RFPs in the repository", userId: "user-1");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresApproval);
    }

    [Fact]
    public async Task CreatePlanAsync_without_settings_service_keeps_base_heuristic()
    {
        var service = CreateService();

        var result = await service.CreatePlanAsync("summarize all RFPs in the repository", userId: "user-1");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RequiresApproval);
    }

    [Fact]
    public async Task CreatePlanAsync_non_expensive_plan_never_requires_approval_even_with_override()
    {
        var settings = new FakeSettingsService();
        settings.SetAppBool(ChatApprovalSettings.ForceApproval, true);
        var service = CreateService(settings);

        var result = await service.CreatePlanAsync("what is the upload date of the CMP document?", userId: "user-1");

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.RequiresApproval);
    }

    [Fact]
    public async Task ApproveAsync_transitions_plan_to_approved()
    {
        var service = CreateService();
        var created = await service.CreatePlanAsync("compare the two proposals");
        Assert.True(created.IsSuccess);

        var result = await service.ApproveAsync(created.Value!.PlanId, "operator@aletheia");

        Assert.True(result.IsSuccess);
        Assert.Equal(ChatPlanStatus.Approved, result.Value!.Status);
        Assert.Equal("operator@aletheia", result.Value.ReviewedBy);
        Assert.NotNull(result.Value.ReviewedAt);
    }

    [Fact]
    public async Task CancelAsync_transitions_plan_to_cancelled()
    {
        var service = CreateService();
        var created = await service.CreatePlanAsync("build a timeline of changes");
        Assert.True(created.IsSuccess);

        var result = await service.CancelAsync(created.Value!.PlanId, "User declined.");

        Assert.True(result.IsSuccess);
        Assert.Equal(ChatPlanStatus.Cancelled, result.Value!.Status);
        Assert.Equal("User declined.", result.Value.CancellationReason);
        Assert.NotNull(result.Value.ReviewedAt);
    }

    [Fact]
    public async Task ApproveAsync_fails_when_plan_not_found()
    {
        var service = CreateService();

        var result = await service.ApproveAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAsync_fails_when_plan_not_found()
    {
        var service = CreateService();

        var result = await service.CancelAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAsync_fails_when_already_cancelled()
    {
        var service = CreateService();
        var created = await service.CreatePlanAsync("summarize the corpus");
        Assert.True(created.IsSuccess);
        var cancelled = await service.CancelAsync(created.Value!.PlanId);
        Assert.True(cancelled.IsSuccess);

        var result = await service.ApproveAsync(created.Value.PlanId);

        Assert.True(result.IsFailure);
        Assert.Contains("cannot be approved", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelAsync_fails_when_already_approved()
    {
        var service = CreateService();
        var created = await service.CreatePlanAsync("summarize the corpus");
        Assert.True(created.IsSuccess);
        var approved = await service.ApproveAsync(created.Value!.PlanId);
        Assert.True(approved.IsSuccess);

        var result = await service.CancelAsync(created.Value.PlanId);

        Assert.True(result.IsFailure);
        Assert.Contains("cannot be cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAsync_expires_proposed_plan_past_expiry()
    {
        var repository = new InMemoryChatPlanRepository();
        var planning = new ChatPlanningService();
        var service = new ChatPlanApprovalService(planning, repository);
        var plan = new ChatExecutionPlan
        {
            PlanId = Guid.NewGuid(),
            Prompt = "expired prompt",
            Mode = ChatExecutionMode.CorpusAnalysis,
            Steps = new[] { "step" },
            EstimatedSecondsMin = 1,
            EstimatedSecondsMax = 10,
            EstimatedLlmCalls = 1,
            EstimatedInputTokens = 10,
            EstimatedOutputTokens = 10,
            EstimatedRetrievalCount = 5,
            RequiresApproval = true,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var record = Map(plan);
        await repository.SaveAsync(record);

        var result = await service.GetAsync(plan.PlanId);

        Assert.True(result.IsSuccess);
        Assert.Equal(ChatPlanStatus.Expired, result.Value!.Status);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_missing_plan()
    {
        var service = CreateService();

        var result = await service.GetAsync(Guid.NewGuid());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Repository_SaveAsync_and_GetAsync_round_trip()
    {
        var repository = new InMemoryChatPlanRepository();
        var record = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            Prompt = "test",
            Mode = ChatExecutionMode.Retrieval,
            Status = ChatPlanStatus.Proposed,
            Steps = new[] { "a", "b" },
            EstimatedSecondsMin = 1,
            EstimatedSecondsMax = 2,
            EstimatedLlmCalls = 1,
            EstimatedInputTokens = 10,
            EstimatedOutputTokens = 20,
            EstimatedRetrievalCount = 5,
            RequiresApproval = false
        };

        var saveResult = await repository.SaveAsync(record);
        var getResult = await repository.GetAsync(record.PlanId);

        Assert.True(saveResult.IsSuccess);
        Assert.True(getResult.IsSuccess);
        Assert.Equal(record.PlanId, getResult.Value!.PlanId);
        Assert.Equal("test", getResult.Value.Prompt);
    }

    [Fact]
    public async Task Repository_UpdateStatusAsync_fails_when_plan_missing()
    {
        var repository = new InMemoryChatPlanRepository();

        var result = await repository.UpdateStatusAsync(Guid.NewGuid(), ChatPlanStatus.Approved);

        Assert.True(result.IsFailure);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Repository_GetPendingAsync_returns_only_proposed_non_expired_plans()
    {
        var repository = new InMemoryChatPlanRepository();
        var pending = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            Prompt = "pending",
            Mode = ChatExecutionMode.Retrieval,
            Status = ChatPlanStatus.Proposed,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        var expired = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            Prompt = "expired",
            Mode = ChatExecutionMode.Retrieval,
            Status = ChatPlanStatus.Proposed,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var approved = new ChatPlanRecord
        {
            PlanId = Guid.NewGuid(),
            Prompt = "approved",
            Mode = ChatExecutionMode.Retrieval,
            Status = ChatPlanStatus.Approved,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        await repository.SaveAsync(pending);
        await repository.SaveAsync(expired);
        await repository.SaveAsync(approved);

        var result = await repository.GetPendingAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Single(result.Value);
        Assert.Equal(pending.PlanId, result.Value[0].PlanId);
    }

    [Fact]
    public void CopilotController_has_planning_endpoints()
    {
        var controller = typeof(Repository.API.Controllers.CopilotController);
        var methods = controller.GetMethods()
            .Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), false).Any()
                || m.GetCustomAttributes(typeof(HttpGetAttribute), false).Any())
            .Select(m => new
            {
                m.Name,
                Route = (m.GetCustomAttributes(typeof(HttpPostAttribute), false).FirstOrDefault() as HttpPostAttribute)?.Template
                    ?? (m.GetCustomAttributes(typeof(HttpGetAttribute), false).FirstOrDefault() as HttpGetAttribute)?.Template
            })
            .ToList();

        Assert.Contains(methods, m => m.Name == "Plan" && m.Route == "plan");
        Assert.Contains(methods, m => m.Name == "ApprovePlan" && m.Route == "plans/{planId:guid}/approve");
        Assert.Contains(methods, m => m.Name == "CancelPlan" && m.Route == "plans/{planId:guid}/cancel");
        Assert.Contains(methods, m => m.Name == "GetPlan" && m.Route == "plans/{planId:guid}");
    }

    private static ChatPlanApprovalService CreateService(ISettingsService? settings = null)
    {
        var planning = new ChatPlanningService();
        var repository = new InMemoryChatPlanRepository();
        return new ChatPlanApprovalService(planning, repository, settings);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        private readonly Dictionary<string, string> _app = new();
        private readonly Dictionary<string, Dictionary<string, string>> _users = new();

        public void SetAppBool(string key, bool value) => _app[key] = value.ToString();
        public void SetUserBool(string key, bool value, string userId)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                user = new Dictionary<string, string>();
                _users[userId] = user;
            }

            user[key] = value.ToString();
        }

        public Task<Result<IReadOnlyDictionary<string, string>>> GetAppSettingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(_app)));

        public Task<Result<bool>> SetAppSettingAsync(string key, string value, string? updatedBy = null, CancellationToken cancellationToken = default)
        {
            _app[key] = value;
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<IReadOnlyDictionary<string, string>>> GetUserSettingsAsync(string userId, CancellationToken cancellationToken = default)
        {
            var values = _users.TryGetValue(userId, out var user) ? user : new Dictionary<string, string>();
            return Task.FromResult(Result<IReadOnlyDictionary<string, string>>.Success(new Dictionary<string, string>(values)));
        }

        public Task<Result<bool>> SetUserSettingAsync(string userId, string key, string value, CancellationToken cancellationToken = default)
        {
            if (!_users.TryGetValue(userId, out var user))
            {
                user = new Dictionary<string, string>();
                _users[userId] = user;
            }

            user[key] = value;
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> GetBoolAsync(string key, bool defaultValue, string? userId = null, CancellationToken cancellationToken = default)
        {
            var raw = userId is null
                ? _app.TryGetValue(key, out var v) ? v : null
                : _users.TryGetValue(userId, out var user) && user.TryGetValue(key, out var uv) ? uv : null;
            return Task.FromResult(Result<bool>.Success(raw is null ? defaultValue : bool.TryParse(raw, out var parsed) && parsed));
        }

        public Task<Result<bool>> SetBoolAsync(string key, bool value, string? userId = null, CancellationToken cancellationToken = default)
        {
            if (userId is null)
            {
                _app[key] = value.ToString();
            }
            else
            {
                if (!_users.TryGetValue(userId, out var user))
                {
                    user = new Dictionary<string, string>();
                    _users[userId] = user;
                }

                user[key] = value.ToString();
            }

            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<string?>> GetStringAsync(string key, string? userId = null, CancellationToken cancellationToken = default)
        {
            var raw = userId is null
                ? _app.TryGetValue(key, out var v) ? v : null
                : _users.TryGetValue(userId, out var user) && user.TryGetValue(key, out var uv) ? uv : null;
            return Task.FromResult(Result<string?>.Success(raw));
        }

        public Task<Result<bool>> SetStringAsync(string key, string value, string? userId = null, CancellationToken cancellationToken = default)
        {
            if (userId is null)
            {
                _app[key] = value;
            }
            else
            {
                if (!_users.TryGetValue(userId, out var user))
                {
                    user = new Dictionary<string, string>();
                    _users[userId] = user;
                }

                user[key] = value;
            }

            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> ClearAppSettingAsync(string key, CancellationToken cancellationToken = default)
        {
            _app.Remove(key);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }

    private static ChatPlanRecord Map(ChatExecutionPlan plan)
    {
        return new ChatPlanRecord
        {
            PlanId = plan.PlanId,
            Prompt = plan.Prompt,
            Mode = plan.Mode,
            Status = ChatPlanStatus.Proposed,
            Steps = plan.Steps,
            EstimatedSecondsMin = plan.EstimatedSecondsMin,
            EstimatedSecondsMax = plan.EstimatedSecondsMax,
            EstimatedLlmCalls = plan.EstimatedLlmCalls,
            EstimatedInputTokens = plan.EstimatedInputTokens,
            EstimatedOutputTokens = plan.EstimatedOutputTokens,
            EstimatedRetrievalCount = plan.EstimatedRetrievalCount,
            RequiresApproval = plan.RequiresApproval,
            CreatedAt = plan.CreatedAt,
            ExpiresAt = plan.ExpiresAt
        };
    }
}
