namespace Framework.Features.Definitions;

public interface ICanCreateChildFeature
{
    FeatureDefinition AddChild(
        string name,
        string? defaultValue = null,
        string? displayName = null,
        string? description = null,
        bool isVisibleToClients = true,
        bool isAvailableToHost = true
    );
}
