using System.Security.Claims;
using Aletheia.Foundation.Security;
using Aletheia.Foundation.Shared;
using Aletheia.Security.Authentication;
using Aletheia.Security.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Repository.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserService _userService;
    private readonly IRoleService _roleService;
    private readonly ICurrentUserService _currentUserService;

    public AuthController(
        IAuthenticationService authenticationService,
        IUserService userService,
        IRoleService roleService,
        ICurrentUserService currentUserService)
    {
        _authenticationService = authenticationService;
        _userService = userService;
        _roleService = roleService;
        _currentUserService = currentUserService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authenticationService.AuthenticateAsync(request.Username, request.Password, request.IdentityProvider, cancellationToken);
        if (!result.IsSuccess)
        {
            return Unauthorized(new { error = result.Error });
        }

        return Ok(new
        {
            user = MapUser(result.User!),
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresAt = result.ExpiresAt
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _authenticationService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
        if (!result.IsSuccess)
        {
            return Unauthorized(new { error = result.Error });
        }

        return Ok(new
        {
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresAt = result.ExpiresAt
        });
    }

    [HttpPost("revoke")]
    [Authorize]
    public async Task<IActionResult> Revoke([FromBody] RevokeRequest request, CancellationToken cancellationToken)
    {
        var result = await _authenticationService.RevokeTokenAsync(request.RefreshToken, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var user = _currentUserService.CurrentUser;
        if (user == null)
        {
            return Unauthorized();
        }

        return Ok(MapUser(user));
    }

    [HttpPost("users")]
    [Authorize(Roles = "Administrator,PowerUser")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.CreateUserAsync(
            request.Username,
            request.Email,
            request.DisplayName,
            request.Password,
            request.Roles,
            cancellationToken);

        if (result.IsFailure || result.Value is null)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(MapUser(result.Value));
    }

    [HttpGet("users")]
    [Authorize(Roles = "Administrator,PowerUser")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var result = await _userService.GetAllUsersAsync(cancellationToken);
        if (result.IsFailure || result.Value is null)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value.Select(MapUser));
    }

    [HttpGet("users/{userId}")]
    [Authorize(Roles = "Administrator,PowerUser,Auditor")]
    public async Task<IActionResult> GetUser(string userId, CancellationToken cancellationToken)
    {
        var result = await _userService.GetUserAsync(userId, cancellationToken);
        if (result.IsFailure || result.Value is null)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(MapUser(result.Value));
    }

    [HttpPost("users/{userId}/disable")]
    [Authorize(Roles = "Administrator,PowerUser")]
    public async Task<IActionResult> DisableUser(string userId, CancellationToken cancellationToken)
    {
        var result = await _userService.DisableUserAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpPost("users/{userId}/enable")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> EnableUser(string userId, CancellationToken cancellationToken)
    {
        var result = await _userService.EnableUserAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpPost("users/{userId}/reset-password")]
    [Authorize(Roles = "Administrator,PowerUser")]
    public async Task<IActionResult> ResetPassword(string userId, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.ResetPasswordAsync(userId, request.NewPassword, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpDelete("users/{userId}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken cancellationToken)
    {
        var result = await _userService.DeleteUserAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpPost("users/{userId}/roles")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> AssignRole(string userId, [FromBody] AssignRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _roleService.AssignRoleAsync(userId, request.Role, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpDelete("users/{userId}/roles/{role}")]
    [Authorize(Roles = "Administrator")]
    public async Task<IActionResult> RemoveRole(string userId, string role, CancellationToken cancellationToken)
    {
        var result = await _roleService.RemoveRoleAsync(userId, role, cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok();
    }

    [HttpGet("roles")]
    [Authorize]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        var result = await _roleService.GetAvailableRolesAsync(cancellationToken);
        if (result.IsFailure)
        {
            return BadRequest(new { error = result.Error });
        }

        return Ok(result.Value);
    }

    private static object MapUser(UserIdentity user)
    {
        return new
        {
            user.UserId,
            user.Username,
            user.Email,
            user.DisplayName,
            user.Roles,
            user.IdentityProvider
        };
    }
}

public sealed record LoginRequest(string Username, string Password, string? IdentityProvider = null);
public sealed record RefreshRequest(string RefreshToken);
public sealed record RevokeRequest(string RefreshToken);
public sealed record CreateUserRequest(string Username, string Email, string DisplayName, string Password, IEnumerable<string>? Roles = null);
public sealed record ResetPasswordRequest(string NewPassword);
public sealed record AssignRoleRequest(string Role);
