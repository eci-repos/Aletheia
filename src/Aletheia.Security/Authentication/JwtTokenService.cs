using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aletheia.Foundation.Security;
using Microsoft.IdentityModel.Tokens;

namespace Aletheia.Security.Authentication;

public sealed class JwtTokenService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _accessTokenLifetime;
    private readonly TimeSpan _refreshTokenLifetime;

    public JwtTokenService(string secret, string issuer, string audience, TimeSpan accessTokenLifetime, TimeSpan refreshTokenLifetime)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("JWT secret is required.", nameof(secret));
        }

        _secret = secret;
        _issuer = issuer;
        _audience = audience;
        _accessTokenLifetime = accessTokenLifetime;
        _refreshTokenLifetime = refreshTokenLifetime;
    }

    public string GenerateAccessToken(UserIdentity user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("display_name", user.DisplayName),
            new("identity_provider", user.IdentityProvider)
        };

        foreach (var role in user.Roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        foreach (var claim in user.Claims)
        {
            claims.Add(new Claim(claim.Key, claim.Value));
        }

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(_accessTokenLifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));

        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = !string.IsNullOrWhiteSpace(_issuer),
                ValidIssuer = _issuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(_audience),
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            }, out _);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    public TimeSpan AccessTokenLifetime => _accessTokenLifetime;
    public TimeSpan RefreshTokenLifetime => _refreshTokenLifetime;
}
