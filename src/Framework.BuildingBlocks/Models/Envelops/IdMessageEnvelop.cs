#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

public sealed record IdMessageEnvelop(string Id, MessageDescriptor Message) : IIdEnvelop, IMessageEnvelop
{
    public IdMessageEnvelop(Guid id, MessageDescriptor message)
        : this(id.ToString(), message) { }

    public IdMessageEnvelop(long id, MessageDescriptor message)
        : this(id.ToString(CultureInfo.InvariantCulture), message) { }

    public IdMessageEnvelop(int id, MessageDescriptor message)
        : this(id.ToString(CultureInfo.InvariantCulture), message) { }
}
