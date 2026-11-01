// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Encryption;
using Headless.Testing.Tests;

namespace Tests;

public sealed class LocalKeyEncryptionKeyProviderTests : TestBase
{
    private static readonly KeyWrapContext _Context = new()
    {
        Scope = "charity-a",
        KeyId = Guid.CreateVersion7(),
        Kind = DataKeyKind.Data,
    };

    [Fact]
    public async Task should_unwrap_what_it_wrapped_under_the_active_key_id()
    {
        var provider = _Provider(EncryptionTestHost.KeyOne);
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = await provider.WrapAsync(dataKey, _Context, AbortToken);
        var unwrapped = await provider.UnwrapAsync(wrapped, _Context, AbortToken);

        wrapped.KekId.Should().Be(EncryptionTestHost.KeyOne);
        wrapped.Ciphertext.Should().HaveCount(12 + 32 + 16);
        unwrapped.Should().Equal(dataKey);
    }

    [Theory]
    [InlineData("charity-b", null)]
    [InlineData(null, DataKeyKind.Lookup)]
    public async Task should_refuse_a_wrapped_key_moved_to_another_context(string? scope, DataKeyKind? kind)
    {
        var provider = _Provider(EncryptionTestHost.KeyOne);
        var wrapped = await provider.WrapAsync(RandomNumberGenerator.GetBytes(32), _Context, AbortToken);
        var moved = new KeyWrapContext
        {
            Scope = scope ?? _Context.Scope,
            KeyId = _Context.KeyId,
            Kind = kind ?? _Context.Kind,
        };

        var act = () => provider.UnwrapAsync(wrapped, moved, AbortToken).AsTask();

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task should_unwrap_with_a_retired_key_that_is_still_configured()
    {
        var old = _Provider(EncryptionTestHost.KeyOne);
        var wrapped = await old.WrapAsync(RandomNumberGenerator.GetBytes(32), _Context, AbortToken);
        var rotated = _Provider(EncryptionTestHost.KeyTwo);

        var unwrapped = await rotated.UnwrapAsync(wrapped, _Context, AbortToken);
        var rewrapped = await rotated.WrapAsync(unwrapped, _Context, AbortToken);

        rewrapped.KekId.Should().Be(EncryptionTestHost.KeyTwo);
    }

    [Fact]
    public async Task should_refuse_an_unknown_key_id()
    {
        var provider = _Provider(EncryptionTestHost.KeyOne);
        var wrapped = await provider.WrapAsync(RandomNumberGenerator.GetBytes(32), _Context, AbortToken);
        var unknown = new WrappedDataKey { KekId = "kek-gone", Ciphertext = wrapped.Ciphertext };

        var act = () => provider.UnwrapAsync(unknown, _Context, AbortToken).AsTask();

        await act.Should().ThrowAsync<CryptographicException>().WithMessage("*kek-gone*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("AAAA")]
    public void should_reject_a_key_that_is_not_32_bytes_of_base64(string key)
    {
        LocalKeyEncryptionKeyProvider.TryDecodeKey(key, out _).Should().BeFalse();

        var validator = new LocalKeyEncryptionKeyOptionsValidator();
        var options = new LocalKeyEncryptionKeyOptions { ActiveKeyId = "k" };
        options.Keys["k"] = key;

        validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_reject_an_active_key_id_that_names_no_key()
    {
        var options = new LocalKeyEncryptionKeyOptions { ActiveKeyId = "missing" };
        options.Keys[EncryptionTestHost.KeyOne] = EncryptionTestHost.KeyOneValue;

        new LocalKeyEncryptionKeyOptionsValidator().Validate(options).IsValid.Should().BeFalse();
    }

    private static LocalKeyEncryptionKeyProvider _Provider(string active)
    {
        var options = new LocalKeyEncryptionKeyOptions { ActiveKeyId = active };
        options.Keys[EncryptionTestHost.KeyOne] = EncryptionTestHost.KeyOneValue;
        options.Keys[EncryptionTestHost.KeyTwo] = EncryptionTestHost.KeyTwoValue;

        return new LocalKeyEncryptionKeyProvider(options);
    }
}
