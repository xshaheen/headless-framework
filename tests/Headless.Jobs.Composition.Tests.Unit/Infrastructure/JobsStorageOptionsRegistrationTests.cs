// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Infrastructure;

/// <summary>
/// How the two <c>ConfigureStorage</c> overloads compose once the EF store is registered: the section binds in the
/// Core layer, the callback snapshot is applied after it by the provider, and the resolved value is validated.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class JobsStorageOptionsRegistrationTests
{
    [Fact]
    public void schema_binds_from_the_supplied_configuration_section()
    {
        using var provider = _BuildProvider(jobs => jobs.ConfigureStorage(_StorageSection("configured_jobs")));

        provider.GetRequiredService<JobsStorageOptions>().Schema.Should().Be("configured_jobs");
    }

    [Fact]
    public void schema_authored_in_code_wins_over_the_bound_section()
    {
        using var provider = _BuildProvider(jobs =>
        {
            jobs.ConfigureStorage(_StorageSection("from_configuration"));
            jobs.ConfigureStorage(storage => storage.Schema = "from_code");
        });

        provider.GetRequiredService<JobsStorageOptions>().Schema.Should().Be("from_code");
    }

    [Fact]
    public void bound_section_survives_a_registration_that_never_authored_a_schema()
    {
        // The provider registers its snapshot on every host. Were that snapshot unconditional it would write the
        // builder's untouched default over the bound section, which is the whole reason it is applied conditionally.
        using var provider = _BuildProvider(jobs => jobs.ConfigureStorage(_StorageSection("section_only")));

        provider.GetRequiredService<JobsStorageOptions>().Schema.Should().Be("section_only");
    }

    [Fact]
    public void schema_falls_back_to_the_default_when_neither_overload_is_used()
    {
        using var provider = _BuildProvider(_ => { });

        provider.GetRequiredService<JobsStorageOptions>().Schema.Should().Be(JobsStorageOptions.DefaultSchema);
    }

    [Fact]
    public void an_identifier_no_provider_could_accept_is_rejected()
    {
        using var provider = _BuildProvider(jobs => jobs.ConfigureStorage(storage => storage.Schema = "not a schema!"));

        var act = () => provider.GetRequiredService<JobsStorageOptions>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void a_section_carrying_an_unusable_identifier_is_rejected_the_same_way()
    {
        using var provider = _BuildProvider(jobs => jobs.ConfigureStorage(_StorageSection("not a schema!")));

        var act = () => provider.GetRequiredService<JobsStorageOptions>();

        act.Should().Throw<OptionsValidationException>();
    }

    /// <summary>The <c>Headless:Jobs:Storage</c> section itself, as a caller is expected to pass it.</summary>
    private static IConfiguration _StorageSection(string schema)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Headless:Jobs:Storage:Schema"] = schema }
            )
            .Build()
            .GetSection("Headless:Jobs:Storage");
    }

    private static ServiceProvider _BuildProvider(Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>> configure)
    {
        var services = new ServiceCollection();
        // The durable store requires a membership provider that is not the null fallback; nothing here starts it.
        services.AddSingleton(Substitute.For<INodeMembership>());
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
        {
            configure(jobs);
            jobs.UseEntityFramework();
        });

        return services.BuildServiceProvider();
    }
}
