namespace Framework.Permissions.Permissions.Definitions;

public interface IPermissionDefinitionProvider
{
    void PreDefine(IPermissionDefinitionContext context);

    void Define(IPermissionDefinitionContext context);

    void PostDefine(IPermissionDefinitionContext context);
}

public abstract class PermissionDefinitionProvider : IPermissionDefinitionProvider
{
    public virtual void PreDefine(IPermissionDefinitionContext context) { }

    public abstract void Define(IPermissionDefinitionContext context);

    public virtual void PostDefine(IPermissionDefinitionContext context) { }
}
