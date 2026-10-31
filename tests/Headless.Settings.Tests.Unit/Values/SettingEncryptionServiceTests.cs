// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Encryption;
using Headless.Settings;
using Headless.Settings.Resources;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Values;

public sealed class SettingEncryptionServiceTests : TestBase
{
    private static readonly SettingDefinition _Definition = new("Smtp.Password", isEncrypted: true);
    private readonly ISettingErrorsDescriptor _errors = new DefaultSettingErrorsDescriptor();
    private readonly RecordingFieldEncryptor _encryptor = new();

    [Fact]
    public async Task should_round_trip_a_global_value_under_the_host_scope()
    {
        // given
        var sut = _CreateService(_encryptor);

        // when
        var encrypted = await sut.EncryptAsync(
            _Definition,
            "s3cret",
            SettingValueProviderNames.Global,
            providerKey: null,
            AbortToken
        );
        var decrypted = await sut.DecryptAsync(
            _Definition,
            encrypted,
            SettingValueProviderNames.Global,
            providerKey: null,
            AbortToken
        );

        // then
        encrypted.Should().NotBe("s3cret");
        EncryptedValue.TryParse(encrypted, out _).Should().BeTrue();
        decrypted.Should().Be("s3cret");
        _encryptor.Contexts.Should().HaveCount(2);
        _encryptor
            .Contexts.Should()
            .AllSatisfy(c =>
            {
                c.Scope.Should().Be(FieldContext.HostScope);
                c.Purpose.Should().Be("settings:Smtp.Password");
            });
    }

    [Fact]
    public async Task should_use_the_host_scope_for_user_values()
    {
        // given
        var sut = _CreateService(_encryptor);

        // when
        await sut.EncryptAsync(_Definition, "s3cret", SettingValueProviderNames.User, "user-1", AbortToken);

        // then
        _encryptor.Contexts.Should().ContainSingle().Which.Scope.Should().Be(FieldContext.HostScope);
    }

    [Fact]
    public async Task should_use_the_tenant_scope_for_tenant_values()
    {
        // given
        var sut = _CreateService(_encryptor);

        // when
        await sut.EncryptAsync(_Definition, "s3cret", SettingValueProviderNames.Tenant, "tenant-1", AbortToken);

        // then
        _encryptor.Contexts.Should().ContainSingle().Which.Scope.Should().Be("tenant-1");
    }

    [Fact]
    public async Task should_leave_the_scope_to_the_current_tenant_when_the_tenant_key_is_null()
    {
        // given
        var sut = _CreateService(_encryptor);

        // when
        await sut.EncryptAsync(_Definition, "s3cret", SettingValueProviderNames.Tenant, providerKey: null, AbortToken);

        // then
        _encryptor.Contexts.Should().ContainSingle().Which.Scope.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task should_pass_empty_values_through_without_an_encryptor(string? value)
    {
        // given
        var sut = _CreateService(fieldEncryptor: null);

        // when
        var encrypted = await sut.EncryptAsync(_Definition, value, SettingValueProviderNames.Global, null, AbortToken);
        var decrypted = await sut.DecryptAsync(_Definition, value, SettingValueProviderNames.Global, null, AbortToken);

        // then
        encrypted.Should().Be(value);
        decrypted.Should().Be(value);
    }

    [Fact]
    public async Task should_name_add_headless_encryption_when_no_encryptor_is_registered()
    {
        // given
        var sut = _CreateService(fieldEncryptor: null);

        // when
        var act = async () =>
            await sut.EncryptAsync(_Definition, "s3cret", SettingValueProviderNames.Global, null, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Smtp.Password*AddHeadlessEncryption*");
    }

    [Fact]
    public async Task should_report_decryption_failed_when_the_stored_value_is_not_a_frame()
    {
        // given
        var sut = _CreateService(_encryptor);

        // when
        var act = async () =>
            await sut.DecryptAsync(_Definition, "not-a-frame", SettingValueProviderNames.Global, null, AbortToken);

        // then
        var assertions = await act.Should().ThrowAsync<ConflictException>();
        assertions
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(_errors.DecryptionFailed(_Definition.Name));
    }

    [Fact]
    public async Task should_report_decryption_failed_when_the_key_is_gone()
    {
        // given
        var sut = _CreateService(_encryptor);
        var encrypted = await sut.EncryptAsync(
            _Definition,
            "s3cret",
            SettingValueProviderNames.Tenant,
            "tenant-1",
            AbortToken
        );
        _encryptor.Failure = new FieldDecryptionException(FieldDecryptionFailure.KeyNotFound, "Shredded.");

        // when
        var act = async () =>
            await sut.DecryptAsync(_Definition, encrypted, SettingValueProviderNames.Tenant, "tenant-1", AbortToken);

        // then
        var assertions = await act.Should().ThrowAsync<ConflictException>();
        assertions
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(_errors.DecryptionFailed(_Definition.Name));
    }

    [Fact]
    public async Task should_rethrow_when_the_encryptor_fails()
    {
        // given
        var encryptor = Substitute.For<IFieldEncryptor>();
        encryptor
            .EncryptAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<FieldContext>(), Arg.Any<CancellationToken>())
            .Returns<EncryptedValue>(_ => throw new DataKeyScopeShreddedException("tenant-1"));
        var sut = _CreateService(encryptor);

        // when
        var act = async () =>
            await sut.EncryptAsync(_Definition, "s3cret", SettingValueProviderNames.Tenant, "tenant-1", AbortToken);

        // then - a shredded tenant is not a decryption conflict, so the caller sees the encryptor's own error
        await act.Should().ThrowAsync<DataKeyScopeShreddedException>();
    }

    private SettingEncryptionService _CreateService(IFieldEncryptor? fieldEncryptor)
    {
        return new SettingEncryptionService(_errors, NullLogger<SettingEncryptionService>.Instance, fieldEncryptor);
    }

    /// <summary>Frames the plaintext behind a zeroed header and records each context it is asked to use.</summary>
    private sealed class RecordingFieldEncryptor : IFieldEncryptor
    {
        public List<FieldContext> Contexts { get; } = [];

        public FieldDecryptionException? Failure { get; set; }

        public ValueTask<EncryptedValue> EncryptAsync(
            ReadOnlyMemory<byte> plaintext,
            FieldContext context,
            CancellationToken cancellationToken = default
        )
        {
            Contexts.Add(context);

            var frame = new byte[EncryptedValue.Overhead + plaintext.Length];
            frame[0] = EncryptedValue.FormatVersion;
            plaintext.Span.CopyTo(frame.AsSpan(EncryptedValue.HeaderLength));

            return ValueTask.FromResult(EncryptedValue.FromBytes(frame));
        }

        public ValueTask<FieldDecryption> DecryptAsync(
            EncryptedValue value,
            FieldContext context,
            CancellationToken cancellationToken = default
        )
        {
            Contexts.Add(context);

            if (Failure is not null)
            {
                throw Failure;
            }

            var plaintext = value
                .Frame.Span.Slice(EncryptedValue.HeaderLength, value.Length - EncryptedValue.Overhead)
                .ToArray();

            return ValueTask.FromResult(new FieldDecryption { Plaintext = plaintext, KeyIsCurrent = true });
        }
    }
}
