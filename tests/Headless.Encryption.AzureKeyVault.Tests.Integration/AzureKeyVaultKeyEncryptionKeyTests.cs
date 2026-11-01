// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;
using Headless.Encryption;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<LowkeyVaultFixture>]
public sealed class AzureKeyVaultKeyEncryptionKeyTests(LowkeyVaultFixture fixture) : TestBase
{
    private const string _Purpose = "Beneficiary.NationalId";
    private const string _NationalId = "29001011234567";

    [Fact]
    public async Task should_unwrap_what_it_wrapped_and_name_the_exact_key_version()
    {
        var key = await fixture.CreateKeyAsync(_KeyName(), AbortToken);
        var provider = _Provider(key.Name);
        var context = _Context("charity-a");
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = await provider.WrapAsync(dataKey, context, AbortToken);
        var unwrapped = await provider.UnwrapAsync(wrapped, context, AbortToken);

        unwrapped.Should().Equal(dataKey);
        provider.Name.Should().Be("AzureKeyVault");
        wrapped.KekId.Should().EndWith($"/keys/{key.Name}/{key.Properties.Version}");
    }

    [Theory]
    [InlineData("charity-b", DataKeyKind.Data)]
    [InlineData("charity-a", DataKeyKind.Lookup)]
    public async Task should_refuse_a_wrapped_key_moved_to_another_context(string scope, DataKeyKind kind)
    {
        var key = await fixture.CreateKeyAsync(_KeyName(), AbortToken);
        var provider = _Provider(key.Name);
        var original = _Context("charity-a");
        var wrapped = await provider.WrapAsync(RandomNumberGenerator.GetBytes(32), original, AbortToken);
        var moved = new KeyWrapContext
        {
            Scope = scope,
            KeyId = original.KeyId,
            Kind = kind,
        };

        var act = () => provider.UnwrapAsync(wrapped, moved, AbortToken).AsTask();

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task should_wrap_with_the_new_version_after_rotation_and_still_unwrap_with_the_old_one()
    {
        var first = await fixture.CreateKeyAsync(_KeyName(), AbortToken);
        var provider = _Provider(first.Name);
        var context = _Context("charity-a");
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var before = await provider.WrapAsync(dataKey, context, AbortToken);

        var second = await fixture.CreateKeyAsync(first.Name, AbortToken);
        var after = await provider.WrapAsync(dataKey, context, AbortToken);

        second.Properties.Version.Should().NotBe(first.Properties.Version);
        before.KekId.Should().EndWith(first.Properties.Version);
        after.KekId.Should().EndWith(second.Properties.Version);
        (await provider.UnwrapAsync(before, context, AbortToken)).Should().Equal(dataKey);
        (await provider.UnwrapAsync(after, context, AbortToken)).Should().Equal(dataKey);
    }

    [Fact]
    public async Task should_refuse_a_key_wrapped_in_another_vault_or_without_a_version()
    {
        var key = await fixture.CreateKeyAsync(_KeyName(), AbortToken);
        var provider = _Provider(key.Name);
        var context = _Context("charity-a");
        var wrapped = await provider.WrapAsync(RandomNumberGenerator.GetBytes(32), context, AbortToken);
        var otherVault = new WrappedDataKey
        {
            KekId = $"https://other-vault.vault.azure.net/keys/{key.Name}/{key.Properties.Version}",
            Ciphertext = wrapped.Ciphertext,
        };
        var versionless = new WrappedDataKey
        {
            KekId = key
                .Id.GetLeftPart(UriPartial.Path)
                .Replace($"/{key.Properties.Version}", "", StringComparison.Ordinal),
            Ciphertext = wrapped.Ciphertext,
        };

        await FluentActions
            .Awaiting(() => provider.UnwrapAsync(otherVault, context, AbortToken).AsTask())
            .Should()
            .ThrowAsync<CryptographicException>()
            .WithMessage("*other-vault*");
        await FluentActions
            .Awaiting(() => provider.UnwrapAsync(versionless, context, AbortToken).AsTask())
            .Should()
            .ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task should_move_data_keys_from_the_local_key_to_key_vault_and_keep_old_values_readable()
    {
        var key = await fixture.CreateKeyAsync(_KeyName(), AbortToken);
        var store = new InMemoryDataKeyStore();
        var context = new FieldContext(_Purpose) { Scope = "charity-a" };
        EncryptedValue before;

        await using (var local = _Host(store, setup => setup.UseLocalKey(EncryptionFixtureExtensions.UseKeyOne)))
        {
            before = await local.GetRequiredService<IFieldEncryptor>().EncryptAsync(_NationalId, context, AbortToken);
        }

        await using (
            var migrating = _Host(
                store,
                setup =>
                    setup
                        .UseAzureKeyVault(o => o.KeyName = key.Name, _ => fixture.CreateKeyClient())
                        .KeepPreviousKeyEncryptionKey(previous =>
                            previous.UseLocalKey(EncryptionFixtureExtensions.UseKeyOne)
                        )
            )
        )
        {
            (await migrating.GetRequiredService<IFieldEncryptor>().DecryptStringAsync(before, context, AbortToken))
                .Should()
                .Be(_NationalId);
            (await migrating.GetRequiredService<IDataKeyManager>().RewrapAsync(AbortToken)).Should().Be(1);
        }

        store
            .Snapshot()
            .Should()
            .OnlyContain(k =>
                k.KekProvider == "AzureKeyVault" && k.WrappedKey.KekId.Contains(key.Name, StringComparison.Ordinal)
            );

        await using var vaultOnly = _Host(
            store,
            setup => setup.UseAzureKeyVault(o => o.KeyName = key.Name, _ => fixture.CreateKeyClient())
        );
        (await vaultOnly.GetRequiredService<IFieldEncryptor>().DecryptStringAsync(before, context, AbortToken))
            .Should()
            .Be(_NationalId);
    }

    private IKeyEncryptionKeyProvider _Provider(string keyName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fixture.CreateKeyClient());
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(new InMemoryDataKeyStore().AsExtension());
            setup.UseAzureKeyVault(o => o.KeyName = keyName);
        });

        return services.BuildServiceProvider().GetRequiredService<IKeyEncryptionKeyProvider>();
    }

    private static ServiceProvider _Host(InMemoryDataKeyStore store, Action<HeadlessEncryptionSetupBuilder> keys)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessEncryption(setup =>
        {
            setup.RegisterKeyStore(store.AsExtension());
            keys(setup);
        });

        return services.BuildServiceProvider();
    }

    private static KeyWrapContext _Context(string scope) =>
        new()
        {
            Scope = scope,
            KeyId = Guid.CreateVersion7(),
            Kind = DataKeyKind.Data,
        };

    private static string _KeyName() => "kek-" + Guid.NewGuid().ToString("N")[..12];
}
