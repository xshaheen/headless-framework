// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;

namespace Headless.Tus;

/// <summary>
/// What an <see cref="ITusUploadCompletedHandler"/> decided about a finished upload: keep it, delete it, or
/// refuse it.
/// </summary>
[PublicAPI]
public sealed class TusUploadHandoff
{
    private TusUploadHandoff(string? message)
    {
        Message = message;
    }

    /// <summary>
    /// The application took the upload over and it stays in the store. The upload is the application's to delete
    /// from then on: a client can no longer delete it.
    /// </summary>
    public static TusUploadHandoff Keep { get; } = new(message: null);

    /// <summary>
    /// The application took what it needs, for example a copy, and the upload is removed from the store once the
    /// response is sent.
    /// </summary>
    public static TusUploadHandoff Delete { get; } = new(message: null);

    /// <summary>Whether the application refused the upload.</summary>
    [MemberNotNullWhen(true, nameof(Message))]
    public bool IsRejected => Message is not null;

    /// <summary>
    /// The reason the upload was refused, sent to the client as the plain-text body of the response, or
    /// <see langword="null"/> when it was not refused.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Refuses the upload for good: it is removed from the store at once, the request is answered with 400 and
    /// <paramref name="message"/>, and the handler never runs for it again.
    /// </summary>
    /// <param name="message">The reason, sent to the client as the plain-text body of the response.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// Refuse an upload the first time the handler sees it. A refusal reached when the handler runs again on a
    /// status request fails that request, and a tus client then uploads the whole file again before it reports
    /// the refusal.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="message"/> is empty or white space.</exception>
    public static TusUploadHandoff Reject(string message)
    {
        Argument.IsNotNullOrWhiteSpace(message);

        return new TusUploadHandoff(message);
    }
}
