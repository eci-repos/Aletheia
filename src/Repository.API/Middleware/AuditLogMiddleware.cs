namespace Aletheia.Repository.API.Middleware;

public sealed class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;

    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        var isMutating = method is "POST" or "PUT" or "DELETE" or "PATCH";

        if (isMutating)
        {
            _logger.LogInformation("Audit: {Method} {Path} started at {Timestamp}", method, path, DateTime.UtcNow);
        }

        await _next(context);

        if (isMutating)
        {
            _logger.LogInformation("Audit: {Method} {Path} completed with {StatusCode} at {Timestamp}", method, path, context.Response.StatusCode, DateTime.UtcNow);
        }
    }
}
