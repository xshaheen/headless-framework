using Framework.BuildingBlocks.Helpers;

namespace Framework.Api.Core.Abstractions;

public interface ICurrentTenant
{
    bool IsAvailable { get; }

    Guid? Id { get; }

    string? Name { get; }

    IDisposable Change(Guid? id, string? name = null);
}

public sealed class NullCurrentTenant : ICurrentTenant
{
    public bool IsAvailable => false;

    public Guid? Id => null;

    public string? Name => null;

    public IDisposable Change(Guid? id, string? name = null)
    {
        return NullDisposable.Instance;
    }
}
