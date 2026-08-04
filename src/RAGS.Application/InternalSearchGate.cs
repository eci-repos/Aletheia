using Aletheia.RAGS.Abstractions.Configuration;
using Aletheia.RAGS.Abstractions.Interfaces;
using Microsoft.Extensions.Options;

namespace Aletheia.RAGS.Application;

public sealed class InternalSearchGate : IInternalSearchGate
{
    public InternalSearchGate(IOptions<FeatureFlagsOptions> options)
    {
        ShowInternalSearch = options?.Value.ShowInternalSearch ?? false;
    }

    public bool ShowInternalSearch { get; }
}
