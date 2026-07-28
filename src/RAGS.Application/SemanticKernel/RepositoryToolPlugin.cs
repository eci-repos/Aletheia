using Microsoft.SemanticKernel;

namespace Aletheia.RAGS.Application.SemanticKernel;

/// <summary>
/// Alternative plugin name for the Aletheia Knowledge Estate tool suite.
/// This is a thin shim over <see cref="AletheiaKnowledgePlugin"/>, registered under
/// the "RepositoryTool" plugin name so that planners and prompts can refer to it as the
/// repository/local-knowledge tool.
/// </summary>
public sealed class RepositoryToolPlugin
{
    private readonly AletheiaKnowledgePlugin _inner;

    public RepositoryToolPlugin(AletheiaKnowledgePlugin inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }
}
