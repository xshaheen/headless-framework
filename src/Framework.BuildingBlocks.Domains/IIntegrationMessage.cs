namespace Framework.BuildingBlocks.Domains;

public interface IIntegrationMessage
{
    string Id { get; }

    string MessageKey { get; }

    DateTimeOffset Timestamp { get; }
}
