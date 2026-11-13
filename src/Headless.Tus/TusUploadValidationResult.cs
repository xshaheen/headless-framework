// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Microsoft.AspNetCore.Http;

namespace Headless.Tus;

/// <summary>What an <see cref="ITusUploadValidator"/> decided about an upload: let it start, or refuse it.</summary>
[PublicAPI]
public sealed class TusUploadValidationResult
{
    private TusUploadValidationResult(int? statusCode, string? message)
    {
        StatusCode = statusCode;
        Message = message;
    }

    /// <summary>The result that lets the upload start.</summary>
    public static TusUploadValidationResult Success { get; } = new(statusCode: null, message: null);

    /// <summary>Whether the upload may start.</summary>
    [MemberNotNullWhen(false, nameof(StatusCode), nameof(Message))]
    public bool IsSuccess => StatusCode is null;

    /// <summary>The HTTP status the creating request is answered with, or <see langword="null"/> on success.</summary>
    public int? StatusCode { get; }

    /// <summary>
    /// The reason, sent to the client as the plain-text body of the response, or <see langword="null"/> on success.
    /// </summary>
    public string? Message { get; }

    /// <summary>Refuses the upload with 400 and <paramref name="message"/>.</summary>
    /// <param name="message">The reason, sent to the client as the plain-text body of the response.</param>
    /// <returns>The failure.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is empty or white space.</exception>
    public static TusUploadValidationResult Failure(string message)
    {
        return Failure(StatusCodes.Status400BadRequest, message);
    }

    /// <summary>Refuses the upload with <paramref name="statusCode"/> and <paramref name="message"/>.</summary>
    /// <param name="statusCode">
    /// The HTTP status of the response, from 400 to 599, for example 403 for a caller over their quota.
    /// </param>
    /// <param name="message">The reason, sent to the client as the plain-text body of the response.</param>
    /// <returns>The failure.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="statusCode"/> is not an error status.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is empty or white space.</exception>
    public static TusUploadValidationResult Failure(int statusCode, string message)
    {
        Argument.IsInclusiveBetween(statusCode, 400, 599);
        Argument.IsNotNullOrWhiteSpace(message);

        return new TusUploadValidationResult(statusCode, message);
    }
}
