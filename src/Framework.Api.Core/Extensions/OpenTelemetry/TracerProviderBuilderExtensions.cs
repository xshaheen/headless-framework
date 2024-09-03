using Framework.Kernel.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

public static class TracerProviderBuilderExtensions
{
    public static TracerProviderBuilder AddIf(
        this TracerProviderBuilder builder,
        bool condition,
        Func<TracerProviderBuilder, TracerProviderBuilder> action
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(action);

        if (condition)
        {
            builder = action(builder);
        }

        return builder;
    }
}
