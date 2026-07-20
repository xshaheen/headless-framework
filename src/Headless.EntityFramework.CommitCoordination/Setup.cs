// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.CommitCoordination;
using Headless.EntityFramework.Contexts.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.EntityFramework;

/// <summary>Registers commit coordination for the Headless Entity Framework save pipeline.</summary>
[PublicAPI]
public static class SetupEntityFrameworkCommitCoordination
{
    /// <summary>
    /// Enlists transactions opened by the Headless save pipeline in commit coordination so deferred work
    /// drains on commit and is discarded on rollback.
    /// </summary>
    /// <param name="builder">The Headless Entity Framework builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IHeadlessDbContextBuilder AddCommitCoordination(this IHeadlessDbContextBuilder builder)
    {
        Argument.IsNotNull(builder);

        builder.Services.AddEntityFrameworkCommitCoordination();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<
                IHeadlessTransactionCoordinator,
                HeadlessCommitCoordinationTransactionCoordinator
            >()
        );

        return builder;
    }
}
