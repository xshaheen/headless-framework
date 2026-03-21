// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Setup;

public sealed class SecuritySetupTests
{
    [Fact]
    public void add_string_encryption_service_should_be_idempotent()
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
    public void add_string_hash_service_should_be_idempotent()
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
        services.AddStringHashService(configuration.GetRequiredSection("Security:One"));
        services.AddStringHashService(configuration.GetRequiredSection("Security:Two"));

        using var serviceProvider = services.BuildServiceProvider();
        var hashOptions = serviceProvider.GetRequiredService<IOptions<StringHashOptions>>().Value;

        // then
        hashOptions.DefaultSalt.Should().Be("FirstSalt");
        hashOptions.Iterations.Should().Be(700000);
    }
}
