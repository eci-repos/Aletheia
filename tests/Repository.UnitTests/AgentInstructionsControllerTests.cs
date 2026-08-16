using System.Security.Claims;
using Aletheia.Foundation.Shared;
using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Aletheia.RAGS.Abstractions.Models;
using Aletheia.Repository.Abstractions.Interfaces;
using Repository.API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Repository.UnitTests.Controllers;

public class AgentInstructionsControllerTests
{
    [Fact]
    public async Task GetAgentInstructions_returns_all_roles()
    {
        var resolver = new Mock<IAgentInstructionResolver>();
        resolver
            .Setup(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string role, CancellationToken _) =>
                Result<AgentInstructionResolution>.Success(new(role, $"prompt for {role}", "config")));
        var controller = CreateController(resolver);

        var result = await controller.GetAgentInstructions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<AgentInstructionResolution>>(ok.Value);
        Assert.Equal(AgentInstructionRoles.All.Count, items.Count);
        Assert.All(items, i => Assert.Equal("config", i.Source));
    }

    [Fact]
    public async Task GetAgentInstructions_returns_500_when_resolver_not_configured()
    {
        var controller = CreateController(null);

        var result = await controller.GetAgentInstructions(CancellationToken.None);

        var serverError = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, serverError.StatusCode);
    }

    [Fact]
    public async Task UpdateAgentInstruction_saves_override_for_known_role()
    {
        var settings = new Mock<ISettingsService>();
        settings
            .Setup(x => x.SetAppSettingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        var controller = CreateController(settings: settings);

        var result = await controller.UpdateAgentInstruction(
            AgentInstructionRoles.GraphRagExtractor,
            new UpdateAgentInstructionRequest("Custom extractor prompt."),
            CancellationToken.None);

        Assert.IsType<OkResult>(result);
        settings.Verify(x => x.SetAppSettingAsync(
            AgentInstructionRoles.SettingKey(AgentInstructionRoles.GraphRagExtractor),
            "Custom extractor prompt.",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAgentInstruction_rejects_unknown_role()
    {
        var controller = CreateController();

        var result = await controller.UpdateAgentInstruction(
            "not.a.role",
            new UpdateAgentInstructionRequest("prompt"),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Unknown agent instruction role", badRequest.Value!.ToString());
    }

    [Fact]
    public async Task UpdateAgentInstruction_rejects_empty_value()
    {
        var controller = CreateController();

        var result = await controller.UpdateAgentInstruction(
            AgentInstructionRoles.GraphRagExtractor,
            new UpdateAgentInstructionRequest("   "),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateAgentInstruction_rejects_value_over_length_limit()
    {
        var controller = CreateController();

        var result = await controller.UpdateAgentInstruction(
            AgentInstructionRoles.GraphRagExtractor,
            new UpdateAgentInstructionRequest(new string('x', 20_001)),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ResetAgentInstruction_clears_the_row()
    {
        var settings = new Mock<ISettingsService>();
        settings
            .Setup(x => x.ClearAppSettingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool>.Success(true));
        var controller = CreateController(settings: settings);

        var result = await controller.ResetAgentInstruction(AgentInstructionRoles.GraphRagExtractor, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        settings.Verify(x => x.ClearAppSettingAsync(
            AgentInstructionRoles.SettingKey(AgentInstructionRoles.GraphRagExtractor),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResetAgentInstruction_rejects_unknown_role()
    {
        var controller = CreateController();

        var result = await controller.ResetAgentInstruction("not.a.role", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    private static SettingsController CreateController(
        Mock<IAgentInstructionResolver>? resolver = null,
        Mock<ISettingsService>? settings = null)
    {
        var controller = new SettingsController(
            (settings ?? new Mock<ISettingsService>()).Object,
            resolver?.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "admin@aletheia")
                }))
            }
        };
        return controller;
    }
}
