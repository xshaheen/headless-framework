// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging;

internal static class MessagingModuleContributionRecording
{
    /// <summary>
    /// Records one generated module for the host's consumer registry. <c>ConfigureMessaging(...).AddModule&lt;T&gt;()</c>
    /// and <c>AddHeadlessMessaging(o =&gt; o.AddModule&lt;T&gt;())</c> both land here, so a module added twice registers once.
    /// </summary>
    public static void AddMessagingModuleContribution<TModule>(this IServiceCollection services)
        where TModule : IMessagingModule
    {
        services.AddSingleton(
            new MessagingModuleContribution(typeof(TModule), static catalog => TModule.Register(catalog))
        );
    }
}

/// <summary>One generated module that a contribution or the host asked messaging to register.</summary>
/// <param name="ModuleType">The generated module type, which identifies the module across contributions.</param>
/// <param name="Register">Runs the module's generated registration against a catalog.</param>
internal sealed record MessagingModuleContribution(Type ModuleType, Action<MessagingCatalogBuilder> Register);
