// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>One independently built host over a shared key store: its own key cache, clock, and local keys.</summary>
public sealed class EncryptionTestHost : IDisposable
{
    public const string KeyOne = "kek-1";
    public const string KeyTwo = "kek-2";

    public static readonly string KeyOneValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
    public static readonly string KeyTwoValue = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 32).ToArray());

    private readonly ServiceProvider _services;

    public EncryptionTestHost(
        InMemoryDataKeyStore store,
        Action<LocalKeyEncryptionKeyOptions>? keys = null,
        Action<EncryptionOptions>? options = null,
        Action<HeadlessEncryptionSetupBuilder>? keyEncryptionKeys = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(store.AsExtension());
            if (keyEncryptionKeys is not null)
            {
                keyEncryptionKeys(setup);
            }
            else
            {
                setup.UseLocalKey(keys ?? UseKeyOne);
            }

            if (options is not null)
            {
                setup.ConfigureOptions(options);
            }
        });

        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public static void UseKeyOne(LocalKeyEncryptionKeyOptions options)
    {
        options.Keys[KeyOne] = KeyOneValue;
        options.ActiveKeyId = KeyOne;
    }

    public FakeTimeProvider Clock { get; } =
        new(DateTimeOffset.Parse("2026-10-30T00:00:00Z", CultureInfo.InvariantCulture));

    public IServiceProvider Services => _services;

    public IFieldEncryptor Encryptor => _services.GetRequiredService<IFieldEncryptor>();

    public IDataKeyManager Manager => _services.GetRequiredService<IDataKeyManager>();

    public ICurrentTenant CurrentTenant => _services.GetRequiredService<ICurrentTenant>();

    public void Dispose() => _services.Dispose();
}
