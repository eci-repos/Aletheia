using System.Text;
using Aletheia.Foundation.Security;
using Aletheia.Security.Authentication;
using Aletheia.Security.Authorization;
using Aletheia.Security.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Aletheia.Security;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAletheiaSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSecret = ResolveSecret(configuration, "Authentication:Jwt:Secret", "ALETHEIA_JWT_SECRET");
        var jwtIssuer = configuration["Authentication:Jwt:Issuer"] ?? "Aletheia";
        var jwtAudience = configuration["Authentication:Jwt:Audience"] ?? "Aletheia.API";
        var accessTokenMinutes = int.Parse(configuration["Authentication:Jwt:AccessTokenLifetimeMinutes"] ?? "60");
        var refreshTokenMinutes = int.Parse(configuration["Authentication:Jwt:RefreshTokenLifetimeMinutes"] ?? "10080");
        var clockSkewMinutes = int.Parse(configuration["Authentication:Jwt:ClockSkewMinutes"] ?? "43200");

        var tokenService = new JwtTokenService(
            jwtSecret,
            jwtIssuer,
            jwtAudience,
            TimeSpan.FromMinutes(accessTokenMinutes),
            TimeSpan.FromMinutes(refreshTokenMinutes));

        services.AddSingleton(tokenService);
        services.TryAddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();
        services.TryAddSingleton<IUserStore, InMemoryUserStore>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                ValidateIssuer = !string.IsNullOrWhiteSpace(jwtIssuer),
                ValidIssuer = jwtIssuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(jwtAudience),
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(clockSkewMinutes)
            };
        });

        services.AddAuthorization(options => AuthorizationPolicies.Configure(options));

        services.AddSingleton<IIdentityProvider, LocalIdentityProvider>();
        services.AddSingleton<IAuthenticationService, Authentication.AuthenticationService>();
        services.AddSingleton<IUserService, InMemoryUserService>();
        services.AddSingleton<IRoleService, InMemoryRoleService>();
        services.AddSingleton<ICurrentUserService, HttpContextCurrentUserService>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddHostedService<AdminSeederHostedService>();

        return services;
    }

    private static string ResolveSecret(IConfiguration configuration, string configKey, string envKey)
    {
        var value = configuration[configKey];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Security secret not found. Configure '{configKey}' in settings or set environment variable '{envKey}'.");
    }
}
