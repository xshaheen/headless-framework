namespace Framework.Settings.DefinitionProviders;

public interface ISettingDefinitionProvider
{
    void Define(ISettingDefinitionContext context);
}

public abstract class SettingDefinitionProvider : ISettingDefinitionProvider
{
    public abstract void Define(ISettingDefinitionContext context);
}
