namespace Aletheia.RAGS.Abstractions.Configuration;

/// <summary>Setting keys for the Copilot chat approval policy (Sprint 61). Shared by the API
/// (RAGS.Application) and the Web client so the keys never drift.</summary>
public static class ChatApprovalSettings
{
    /// <summary>Per-user preference: when false, plans auto-approve and execute immediately. Default true.</summary>
    public const string RequireApproval = "copilot.requireApproval";

    /// <summary>Global admin override: when true, approval is forced even for opted-out users. Default false.</summary>
    public const string ForceApproval = "copilot.requireApproval.force";
}
