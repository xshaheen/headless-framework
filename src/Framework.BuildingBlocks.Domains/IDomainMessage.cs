namespace Framework.BuildingBlocks.Domains;

public interface IDomainMessage
{
    string Id { get; }

    DateTimeOffset Timestamp { get; }
}
