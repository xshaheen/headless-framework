// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;

namespace DotNet.Testcontainers.Builders;

/// <summary>Container reuse that stays inside one checkout of the repository.</summary>
[PublicAPI]
public static class HeadlessContainerBuilderExtensions
{
    extension<TBuilder, TContainer, TConfiguration>(IContainerBuilder<TBuilder, TContainer, TConfiguration> builder)
        where TBuilder : IContainerBuilder<TBuilder, TContainer, TConfiguration>
        where TContainer : IContainer
        where TConfiguration : IContainerConfiguration
    {
        /// <summary>
        /// Keeps the container running between test runs, like <c>WithReuse(true)</c>, but reuses it only from the
        /// same checkout of the repository.
        /// </summary>
        /// <remarks>
        /// Testcontainers picks the reused container by hashing its configuration, and a plain <c>WithReuse(true)</c>
        /// hashes the same in every worktree. Two worktrees running the same integration project would then share
        /// one container: one branch's schema and rows leak into the other's run, and one run's reset breaks the
        /// other's. The checkout label puts the checkout's root directory into that hash. Add a label of your own
        /// when two fixtures in one project build an otherwise identical container.
        /// </remarks>
        public TBuilder WithCheckoutReuse()
        {
#pragma warning disable RS0030 // The one sanctioned caller: it always adds the checkout label the ban exists for.
            return builder.WithReuse(true).WithLabel(ReuseLabel.CheckoutKey, ReuseLabel.Checkout);
#pragma warning restore RS0030
        }
    }
}
