namespace Framework.Settings.Definitions;

/// <summary>Used to define a setting definition.</summary>
public interface ISettingDefinitionProvider
{
    void Define(ISettingDefinitionContext context);
}

/// <inheritdoc />
public abstract class SettingDefinitionProvider : ISettingDefinitionProvider
{
    public abstract void Define(ISettingDefinitionContext context);
}
