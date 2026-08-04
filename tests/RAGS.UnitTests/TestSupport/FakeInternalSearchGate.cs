using Aletheia.RAGS.Abstractions.Interfaces;

namespace RAGS.UnitTests.TestSupport;

public sealed class FakeInternalSearchGate : IInternalSearchGate
{
    public FakeInternalSearchGate(bool showInternalSearch = false)
    {
        ShowInternalSearch = showInternalSearch;
    }

    public bool ShowInternalSearch { get; }
}
