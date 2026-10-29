// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.Encryption;

internal sealed class FieldEncryptor(DataKeyRing ring, ICurrentTenant currentTenant) : IFieldEncryptor
{
    public async ValueTask<EncryptedValue> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        FieldContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        var scope = _ResolveScope(context);

        using var key = await ring.GetOrCreateActiveAsync(scope, DataKeyKind.Data, cancellationToken)
            .ConfigureAwait(false);

        var frame = FieldCipher.Seal(key.Key, key.KeyId, scope, context.Purpose, context.Binding.Span, plaintext.Span);

        return EncryptedValue.FromBytes(frame);
    }

    public async ValueTask<FieldDecryption> DecryptAsync(
        EncryptedValue value,
        FieldContext context,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsFalse(value.IsDefault, "The default EncryptedValue holds no frame to decrypt.", nameof(value));
        var scope = _ResolveScope(context);

        if (value.Algorithm != FieldCipher.AesGcmHkdfSha256)
        {
            throw new FieldDecryptionException(
                FieldDecryptionFailure.UnsupportedAlgorithm,
                $"The encrypted value uses algorithm {value.Algorithm}, which this release does not know."
            );
        }

        var keyId = value.KeyId;
        byte[]? plaintext;

        using (var key = await ring.TryGetAsync(keyId, cancellationToken).ConfigureAwait(false))
        {
            if (key is null)
            {
                throw new FieldDecryptionException(
                    FieldDecryptionFailure.KeyNotFound,
                    $"The data key {keyId} of the encrypted value does not exist; its scope may have been shredded."
                );
            }

            // The tag would refuse these too; checking first skips the derivation and states the cause.
            plaintext =
                key.Kind == DataKeyKind.Data && string.Equals(key.Scope, scope, StringComparison.Ordinal)
                    ? FieldCipher.Open(key.Key, value.Frame.Span, scope, context.Purpose, context.Binding.Span)
                    : null;
        }

        if (plaintext is null)
        {
            throw new FieldDecryptionException(
                FieldDecryptionFailure.AuthenticationFailed,
                "The encrypted value did not authenticate: its purpose, scope, or binding differs from the ones it was "
                    + "encrypted with, or it was changed."
            );
        }

        var activeId = await ring.GetActiveIdAsync(scope, DataKeyKind.Data, cancellationToken).ConfigureAwait(false);

        return new FieldDecryption { Plaintext = plaintext, KeyIsCurrent = activeId == keyId };
    }

    private string _ResolveScope(FieldContext context)
    {
        var scope =
            context.Scope
            ?? currentTenant.Id
            ?? throw new MissingTenantContextException(
                $"Encrypting or decrypting '{context.Purpose}' needs a tenant: no FieldContext.Scope was given and no "
                    + "tenant is current. Wrap the call in ICurrentTenant.Change(tenantId), set FieldContext.Scope, or "
                    + "use FieldContext.Host(purpose) for host-level data."
            );

        return DataKeyScope.Validate(scope, nameof(FieldContext.Scope));
    }
}
