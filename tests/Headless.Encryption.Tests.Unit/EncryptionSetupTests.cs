// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class EncryptionSetupTests : TestBase
{
    private readonly InMemoryDataKeyStore _store = new();

    [Fact]
    public void should_require_a_key_encryption_key()
    {
        var act = () =>
            new ServiceCollection().AddHeadlessEncryption(setup => setup.RegisterKeyStore(_store.AsExtension()));

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one key-encryption key*UseLocalKey*");
    }

    [Fact]
    public void should_refuse_two_key_encryption_keys()
    {
        var act = () =>
            new ServiceCollection().AddHeadlessEncryption(setup =>
            {
                setup.RegisterKeyStore(_store.AsExtension());
                setup.UseLocalKey(_Keys).UseLocalKey(_Keys);
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("*Multiple key-encryption keys*");
    }

    [Fact]
    public void should_require_a_key_store()
    {
        var act = () => new ServiceCollection().AddHeadlessEncryption(setup => setup.UseLocalKey(_Keys));

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one storage provider*UsePostgreSql*");
    }

    [Fact]
    public void should_refuse_a_second_registration()
    {
        var services = new ServiceCollection();
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(_store.AsExtension());
            setup.UseLocalKey(_Keys);
        });

        var act = () =>
            services.AddHeadlessEncryption(setup =>
            {
                setup.RegisterKeyStore(_store.AsExtension());
                setup.UseLocalKey(_Keys);
            });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void should_bind_the_local_key_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ActiveKeyId"] = EncryptionTestHost.KeyOne,
                    [$"Keys:{EncryptionTestHost.KeyOne}"] = EncryptionTestHost.KeyOneValue,
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(_store.AsExtension());
            setup.UseLocalKey(configuration);
        });
        using var provider = services.BuildServiceProvider();

        var kek = provider.GetRequiredService<IKeyEncryptionKeyProvider>();

        kek.Name.Should().Be("Local");
        provider
            .GetRequiredService<IOptions<LocalKeyEncryptionKeyOptions>>()
            .Value.ActiveKeyId.Should()
            .Be(EncryptionTestHost.KeyOne);
    }

    [Fact]
    public void should_configure_the_local_key_with_access_to_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new LocalKeyMaterial(EncryptionTestHost.KeyOneValue));
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(_store.AsExtension());
            setup.UseLocalKey(
                (options, sp) =>
                {
                    options.Keys[EncryptionTestHost.KeyOne] = sp.GetRequiredService<LocalKeyMaterial>().Value;
                    options.ActiveKeyId = EncryptionTestHost.KeyOne;
                }
            );
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IKeyEncryptionKeyProvider>().Name.Should().Be("Local");
    }

    [Fact]
    public void should_fail_options_validation_for_a_bad_local_key()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(_store.AsExtension());
            setup.UseLocalKey(o =>
            {
                o.Keys["k"] = "short";
                o.ActiveKeyId = "k";
            });
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IKeyEncryptionKeyProvider>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_register_the_host_keys_resource_and_the_key_warm_up()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(_store.AsExtension());
            setup.UseLocalKey(_Keys);
        });
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IResourceProvisioner>().Should().ContainSingle(p => p.Kind == "EncryptionKeys");
        provider.GetServices<IWarmup>().Should().ContainSingle(w => w.Name == "KeyEncryptionKey");
    }

    [Fact]
    public void should_compose_repeated_option_calls_in_order()
    {
        var services = new ServiceCollection();
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(_store.AsExtension());
            setup
                .UseLocalKey(_Keys)
                .ConfigureOptions(o => o.KeyCacheSize = 5)
                .ConfigureOptions(o => o.KeyCacheDuration = TimeSpan.FromMinutes(2));
        });
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<EncryptionOptions>>().Value;

        options.KeyCacheSize.Should().Be(5);
        options.KeyCacheDuration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void should_reject_a_non_positive_cache()
    {
        var validator = new EncryptionOptionsValidator();

        validator.Validate(new EncryptionOptions { KeyCacheSize = 0 }).IsValid.Should().BeFalse();
        validator.Validate(new EncryptionOptions { KeyCacheDuration = TimeSpan.Zero }).IsValid.Should().BeFalse();
    }

    private static void _Keys(LocalKeyEncryptionKeyOptions options)
    {
        options.Keys[EncryptionTestHost.KeyOne] = EncryptionTestHost.KeyOneValue;
        options.ActiveKeyId = EncryptionTestHost.KeyOne;
    }

    private sealed record LocalKeyMaterial(string Value);
}
