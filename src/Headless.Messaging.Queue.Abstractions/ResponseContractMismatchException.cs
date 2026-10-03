// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Thrown when a reply carries a response contract name or version other than the one the caller expects for its
/// response type.
/// </summary>
/// <remarks>
/// The responder completed the work, so retrying the call repeats it. The mismatch means the two hosts disagree on the
/// response contract and need a coordinated deployment.
/// </remarks>
/// <param name="requestId">The request's identifier.</param>
/// <param name="expectedMessageName">The response contract name the caller expects.</param>
/// <param name="expectedContractVersion">The response contract version the caller expects.</param>
/// <param name="actualMessageName">The response contract name the reply carries.</param>
/// <param name="actualContractVersion">The response contract version the reply carries.</param>
[PublicAPI]
public sealed class ResponseContractMismatchException(
    string? requestId,
    string expectedMessageName,
    string expectedContractVersion,
    string? actualMessageName,
    string? actualContractVersion
)
    : RequestReplyException(
        $"The reply to request '{requestId}' carries response contract '{actualMessageName}' version "
            + $"'{actualContractVersion}', but the caller expects '{expectedMessageName}' version "
            + $"'{expectedContractVersion}'.",
        requestId
    )
{
    /// <summary>Gets the response contract name the caller expects.</summary>
    public string ExpectedMessageName { get; } = expectedMessageName;

    /// <summary>Gets the response contract version the caller expects.</summary>
    public string ExpectedContractVersion { get; } = expectedContractVersion;

    /// <summary>Gets the response contract name the reply carries, or <see langword="null"/> when it carries none.</summary>
    public string? ActualMessageName { get; } = actualMessageName;

    /// <summary>Gets the response contract version the reply carries, or <see langword="null"/> when it carries none.</summary>
    public string? ActualContractVersion { get; } = actualContractVersion;
}
