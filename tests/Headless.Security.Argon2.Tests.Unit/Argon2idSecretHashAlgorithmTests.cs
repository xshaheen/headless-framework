// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Security;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class Argon2idSecretHashAlgorithmTests : TestBase
{
    // Small enough to keep the suite fast; the default cost is covered by the startup check and the options defaults.
    private const int _LowMemorySize = 64;

    [Fact]
    public void should_match_libsodium_known_answer_vector()
    {
        // given — libsodium 1.0.22 test/default/pwhash_argon2id.c, tv() entry 6, expected output from
        // pwhash_argon2id.exp line 7. libsodium reads only the first 16 salt bytes, and its byte memlimit of
        // 1,631,659 is 1,593 KiB.
        var password = Convert.FromHexString("b540beb016a5366524d4605156493f9874514a5aa58818cd0c6dfffaa9e90205f17b");
        var salt = Convert.FromHexString("44071f6d181561670bda728d43fb79b4");
        var expected = Convert.FromHexString(
            "7fb72409b0987f8190c3729710e98c3f80c5a8727d425fdcde7f3644d467fe973f5b5fee683bd3fce812cb9ae5e9921a2d06c2f1"
                + "905e4e839692f2b934b682f11a2fe2b90482ea5dd234863516dba6f52dc0702d324ec77d860c2e181f84472bd7104fedce071f"
                + "fa93c5309494ad51623d214447a7b2b1462dc7d5d55a1f6fd5b54ce024118d86f0c6489d16545aaa87b6689dad9f2fb47fda98"
                + "94f8e12b87d978b483ccd4cc5fd9595cdc7a818452f915ce2f7df95ec12b1c72e3788d473441d884f9748eb14703c21b45d82f"
                + "d667b85f5b2d98c13303b3fe76285531a826b6fc0fe8e3dddecf"
        );
        var destination = new byte[expected.Length];

        // when
        Argon2idSecretHashAlgorithm.Derive(password, salt, memorySize: 1593, iterations: 1, destination);

        // then
        destination.Should().Equal(expected);
    }

    [Fact]
    public void should_produce_a_different_encoding_on_every_call_and_verify_each()
    {
        // given
        using var provider = _BuildProvider();
        var sut = provider.GetRequiredService<ISecretHasher>();

        // when
        var first = sut.Hash("123456");
        var second = sut.Hash("123456");

        // then
        first.Should().StartWith($"$argon2id$v=19$m={_LowMemorySize},t=1,p=1$");
        first.Should().NotBe(second);
        sut.Verify("123456", first).Should().Be(new SecretVerification(true, null));
        sut.Verify("123456", second).Should().Be(new SecretVerification(true, null));
        sut.Verify("654321", first).Should().Be(SecretVerification.Failed);
    }

    [Fact]
    public void should_encode_a_16_byte_salt_and_the_configured_hash_size()
    {
        using var provider = _BuildProvider();

        PhcString.TryParse(provider.GetRequiredService<ISecretHasher>().Hash("pin"), out var phc).Should().BeTrue();

        phc!.Salt.Length.Should().Be(16);
        phc.Hash.Length.Should().Be(32);
    }

    [Fact]
    public void should_rehash_to_the_configured_iterations_when_stored_iterations_are_lower()
    {
        // given
        using var weaker = _BuildProvider(iterations: 1);
        using var stronger = _BuildProvider(iterations: 2);
        var stored = weaker.GetRequiredService<ISecretHasher>().Hash("pin");
        var sut = stronger.GetRequiredService<ISecretHasher>();

        // when
        var result = sut.Verify("pin", stored);

        // then
        result.Succeeded.Should().BeTrue();
        result.Rehashed.Should().StartWith($"$argon2id$v=19$m={_LowMemorySize},t=2,p=1$");
        sut.Verify("pin", result.Rehashed!).Should().Be(new SecretVerification(true, null));
    }

    [Fact]
    public void should_upgrade_a_pbkdf2_hash_to_argon2id_on_verify()
    {
        // given
        using var legacy = _BuildProvider(algorithm: SecretHashAlgorithms.Pbkdf2Sha256);
        using var current = _BuildProvider();
        var stored = legacy.GetRequiredService<ISecretHasher>().Hash("pin");
        var sut = current.GetRequiredService<ISecretHasher>();

        // when
        var result = sut.Verify("pin", stored);

        // then
        stored.Should().StartWith("$pbkdf2-sha256$");
        result.Succeeded.Should().BeTrue();
        result.Rehashed.Should().StartWith("$argon2id$");
    }

    [Theory]
    [InlineData("$argon2id$v=19$m=64,t=1,p=4$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=16$m=64,t=1,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=64,t=1,p=1$c2hvcnRzYWx0MTI$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=262145,t=1,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=0,t=1,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$m=64,t=0,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$v=19$t=1,m=64,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    [InlineData("$argon2id$m=64,t=1,p=1$c29tZXNhbHRzb21lc2FsdA$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g")]
    public void should_refuse_unsupported_or_out_of_bounds_encodings_without_deriving(string encoded)
    {
        // given
        var sut = new Argon2idSecretHashAlgorithm(Options.Create(_Options()));
        PhcString.TryParse(encoded, out var phc).Should().BeTrue();

        // when
        var computed = sut.TryComputeHash("pin"u8, phc!, new byte[phc!.Hash.Length]);

        // then
        computed.Should().BeFalse();
    }

    [Fact]
    public async Task should_start_a_host_with_default_argon2id_options()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessSecretHasher(setup =>
            setup.Configure(o => o.CostCheck.Mode = SecretHasherCostCheckMode.Off).UseArgon2id()
        );
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);

        // then
        host.Services.GetRequiredService<ISecretHasher>()
            .Hash("pin")
            .Should()
            .StartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        await host.StopAsync(AbortToken);
    }

    [Fact]
    public void should_bind_argon2id_options_from_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Argon2id:MemorySize", "128"),
                new KeyValuePair<string, string?>("Argon2id:Iterations", "3"),
            ])
            .Build();
        var services = new ServiceCollection();

        // when
        services.AddHeadlessSecretHasher(setup => setup.UseArgon2id(configuration.GetRequiredSection("Argon2id")));
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<ISecretHasher>().Hash("pin").Should().StartWith("$argon2id$v=19$m=128,t=3,p=1$");
    }

    [Fact]
    public void should_keep_pbkdf2_registered_for_verification_when_argon2id_writes()
    {
        var services = new ServiceCollection();

        services.AddHeadlessSecretHasher(setup => setup.UseArgon2id());
        using var provider = services.BuildServiceProvider();

        provider
            .GetServices<ISecretHashAlgorithm>()
            .Select(a => a.Id)
            .Should()
            .BeEquivalentTo(SecretHashAlgorithms.Argon2id, SecretHashAlgorithms.Pbkdf2Sha256);
    }

    [Fact]
    public void should_reject_invalid_argon2id_options_on_resolution()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSecretHasher(setup => setup.UseArgon2id(o => o.MemorySize = 1));
        using var provider = services.BuildServiceProvider();

        // then
        FluentActions
            .Invoking(() => provider.GetRequiredService<IOptions<Argon2idHashOptions>>().Value)
            .Should()
            .Throw<OptionsValidationException>();
    }

    private static Argon2idHashOptions _Options(int iterations = 1)
    {
        return new Argon2idHashOptions
        {
            MemorySize = _LowMemorySize,
            Iterations = iterations,
            HashSize = 32,
        };
    }

    private static ServiceProvider _BuildProvider(int iterations = 1, string algorithm = SecretHashAlgorithms.Argon2id)
    {
        var services = new ServiceCollection();
        services.AddHeadlessSecretHasher(setup =>
        {
            if (string.Equals(algorithm, SecretHashAlgorithms.Pbkdf2Sha256, StringComparison.Ordinal))
            {
                setup.UsePbkdf2Sha256((Pbkdf2Sha256HashOptions o) => o.Iterations = 1_000);
            }
            else
            {
                setup.UseArgon2id(o =>
                {
                    o.MemorySize = _LowMemorySize;
                    o.Iterations = iterations;
                });
            }
        });

        return services.BuildServiceProvider();
    }
}
