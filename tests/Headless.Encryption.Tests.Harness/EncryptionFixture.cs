// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Hosting;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>What a key-store provider leaf supplies to run <see cref="EncryptionConformanceTests{TFixture}" />.</summary>
public interface IEncryptionFixture
{
    /// <summary>Chooses the key store under test, its key table named <paramref name="tableName" />.</summary>
    void ConfigureKeyStore(HeadlessEncryptionSetupBuilder setup, string tableName);

    /// <summary>Chooses the key store under test, pointed at a database that cannot be reached.</summary>
    void ConfigureUnreachableKeyStore(HeadlessEncryptionSetupBuilder setup);
}

public static class EncryptionFixtureExtensions
{
    public const string KeyOne = "kek-1";
    public const string KeyTwo = "kek-2";

    public static readonly string KeyOneValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
    public static readonly string KeyTwoValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 32).ToArray());

    /// <summary>A key table name no other test uses, so a test that touches every key stays alone.</summary>
    public static string UniqueTable() => "enc_" + Guid.NewGuid().ToString("N")[..16];

    /// <summary>
    /// Builds an independent host (its own service provider, key cache, clock, and connection pool) with the store
    /// under test, provisions its resources as a deploy step would, and returns it.
    /// </summary>
    public static async ValueTask<EncryptionHost> CreateHostAsync(
        this IEncryptionFixture fixture,
        string tableName,
        Action<LocalKeyEncryptionKeyOptions>? keys = null,
        CancellationToken cancellationToken = default
    )
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddHeadlessEncryption(setup =>
        {
            fixture.ConfigureKeyStore(setup, tableName);
            setup.UseLocalKey(keys ?? UseKeyOne);
            setup.ConfigureOptions(o => o.KeyCacheDuration = TimeSpan.FromMinutes(5));
        });

        // A host start only verifies; the test host creates the table and the host keys first, as a development host does.
        services.AddDeployStepsOnStartup(o => o.Seed = false);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken).ConfigureAwait(false);
        }

        return new EncryptionHost(provider, clock);
    }

    public static void UseKeyOne(LocalKeyEncryptionKeyOptions options)
    {
        options.Keys[KeyOne] = KeyOneValue;
        options.ActiveKeyId = KeyOne;
    }

    public static void UseBothKeysWithKeyTwoActive(LocalKeyEncryptionKeyOptions options)
    {
        options.Keys[KeyOne] = KeyOneValue;
        options.Keys[KeyTwo] = KeyTwoValue;
        options.ActiveKeyId = KeyTwo;
    }

    public static void UseKeyTwoOnly(LocalKeyEncryptionKeyOptions options)
    {
        options.Keys[KeyTwo] = KeyTwoValue;
        options.ActiveKeyId = KeyTwo;
    }
}

/// <summary>One independently built host.</summary>
public sealed class EncryptionHost(ServiceProvider services, FakeTimeProvider clock) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public FakeTimeProvider Clock => clock;

    public IFieldEncryptor Encryptor { get; } = services.GetRequiredService<IFieldEncryptor>();

    public IBlindIndexer BlindIndexer { get; } = services.GetRequiredService<IBlindIndexer>();

    public IDataKeyManager Manager { get; } = services.GetRequiredService<IDataKeyManager>();

    public IDataKeyStore Store { get; } = services.GetRequiredService<IDataKeyStore>();

    public ICurrentTenant CurrentTenant { get; } = services.GetRequiredService<ICurrentTenant>();

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
