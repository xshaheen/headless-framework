namespace Framework.Orm.Couchbase.Context;

[PublicAPI]
[AttributeUsage(AttributeTargets.Property)]
public sealed class CollectionAttribute(string collection, string? scope = null) : Attribute
{
    public string? Scope { get; } = scope;

    public string Collection { get; } = collection;
}
