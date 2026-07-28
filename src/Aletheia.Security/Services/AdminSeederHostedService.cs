using Aletheia.Foundation.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aletheia.Security.Services;

public class AdminSeederHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AdminSeederHostedService> _logger;

    public AdminSeederHostedService(IServiceProvider serviceProvider, IHostEnvironment environment, ILogger<AdminSeederHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        // Check if admin user already exists
        var adminResult = await userService.GetUserByUsernameAsync("admin", cancellationToken).ConfigureAwait(false);
        if (adminResult.IsSuccess)
        {
            _logger.LogInformation("Admin user already exists; skipping seed.");
            return;
        }

        var adminPassword = ResolveAdminPassword();

        var result = await userService.CreateUserAsync(
            username: "admin",
            email: "admin@aletheia.local",
            displayName: "System Administrator",
            password: adminPassword,
            roles: new[] { RoleDefinitions.Administrator },
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Admin user seeded successfully. Change default password via Production-Security-Checklist.");
        }
        else
        {
            _logger.LogWarning("Failed to seed admin user: {Error}", result.Error);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private string ResolveAdminPassword()
    {
        var configured = Environment.GetEnvironmentVariable("ALETHEIA_ADMIN_PASSWORD");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        if (_environment.IsDevelopment())
        {
            _logger.LogWarning("ALETHEIA_ADMIN_PASSWORD is not set; using development-only default admin password.");
            return "Admin123!";
        }

        throw new InvalidOperationException("ALETHEIA_ADMIN_PASSWORD must be set before seeding the production admin account.");
    }
}
