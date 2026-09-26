// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Idempotency;

/// <summary>
/// Configures durable idempotency during <c>AddHeadlessIdempotency(setup =&gt; …)</c>: the admission defaults and purge
/// schedule, the storage schema, and exactly one provider chosen through a <c>Use…</c> call such as
/// <c>UseInMemory</c>, <c>UsePostgreSql</c>, or <c>UseSqlServer</c>.
/// </summary>
[PublicAPI]
public sealed class HeadlessIdempotencySetupBuilder
{
    internal HeadlessIdempotencySetupBuilder(IServiceCollection services)
    {
        Services = Argument.IsNotNull(services);
    }

    internal IServiceCollection Services { get; }

    // Composed in call order rather than last-write-wins, so setup split across several calls keeps every part.
    internal Action<IdempotentOperationsOptions>? OptionsConfigurator { get; private set; }

    internal IList<IIdempotencyProviderOptionsExtension> Extensions { get; } = [];

    /// <summary>
    /// Configures <see cref="IdempotentOperationsOptions" />. Repeated calls run in call order against the same
    /// instance, so a later call overrides an earlier one.
    /// </summary>
    /// <param name="configure">Delegate that mutates the options.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessIdempotencySetupBuilder ConfigureOptions(Action<IdempotentOperationsOptions> configure)
    {
        Argument.IsNotNull(configure);

        var previous = OptionsConfigurator;
        OptionsConfigurator = previous is null
            ? configure
            : options =>
            {
                previous(options);
                configure(options);
            };

        return this;
    }

    /// <summary>Configures <see cref="IdempotencyStorageOptions" />, the schema the record table lives in.</summary>
    /// <param name="configure">Delegate that mutates the storage options.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public HeadlessIdempotencySetupBuilder ConfigureStorage(Action<IdempotencyStorageOptions> configure)
    {
        Argument.IsNotNull(configure);

        // No validator here: the selected provider attaches the one that knows its dialect, so the same schema
        // string is checked against PostgreSQL or SQL Server identifier rules, never both.
        Services.Configure(configure);

        return this;
    }

    /// <summary>
    /// Binds <see cref="IdempotencyStorageOptions" /> from <paramref name="configuration" />; pass the
    /// <c>Headless:Idempotency:Storage</c> section to bind <c>Headless:Idempotency:Storage:Schema</c>.
    /// </summary>
    /// <param name="configuration">The configuration section to bind from.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration" /> is <see langword="null" />.</exception>
    public HeadlessIdempotencySetupBuilder ConfigureStorage(IConfiguration configuration)
    {
        Argument.IsNotNull(configuration);

        Services.Configure<IdempotencyStorageOptions>(configuration);

        return this;
    }

    /// <summary>
    /// Registers a provider extension whose services are added when setup completes. Called by provider packages'
    /// <c>Use…</c> members rather than by application code.
    /// </summary>
    /// <param name="extension">The provider extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="extension" /> is <see langword="null" />.</exception>
    public void RegisterExtension(IIdempotencyProviderOptionsExtension extension)
    {
        Argument.IsNotNull(extension);
        Extensions.Add(extension);
    }
}
