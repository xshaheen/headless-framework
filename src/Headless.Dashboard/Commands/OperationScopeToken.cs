// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Headless.Dashboard;

/// <summary>
/// An Operation's frozen scope, as the confirm step signs it and every chunk call brings it back: the module, the
/// command, the filter, the high-water mark, the count, the actor, the reason, the arguments, and the expiry.
/// </summary>
/// <remarks>
/// The dashboard stores no Operation, so this token is the Operation's only state. It is protected with Data
/// Protection, which encrypts and authenticates it, so a chunk can run on any node that shares the key ring and a
/// client can neither read nor change the scope it confirmed.
/// </remarks>
internal sealed record OperationScopeToken
{
    public required string OperationId { get; init; }

    public required string Module { get; init; }

    public required string CommandId { get; init; }

    /// <summary>The filter as the module's list view sent it, as JSON text.</summary>
    public required string Filter { get; init; }

    public required string HighWaterMark { get; init; }

    public required long Count { get; init; }

    /// <summary>The operator who confirmed; a chunk from anyone else is refused.</summary>
    public required string Actor { get; init; }

    public string? Reason { get; init; }

    /// <summary>The validated arguments as a JSON object's text, or <see langword="null"/> when there are none.</summary>
    public string? Arguments { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Signs and reads <see cref="OperationScopeToken"/>s, and refuses one that is tampered with or expired.</summary>
/// <remarks>
/// The expiry is checked against the registered <see cref="TimeProvider"/> rather than a time-limited protector, which
/// reads the system clock, so the expiry follows the same clock as the rest of the app.
/// </remarks>
internal sealed class OperationScopeTokenProtector(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    private const string _Purpose = "Headless.Dashboard.OperationScope.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(_Purpose);

    /// <summary>Protects <paramref name="token"/> into URL-safe text.</summary>
    public string Protect(OperationScopeToken token)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(
            token,
            OperationScopeTokenJsonContext.Default.OperationScopeToken
        );

        return Base64Url.EncodeToString(_protector.Protect(json));
    }

    /// <summary>Reads a token a chunk call brought back.</summary>
    /// <param name="text">The token text.</param>
    /// <param name="token">The token when it is intact and unexpired.</param>
    /// <param name="error">
    /// Otherwise the error to answer with: the token is not one this app signed, or it expired.
    /// </param>
    /// <returns>Whether <paramref name="token"/> was read.</returns>
    public bool TryUnprotect(
        string text,
        [NotNullWhen(true)] out OperationScopeToken? token,
        [NotNullWhen(false)] out CommandFieldError? error
    )
    {
        token = null;
        error = null;
        OperationScopeToken? read;

        try
        {
            var json = _protector.Unprotect(Base64Url.DecodeFromChars(text));
            read = JsonSerializer.Deserialize(json, OperationScopeTokenJsonContext.Default.OperationScopeToken);
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            // Every way a token can be unreadable answers the same: a client learns nothing about why.
            read = null;
        }

        if (read is null)
        {
            error = new CommandFieldError(
                "token",
                "invalid",
                "The Operation token is not valid. Confirm the Operation again."
            );

            return false;
        }

        if (timeProvider.GetUtcNow() >= read.ExpiresAt)
        {
            error = new CommandFieldError(
                "token",
                "expired",
                "The Operation token expired. Confirm the Operation again."
            );

            return false;
        }

        token = read;

        return true;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OperationScopeToken))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class OperationScopeTokenJsonContext : JsonSerializerContext;
