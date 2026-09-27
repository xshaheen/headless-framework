// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2.Responses;

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>
/// Maps a failed FCM send onto an <see cref="FcmSendResult"/>: its status, wire error code, failure kind, and retry
/// guidance.
/// </summary>
/// <remarks>
/// The FCM code decides first, then the platform code the SDK derives from the HTTP status or the google.rpc
/// status, then the exception type. A <see cref="FirebaseException"/> without an HTTP response never reached FCM:
/// the SDK wraps a network failure that way after its own retries.
/// </remarks>
internal static class FcmFailureClassifier
{
    public static FcmSendResult Classify(
        string target,
        Exception? failure,
        bool senderIdMismatchIsUnregistered,
        TimeProvider timeProvider
    )
    {
        var (kind, errorCode, unregistered) = _Kind(failure, senderIdMismatchIsUnregistered);

        return new FcmSendResult
        {
            Response = unregistered
                ? PushNotificationResponse.Unregistered(target)
                : PushNotificationResponse.Failed(target, Describe(failure)),
            ErrorCode = errorCode,
            FailureKind = kind,
            RetryAfter = _RetryAfter(failure, kind, timeProvider),
        };
    }

    /// <summary>
    /// A failure description that starts with the most specific code available: the FCM code, then the platform
    /// code the SDK always sets, then the exception type, so it never starts with an empty code.
    /// </summary>
    public static string Describe(Exception? exception)
    {
        return exception switch
        {
            null => "Unknown error",
            FirebaseMessagingException { MessagingErrorCode: { } code } => $"{code}: {exception.Message}",
            FirebaseException firebase => $"{firebase.ErrorCode}: {exception.Message}",
            _ => $"{exception.GetType().Name}: {exception.Message}",
        };
    }

    private static (FcmFailureKind Kind, string? ErrorCode, bool Unregistered) _Kind(
        Exception? failure,
        bool senderIdMismatchIsUnregistered
    )
    {
        return failure switch
        {
            FirebaseMessagingException { MessagingErrorCode: { } code } messaging => code switch
            {
                MessagingErrorCode.Unregistered => (FcmFailureKind.TokenInvalid, "UNREGISTERED", true),
                // A host signed in to the wrong project gets this for every token, so it is a dead token only when
                // the caller vouches for the credentials.
                MessagingErrorCode.SenderIdMismatch => senderIdMismatchIsUnregistered
                    ? (FcmFailureKind.TokenInvalid, "SENDER_ID_MISMATCH", true)
                    : (FcmFailureKind.Configuration, "SENDER_ID_MISMATCH", false),
                // Google: delete a token on INVALID_ARGUMENT only when the error is about the token, which the code
                // alone does not say, so it is never read as a dead token.
                MessagingErrorCode.InvalidArgument => (FcmFailureKind.Payload, "INVALID_ARGUMENT", false),
                MessagingErrorCode.QuotaExceeded => (FcmFailureKind.Throttled, "QUOTA_EXCEEDED", false),
                MessagingErrorCode.Internal => (FcmFailureKind.ServerError, "INTERNAL", false),
                MessagingErrorCode.Unavailable => (FcmFailureKind.ServerError, "UNAVAILABLE", false),
                MessagingErrorCode.ThirdPartyAuthError => (
                    FcmFailureKind.Authentication,
                    "THIRD_PARTY_AUTH_ERROR",
                    false
                ),
                _ => (_PlatformKind(messaging), _PlatformCode(messaging), false),
            },
            // A multicast send wraps any exception it catches in an Unknown error whose message is the original
            // exception's ToString(), so a rejected token exchange there is recognisable only by its type name.
            // Read as a network failure, it would tell the caller to retry a revoked key forever.
            FirebaseMessagingException { MessagingErrorCode: null, HttpResponse: null } wrapped
                when wrapped.Message.StartsWith(typeof(TokenResponseException).FullName!, StringComparison.Ordinal) => (
                FcmFailureKind.Authentication,
                null,
                false
            ),
            FirebaseException { HttpResponse: null } => (FcmFailureKind.Transport, null, false),
            FirebaseException firebase => (_PlatformKind(firebase), _PlatformCode(firebase), false),
            FcmCredentialException or TokenResponseException => (FcmFailureKind.Authentication, null, false),
            // The SDK validates the message inside the send; input this layer did not check fails the same way FCM's
            // own INVALID_ARGUMENT would.
            ArgumentException => (FcmFailureKind.Payload, null, false),
            // A timeout, a network failure the SDK did not wrap, or anything else that produced no answer.
            _ => (FcmFailureKind.Transport, null, false),
        };
    }

    private static FcmFailureKind _PlatformKind(FirebaseException exception)
    {
        return exception.ErrorCode switch
        {
            ErrorCode.InvalidArgument
            or ErrorCode.FailedPrecondition
            or ErrorCode.OutOfRange
            or ErrorCode.AlreadyExists => FcmFailureKind.Payload,
            ErrorCode.Unauthenticated => FcmFailureKind.Authentication,
            ErrorCode.PermissionDenied or ErrorCode.NotFound => FcmFailureKind.Configuration,
            ErrorCode.ResourceExhausted => FcmFailureKind.Throttled,
            // The SDK reads only a few google.rpc statuses and otherwise maps the HTTP status, so an ABORTED 409
            // arrives as CONFLICT; a send creates nothing that could conflict, so it is an aborted attempt to retry.
            ErrorCode.Internal
            or ErrorCode.Unavailable
            or ErrorCode.DataLoss
            or ErrorCode.DeadlineExceeded
            or ErrorCode.Aborted
            or ErrorCode.Conflict => FcmFailureKind.ServerError,
            // UNKNOWN or CANCELLED with an answer: fall back to the HTTP status class.
            _ => exception.HttpResponse is { StatusCode: >= HttpStatusCode.InternalServerError }
                ? FcmFailureKind.ServerError
                : FcmFailureKind.Payload,
        };
    }

    private static string _PlatformCode(FirebaseException exception)
    {
        return exception.ErrorCode switch
        {
            ErrorCode.InvalidArgument => "INVALID_ARGUMENT",
            ErrorCode.FailedPrecondition => "FAILED_PRECONDITION",
            ErrorCode.OutOfRange => "OUT_OF_RANGE",
            ErrorCode.Unauthenticated => "UNAUTHENTICATED",
            ErrorCode.PermissionDenied => "PERMISSION_DENIED",
            ErrorCode.NotFound => "NOT_FOUND",
            ErrorCode.Conflict => "CONFLICT",
            ErrorCode.Aborted => "ABORTED",
            ErrorCode.AlreadyExists => "ALREADY_EXISTS",
            ErrorCode.ResourceExhausted => "RESOURCE_EXHAUSTED",
            ErrorCode.Cancelled => "CANCELLED",
            ErrorCode.DataLoss => "DATA_LOSS",
            ErrorCode.Internal => "INTERNAL",
            ErrorCode.Unavailable => "UNAVAILABLE",
            ErrorCode.DeadlineExceeded => "DEADLINE_EXCEEDED",
            _ => "UNKNOWN",
        };
    }

    private static TimeSpan? _RetryAfter(Exception? failure, FcmFailureKind kind, TimeProvider timeProvider)
    {
        var retryAfter = failure is FirebaseException firebase
            ? RetryHelper.GetRetryAfter(firebase, timeProvider)
            : null;

        return kind switch
        {
            FcmFailureKind.Throttled => retryAfter ?? RetryHelper.MinQuotaDelay,
            FcmFailureKind.ServerError => retryAfter ?? RetryHelper.MinServerErrorDelay,
            _ => null,
        };
    }
}
