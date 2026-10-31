// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class KeyEncryptionKeyMigrationTests : TestBase
{
    private const string _Purpose = "Beneficiary.NationalId";
    private const string _NationalId = "29001011234567";

    private readonly InMemoryDataKeyStore _store = new();

    [Fact]
    public async Task should_move_every_key_to_the_new_provider_while_the_previous_one_still_unwraps()
    {
        var context = new FieldContext(_Purpose) { Scope = "charity-a" };
        EncryptedValue before;

        using (var local = new EncryptionTestHost(_store))
        {
            before = await local.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
            await local.Manager.EnsureAsync("charity-b", AbortToken);
        }

        using (var migrating = new EncryptionTestHost(_store, keyEncryptionKeys: _ServiceWithPreviousLocal))
        {
            // Old keys still unwrap through the previous provider; new keys go to the active one.
            (await migrating.Encryptor.DecryptStringAsync(before, context, AbortToken))
                .Should()
                .Be(_NationalId);
            await migrating.Manager.EnsureAsync("charity-c", AbortToken);
            _store
                .Snapshot()
                .Where(k => string.Equals(k.Scope, "charity-c", StringComparison.Ordinal))
                .Should()
                .OnlyContain(k => k.KekProvider == TestKeyEncryptionKeyProvider.ProviderName);

            (await migrating.Manager.RewrapAsync(AbortToken)).Should().Be(3);
            (await migrating.Manager.RewrapAsync(AbortToken)).Should().Be(0);
        }

        _store.Snapshot().Should().OnlyContain(k => k.KekProvider == TestKeyEncryptionKeyProvider.ProviderName);

        using var after = new EncryptionTestHost(
            _store,
            keyEncryptionKeys: static setup =>
                setup.RegisterKeyEncryptionKeyProvider(TestKeyEncryptionKeyProvider.AsExtension())
        );
        (await after.Encryptor.DecryptStringAsync(before, context, AbortToken)).Should().Be(_NationalId);
    }

    [Fact]
    public async Task should_name_the_missing_provider_when_a_key_was_wrapped_by_one_no_longer_configured()
    {
        var context = FieldContext.Host(_Purpose);
        EncryptedValue before;

        using (var local = new EncryptionTestHost(_store))
        {
            before = await local.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
        }

        using var service = new EncryptionTestHost(
            _store,
            keyEncryptionKeys: static setup =>
                setup.RegisterKeyEncryptionKeyProvider(TestKeyEncryptionKeyProvider.AsExtension())
        );
        var act = () => service.Encryptor.DecryptAsync(before, context, AbortToken).AsTask();

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'Local'*KeepPreviousKeyEncryptionKey*");
    }

    [Fact]
    public void should_refuse_a_previous_provider_named_like_the_active_one()
    {
        using var host = new EncryptionTestHost(
            _store,
            keyEncryptionKeys: static setup =>
                setup
                    .UseLocalKey(EncryptionTestHost.UseKeyOne)
                    .KeepPreviousKeyEncryptionKey(previous => previous.UseLocalKey(EncryptionTestHost.UseKeyOne))
        );

        var act = () => host.Services.GetRequiredService<IFieldEncryptor>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Local'*");
    }

    [Fact]
    public void should_refuse_a_previous_block_without_a_key_encryption_key_or_with_a_store()
    {
        var empty = () =>
            new ServiceCollection().AddHeadlessEncryption(setup =>
            {
                setup.RegisterKeyStore(_store.AsExtension());
                setup.UseLocalKey(EncryptionTestHost.UseKeyOne).KeepPreviousKeyEncryptionKey(static _ => { });
            });
        var withStore = () =>
            new ServiceCollection().AddHeadlessEncryption(setup =>
            {
                setup.RegisterKeyStore(_store.AsExtension());
                setup
                    .UseLocalKey(EncryptionTestHost.UseKeyOne)
                    .KeepPreviousKeyEncryptionKey(previous =>
                    {
                        previous.RegisterKeyStore(_store.AsExtension());
                        previous.RegisterKeyEncryptionKeyProvider(TestKeyEncryptionKeyProvider.AsExtension());
                    });
            });

        empty.Should().Throw<InvalidOperationException>().WithMessage("*without a key-encryption key*");
        withStore.Should().Throw<InvalidOperationException>().WithMessage("*only takes key-encryption-key providers*");
    }

    private static void _ServiceWithPreviousLocal(HeadlessEncryptionSetupBuilder setup)
    {
        setup.RegisterKeyEncryptionKeyProvider(TestKeyEncryptionKeyProvider.AsExtension());
        setup.KeepPreviousKeyEncryptionKey(static previous => previous.UseLocalKey(EncryptionTestHost.UseKeyOne));
    }
}
