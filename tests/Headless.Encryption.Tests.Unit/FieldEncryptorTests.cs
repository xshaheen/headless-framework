// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Encryption;
using Headless.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FieldEncryptorTests : TestBase
{
    private const string _Purpose = "Beneficiary.NationalId";
    private const string _NationalId = "29001011234567";

    private readonly InMemoryDataKeyStore _store = new();

    [Fact]
    public async Task should_round_trip_a_value_for_the_current_tenant()
    {
        using var host = new EncryptionTestHost(_store);
        using var _ = host.CurrentTenant.Change("charity-a");

        var encrypted = await host.Encryptor.EncryptAsync(_NationalId, new FieldContext(_Purpose), AbortToken);
        var decrypted = await host.Encryptor.DecryptAsync(encrypted, new FieldContext(_Purpose), AbortToken);

        decrypted.GetString().Should().Be(_NationalId);
        decrypted.KeyIsCurrent.Should().BeTrue();
        encrypted.Length.Should().Be(Encoding.UTF8.GetByteCount(_NationalId) + EncryptedValue.Overhead);
    }

    [Fact]
    public async Task should_write_the_frame_header_with_the_active_data_key_id()
    {
        using var host = new EncryptionTestHost(_store);

        var encrypted = await host.Encryptor.EncryptAsync(_NationalId, FieldContext.Host(_Purpose), AbortToken);

        var dataKey = _store.Snapshot().Single(k => k.Kind == DataKeyKind.Data);
        encrypted.Frame.Span[0].Should().Be(EncryptedValue.FormatVersion);
        encrypted.Algorithm.Should().Be(0x01);
        encrypted.KeyId.Should().Be(dataKey.KeyId);
        dataKey.Scope.Should().Be(FieldContext.HostScope);
        dataKey.Version.Should().Be(1);
    }

    [Fact]
    public async Task should_produce_different_frames_for_the_same_plaintext()
    {
        using var host = new EncryptionTestHost(_store);
        var context = FieldContext.Host(_Purpose);

        var first = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
        var second = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task should_refuse_a_value_copied_to_another_tenant()
    {
        using var host = new EncryptionTestHost(_store);
        var encrypted = await host.Encryptor.EncryptAsync(
            _NationalId,
            new FieldContext(_Purpose) { Scope = "charity-a" },
            AbortToken
        );
        await host.Manager.EnsureAsync("charity-b", AbortToken);

        var act = () =>
            host
                .Encryptor.DecryptAsync(encrypted, new FieldContext(_Purpose) { Scope = "charity-b" }, AbortToken)
                .AsTask();

        (await act.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.AuthenticationFailed);
    }

    [Fact]
    public async Task should_refuse_a_value_copied_to_another_purpose()
    {
        using var host = new EncryptionTestHost(_store);
        var encrypted = await host.Encryptor.EncryptAsync(_NationalId, FieldContext.Host(_Purpose), AbortToken);

        var act = () =>
            host.Encryptor.DecryptAsync(encrypted, FieldContext.Host("Beneficiary.Phone"), AbortToken).AsTask();

        (await act.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.AuthenticationFailed);
    }

    [Fact]
    public async Task should_require_the_same_binding_to_decrypt()
    {
        using var host = new EncryptionTestHost(_store);
        var rowA = new FieldContext(_Purpose) { Scope = FieldContext.HostScope, Binding = "row-a"u8.ToArray() };
        var rowB = new FieldContext(_Purpose) { Scope = FieldContext.HostScope, Binding = "row-b"u8.ToArray() };
        var encrypted = await host.Encryptor.EncryptAsync(_NationalId, rowA, AbortToken);

        (await host.Encryptor.DecryptStringAsync(encrypted, rowA, AbortToken)).Should().Be(_NationalId);

        var swapped = () => host.Encryptor.DecryptAsync(encrypted, rowB, AbortToken).AsTask();
        var unbound = () => host.Encryptor.DecryptAsync(encrypted, FieldContext.Host(_Purpose), AbortToken).AsTask();

        await swapped.Should().ThrowAsync<FieldDecryptionException>();
        await unbound.Should().ThrowAsync<FieldDecryptionException>();
    }

    [Fact]
    public async Task should_refuse_a_frame_with_any_flipped_bit()
    {
        using var host = new EncryptionTestHost(_store);
        var context = FieldContext.Host(_Purpose);
        var frame = (await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken)).ToArray();

        for (var i = 0; i < frame.Length; i++)
        {
            var tampered = (byte[])frame.Clone();
            tampered[i] ^= 0x01;

            // The version byte makes the bytes no frame at all; any other byte fails as a decryption.
            if (!EncryptedValue.TryFromBytes(tampered, out var value))
            {
                i.Should().Be(0);

                continue;
            }

            var act = () => host.Encryptor.DecryptAsync(value, context, AbortToken).AsTask();

            await act.Should().ThrowAsync<FieldDecryptionException>($"byte {i} was flipped");
        }
    }

    [Fact]
    public async Task should_report_an_unknown_algorithm()
    {
        using var host = new EncryptionTestHost(_store);
        var context = FieldContext.Host(_Purpose);
        var frame = (await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken)).ToArray();
        frame[1] = 0x7F;

        var act = () => host.Encryptor.DecryptAsync(EncryptedValue.FromBytes(frame), context, AbortToken).AsTask();

        (await act.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.UnsupportedAlgorithm);
    }

    [Fact]
    public async Task should_throw_when_no_scope_and_no_current_tenant()
    {
        using var host = new EncryptionTestHost(_store);

        var act = () => host.Encryptor.EncryptAsync(_NationalId, new FieldContext(_Purpose), AbortToken).AsTask();

        await act.Should().ThrowAsync<MissingTenantContextException>();
        _store.Snapshot().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" padded")]
    [InlineData("nul\0char")]
    public async Task should_refuse_a_scope_that_is_not_portable_key_text(string scope)
    {
        using var host = new EncryptionTestHost(_store);

        var act = () =>
            host.Encryptor.EncryptAsync(_NationalId, new FieldContext(_Purpose) { Scope = scope }, AbortToken).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_refuse_a_scope_longer_than_the_column()
    {
        using var host = new EncryptionTestHost(_store);
        var scope = new string('t', DataKeyFieldLimits.ScopeMaxLength + 1);

        var act = () =>
            host.Encryptor.EncryptAsync(_NationalId, new FieldContext(_Purpose) { Scope = scope }, AbortToken).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_create_one_data_key_when_first_encryptions_race()
    {
        using var hostA = new EncryptionTestHost(_store);
        using var hostB = new EncryptionTestHost(_store);
        var context = new FieldContext(_Purpose) { Scope = "charity-race" };

        var frames = await Task.WhenAll(
            Enumerable
                .Range(0, 40)
                .Select(i =>
                    Task.Run(
                        async () =>
                            await (i % 2 == 0 ? hostA : hostB).Encryptor.EncryptAsync(_NationalId, context, AbortToken),
                        AbortToken
                    )
                )
        );

        var keys = _store
            .Snapshot()
            .Where(k => string.Equals(k.Scope, "charity-race", StringComparison.Ordinal))
            .ToList();
        keys.Should().ContainSingle();
        frames.Select(f => f.KeyId).Distinct().Should().ContainSingle().Which.Should().Be(keys[0].KeyId);

        foreach (var frame in frames)
        {
            (await hostB.Encryptor.DecryptStringAsync(frame, context, AbortToken)).Should().Be(_NationalId);
        }
    }

    [Fact]
    public async Task should_report_a_missing_key_after_its_scope_is_shredded()
    {
        using var host = new EncryptionTestHost(_store);
        var shredded = new FieldContext(_Purpose) { Scope = "charity-gone" };
        var kept = new FieldContext(_Purpose) { Scope = "charity-kept" };
        var gone = await host.Encryptor.EncryptAsync(_NationalId, shredded, AbortToken);
        var stays = await host.Encryptor.EncryptAsync(_NationalId, kept, AbortToken);

        (await host.Manager.ShredAsync("charity-gone", AbortToken)).Should().Be(1);

        var act = () => host.Encryptor.DecryptAsync(gone, shredded, AbortToken).AsTask();
        (await act.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.KeyNotFound);
        (await host.Encryptor.DecryptStringAsync(stays, kept, AbortToken)).Should().Be(_NationalId);
    }

    [Fact]
    public async Task should_refuse_new_keys_for_a_shredded_scope_until_it_is_reopened()
    {
        using var host = new EncryptionTestHost(_store);
        var context = new FieldContext(_Purpose) { Scope = "charity-closed" };
        var before = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
        await host.Manager.ShredAsync("charity-closed", AbortToken);

        var encrypt = () => host.Encryptor.EncryptAsync(_NationalId, context, AbortToken).AsTask();
        var rotate = () => host.Manager.RotateAsync("charity-closed", DataKeyKind.Data, AbortToken);

        (await encrypt.Should().ThrowAsync<DataKeyScopeShreddedException>()).Which.Scope.Should().Be("charity-closed");
        await rotate.Should().ThrowAsync<DataKeyScopeShreddedException>();
        _store.Snapshot().Should().NotContain(k => k.Scope == "charity-closed");

        await host.Manager.EnsureAsync("charity-closed", AbortToken);
        var after = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);

        after.KeyId.Should().NotBe(before.KeyId);
        (await host.Encryptor.DecryptStringAsync(after, context, AbortToken)).Should().Be(_NationalId);
        var old = () => host.Encryptor.DecryptAsync(before, context, AbortToken).AsTask();
        (await old.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.KeyNotFound);
    }

    [Fact]
    public async Task should_stop_decrypting_on_another_host_once_its_cached_key_expires_after_a_shred()
    {
        using var hostA = new EncryptionTestHost(_store, options: o => o.KeyCacheDuration = TimeSpan.FromMinutes(5));
        using var hostB = new EncryptionTestHost(_store, options: o => o.KeyCacheDuration = TimeSpan.FromMinutes(5));
        var context = new FieldContext(_Purpose) { Scope = "charity-ttl" };
        var encrypted = await hostA.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
        (await hostB.Encryptor.DecryptStringAsync(encrypted, context, AbortToken)).Should().Be(_NationalId);

        await hostA.Manager.ShredAsync("charity-ttl", AbortToken);

        // Host B still holds the unwrapped key until its cache entry expires.
        (await hostB.Encryptor.DecryptStringAsync(encrypted, context, AbortToken))
            .Should()
            .Be(_NationalId);

        hostB.Clock.Advance(TimeSpan.FromMinutes(5));

        var act = () => hostB.Encryptor.DecryptAsync(encrypted, context, AbortToken).AsTask();
        (await act.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.KeyNotFound);
    }

    [Fact]
    public async Task should_encrypt_with_the_new_key_and_flag_old_values_after_rotation()
    {
        using var host = new EncryptionTestHost(_store);
        var context = new FieldContext(_Purpose) { Scope = "charity-rotate" };
        var old = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);

        var newKeyId = await host.Manager.RotateAsync("charity-rotate", DataKeyKind.Data, AbortToken);
        var fresh = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);

        newKeyId.Should().NotBe(old.KeyId);
        fresh.KeyId.Should().Be(newKeyId);

        var oldDecryption = await host.Encryptor.DecryptAsync(old, context, AbortToken);
        oldDecryption.GetString().Should().Be(_NationalId);
        oldDecryption.KeyIsCurrent.Should().BeFalse();
        (await host.Encryptor.DecryptAsync(fresh, context, AbortToken)).KeyIsCurrent.Should().BeTrue();
        _store
            .Snapshot()
            .Where(k =>
                string.Equals(k.Scope, "charity-rotate", StringComparison.Ordinal) && k.Kind == DataKeyKind.Data
            )
            .Select(k => k.Version)
            .Should()
            .BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task should_keep_old_values_readable_after_the_key_encryption_key_is_rotated_and_rewrapped()
    {
        var context = new FieldContext(_Purpose) { Scope = "charity-rewrap" };
        EncryptedValue encrypted;

        using (var before = new EncryptionTestHost(_store))
        {
            encrypted = await before.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
            await before.Manager.EnsureAsync("charity-other", AbortToken);
        }

        using (var rotating = new EncryptionTestHost(_store, keys: _BothKeysActive(EncryptionTestHost.KeyTwo)))
        {
            (await rotating.Manager.RewrapAsync(AbortToken)).Should().Be(_store.Snapshot().Count);
            (await rotating.Manager.RewrapAsync(AbortToken)).Should().Be(0);
        }

        _store.Snapshot().Should().OnlyContain(k => k.WrappedKey.KekId == EncryptionTestHost.KeyTwo);

        using var after = new EncryptionTestHost(
            _store,
            keys: o =>
            {
                o.Keys[EncryptionTestHost.KeyTwo] = EncryptionTestHost.KeyTwoValue;
                o.ActiveKeyId = EncryptionTestHost.KeyTwo;
            }
        );
        (await after.Encryptor.DecryptStringAsync(encrypted, context, AbortToken)).Should().Be(_NationalId);
    }

    [Fact]
    public async Task should_create_a_data_and_a_lookup_key_once_when_ensured_twice()
    {
        using var host = new EncryptionTestHost(_store);

        await host.Manager.EnsureAsync("charity-onboard", AbortToken);
        await host.Manager.EnsureAsync("charity-onboard", AbortToken);

        _store
            .Snapshot()
            .Where(k => string.Equals(k.Scope, "charity-onboard", StringComparison.Ordinal))
            .Select(k => (k.Kind, k.Version))
            .Should()
            .BeEquivalentTo([(DataKeyKind.Data, 1), (DataKeyKind.Lookup, 1)]);
    }

    [Fact]
    public async Task should_refuse_the_default_encrypted_value()
    {
        using var host = new EncryptionTestHost(_store);

        var act = () => host.Encryptor.DecryptAsync(default, FieldContext.Host(_Purpose), AbortToken).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_rewrap_keys_across_several_pages()
    {
        using (var before = new EncryptionTestHost(_store))
        {
            for (var i = 0; i < 60; i++)
            {
                await before.Manager.EnsureAsync($"charity-{i:D3}", AbortToken);
            }
        }

        using var rotating = new EncryptionTestHost(_store, keys: _BothKeysActive(EncryptionTestHost.KeyTwo));

        (await rotating.Manager.RewrapAsync(AbortToken)).Should().Be(120);
        _store.Snapshot().Should().OnlyContain(k => k.WrappedKey.KekId == EncryptionTestHost.KeyTwo);
    }

    [Fact]
    public async Task should_refuse_to_rotate_an_undefined_key_kind()
    {
        using var host = new EncryptionTestHost(_store);

        var act = () => host.Manager.RotateAsync("charity-a", (DataKeyKind)9, AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_start_a_rotation_at_version_one_for_a_scope_without_keys()
    {
        using var host = new EncryptionTestHost(_store);

        var keyId = await host.Manager.RotateAsync("charity-new", DataKeyKind.Lookup, AbortToken);

        _store
            .Snapshot()
            .Should()
            .ContainSingle(k => k.KeyId == keyId && k.Version == 1 && k.Kind == DataKeyKind.Lookup);
    }

    [Fact]
    public async Task should_refuse_a_key_wrapped_by_another_provider()
    {
        using var host = new EncryptionTestHost(_store);
        var encrypted = await host.Encryptor.EncryptAsync(_NationalId, FieldContext.Host(_Purpose), AbortToken);
        var stored = _store.Snapshot().Single();
        await _store.ReplaceWrappedKeyAsync(stored.KeyId, "AzureKeyVault", stored.WrappedKey, AbortToken);
        using var fresh = new EncryptionTestHost(_store);

        var act = () => fresh.Encryptor.DecryptAsync(encrypted, FieldContext.Host(_Purpose), AbortToken).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AzureKeyVault*Local*");
    }

    private static Action<LocalKeyEncryptionKeyOptions> _BothKeysActive(string active) =>
        o =>
        {
            o.Keys[EncryptionTestHost.KeyOne] = EncryptionTestHost.KeyOneValue;
            o.Keys[EncryptionTestHost.KeyTwo] = EncryptionTestHost.KeyTwoValue;
            o.ActiveKeyId = active;
        };
}
