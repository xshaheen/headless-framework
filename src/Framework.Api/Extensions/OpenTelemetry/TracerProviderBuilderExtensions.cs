// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace OpenTelemetry.Trace;

[PublicAPI]
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
