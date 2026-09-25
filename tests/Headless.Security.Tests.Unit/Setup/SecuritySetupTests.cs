// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Validation;
using Headless.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class SecuritySetupTests
{
    [Fact]
    public void should_be_idempotent_when_add_string_encryption_service()
    {
        // given
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Security:One:DefaultPassPhrase", "FirstPassPhrase123"),
                new KeyValuePair<string, string?>("Security:One:InitVectorBytes", "VGVzdElWMDEyMzQ1Njc4OQ=="),
                new KeyValuePair<string, string?>("Security:One:DefaultSalt", "VGVzdFNhbHQ="),
                new KeyValuePair<string, string?>("Security:Two:DefaultPassPhrase", "SecondPassPhrase12"),
                new KeyValuePair<string, string?>("Security:Two:InitVectorBytes", "U2Vjb25kSVYwMTIzNDU2Nw=="),
                new KeyValuePair<string, string?>("Security:Two:DefaultSalt", "U2Vjb25kU2FsdA=="),
            ])
            .Build();

        // when
        services.AddStringEncryptionService(configuration.GetRequiredSection("Security:One"));
        services.AddStringEncryptionService(configuration.GetRequiredSection("Security:Two"));

        using var serviceProvider = services.BuildServiceProvider();
        var encryptionOptions = serviceProvider.GetRequiredService<IOptions<StringEncryptionOptions>>().Value;

        // then
        encryptionOptions.DefaultPassPhrase.Should().Be("FirstPassPhrase123");
        encryptionOptions.DefaultSalt.Should().BeEquivalentTo("TestSalt"u8.ToArray());
    }

    [Fact]
    public void should_be_idempotent_when_add_string_hash_service()
    {
        // given
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Security:One:DefaultSalt", "FirstSalt"),
                new KeyValuePair<string, string?>("Security:One:Iterations", "700000"),
                new KeyValuePair<string, string?>("Security:Two:DefaultSalt", "SecondSalt"),
                new KeyValuePair<string, string?>("Security:Two:Iterations", "800000"),
            ])
            .Build();

        // when
        services.AddLookupHasher(configuration.GetRequiredSection("Security:One"));
        services.AddLookupHasher(configuration.GetRequiredSection("Security:Two"));

        using var serviceProvider = services.BuildServiceProvider();
        var hashOptions = serviceProvider.GetRequiredService<IOptions<LookupHasherOptions>>().Value;

        // then
        hashOptions.DefaultSalt.Should().Be("FirstSalt");
        hashOptions.Iterations.Should().Be(700000);
    }

    [Fact]
    public void should_bind_shared_options_and_pbkdf2_options_from_configuration()
    {
        // given
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("SecretHasher:MaxSecretLength", "64"),
                new KeyValuePair<string, string?>("SecretHasher:Pbkdf2:Iterations", "1000"),
            ])
            .Build();

        // when
        services.AddHeadlessSecretHasher(setup =>
        {
            setup.Configure(configuration.GetRequiredSection("SecretHasher"));
            setup.UsePbkdf2Sha256(configuration.GetRequiredSection("SecretHasher:Pbkdf2"));
        });

        using var serviceProvider = services.BuildServiceProvider();
        var hasher = serviceProvider.GetRequiredService<ISecretHasher>();
        var encoded = hasher.Hash("pin");

        // then
        encoded.Should().StartWith("$pbkdf2-sha256$i=1000,l=32$");
        hasher.Verify("pin", encoded).Succeeded.Should().BeTrue();
        serviceProvider.GetRequiredService<IOptions<SecretHasherOptions>>().Value.MaxSecretLength.Should().Be(64);
        serviceProvider.GetServices<ISecretHashAlgorithm>().Should().ContainSingle();
    }

    [Fact]
    public void should_select_pbkdf2_with_defaults()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256());

        using var serviceProvider = services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ISecretHasher>().Hash("pin").Should().StartWith("$pbkdf2-sha256$i=600000,");
    }

    [Fact]
    public void should_select_pbkdf2_from_delegate()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256(o => o.Iterations = 1_000));

        using var serviceProvider = services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ISecretHasher>().Hash("pin").Should().StartWith("$pbkdf2-sha256$i=1000,");
    }

    [Fact]
    public void should_select_pbkdf2_from_service_provider_delegate()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256((o, _) => o.Iterations = 1_000));

        using var serviceProvider = services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ISecretHasher>().Hash("pin").Should().StartWith("$pbkdf2-sha256$i=1000,");
    }

    [Fact]
    public void should_refuse_registration_without_an_algorithm()
    {
        var services = new ServiceCollection();

        FluentActions
            .Invoking(() => services.AddHeadlessSecretHasher(setup => setup.Configure(_ => { })))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exactly one algorithm*UseArgon2id*UsePbkdf2Sha256*");
    }

    [Fact]
    public void should_refuse_more_than_one_algorithm()
    {
        var services = new ServiceCollection();

        FluentActions
            .Invoking(() => services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256().UsePbkdf2Sha256()))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Multiple algorithms*");
    }

    [Fact]
    public void should_refuse_a_second_registration()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256());

        // then
        FluentActions
            .Invoking(() => services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256()))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*already called*");
    }

    [Fact]
    public void should_register_the_registration_validator_and_the_cost_check()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256());

        // then
        services
            .Where(d => d.ServiceType == typeof(IHeadlessStartupValidator))
            .Should()
            .ContainSingle(d => d.ImplementationType == typeof(SecretHasherRegistrationValidator));
        services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Should()
            .Contain(d => d.ImplementationType == typeof(SecretHasherCostCheckService));
    }

    [Fact]
    public async Task should_fail_host_start_when_the_selected_algorithm_is_not_registered()
    {
        // given — a custom Use* extension that selects an id but never registers the algorithm.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessSecretHasher(setup =>
        {
            setup.Configure(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Off);
            setup.RegisterExtension(new SelectOnlyExtension());
        });
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(CancellationToken.None);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'custom'*no ISecretHashAlgorithm*");
    }

    private sealed class SelectOnlyExtension : ISecretHashAlgorithmOptionsExtension
    {
        public string AlgorithmId => "custom";

        public void AddServices(IServiceCollection services) { }
    }

    [Fact]
    public void should_reject_invalid_shared_options_on_resolution()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSecretHasher(setup => setup.Configure(o => o.MaxSecretLength = 0).UsePbkdf2Sha256());

        using var serviceProvider = services.BuildServiceProvider();

        // then
        FluentActions
            .Invoking(() => serviceProvider.GetRequiredService<IOptions<SecretHasherOptions>>().Value)
            .Should()
            .Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_reject_invalid_pbkdf2_options_on_resolution()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSecretHasher(setup => setup.UsePbkdf2Sha256(o => o.Iterations = 0));

        using var serviceProvider = services.BuildServiceProvider();

        // then
        FluentActions
            .Invoking(() => serviceProvider.GetRequiredService<IOptions<Pbkdf2Sha256HashOptions>>().Value)
            .Should()
            .Throw<OptionsValidationException>();
    }
}
