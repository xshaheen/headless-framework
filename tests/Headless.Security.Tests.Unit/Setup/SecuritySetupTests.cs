// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
    public void should_register_secret_hasher_from_configuration_and_keep_the_first_registration()
    {
        // given
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("One:Algorithm", SecretHashAlgorithms.Pbkdf2Sha256),
                new KeyValuePair<string, string?>("One:Pbkdf2Sha256:Iterations", "1000"),
                new KeyValuePair<string, string?>("Two:Pbkdf2Sha256:Iterations", "2000"),
            ])
            .Build();

        // when
        services.AddSecretHasher(configuration.GetRequiredSection("One"));
        services.AddSecretHasher(configuration.GetRequiredSection("Two"));

        using var serviceProvider = services.BuildServiceProvider();
        var hasher = serviceProvider.GetRequiredService<ISecretHasher>();
        var encoded = hasher.Hash("pin");

        // then
        encoded.Should().StartWith("$pbkdf2-sha256$i=1000,l=32$");
        hasher.Verify("pin", encoded).Succeeded.Should().BeTrue();
        serviceProvider.GetServices<ISecretHashAlgorithm>().Should().ContainSingle();
    }

    [Fact]
    public void should_register_secret_hasher_from_delegate()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddSecretHasher(options =>
        {
            options.Algorithm = SecretHashAlgorithms.Pbkdf2Sha256;
            options.Pbkdf2Sha256.Iterations = 1_000;
        });

        using var serviceProvider = services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ISecretHasher>().Hash("pin").Should().StartWith("$pbkdf2-sha256$i=1000,");
    }

    [Fact]
    public void should_register_secret_hasher_from_service_provider_delegate()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddSecretHasher(
            (options, _) =>
            {
                options.Algorithm = SecretHashAlgorithms.Pbkdf2Sha256;
                options.Pbkdf2Sha256.Iterations = 1_000;
            }
        );

        using var serviceProvider = services.BuildServiceProvider();

        // then
        serviceProvider.GetRequiredService<ISecretHasher>().Hash("pin").Should().StartWith("$pbkdf2-sha256$i=1000,");
    }

    [Fact]
    public void should_register_the_startup_check_once()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddSecretHasher(_ => { });
        services.AddSecretHasher(_ => { });

        // then
        services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Should()
            .ContainSingle(d => d.ImplementationType == typeof(SecretHasherStartupValidationService));
    }

    [Fact]
    public void should_reject_invalid_secret_hasher_options_on_resolution()
    {
        // given
        var services = new ServiceCollection();
        services.AddSecretHasher(options => options.Pbkdf2Sha256.Iterations = 0);

        using var serviceProvider = services.BuildServiceProvider();

        // then
        FluentActions
            .Invoking(() => serviceProvider.GetRequiredService<IOptions<SecretHasherOptions>>().Value)
            .Should()
            .Throw<OptionsValidationException>();
    }
}
