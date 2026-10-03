// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using FluentValidation;

namespace Headless.Messaging.Configuration;

/// <summary>Configures request/reply on the Queue lane for this host.</summary>
/// <remarks>
/// A host sends requests only after <see cref="MessagingSetupBuilder.AddRequestReply"/>; these settings apply to the
/// requests it sends and the replies its responders send.
/// </remarks>
[PublicAPI]
public sealed class RequestReplyOptions
{
    /// <summary>
    /// Gets the longest <see cref="DefaultTimeout"/> a host may configure: the same bound a call's
    /// <see cref="RequestOptions.Timeout"/> has, so no call can wait longer than the host default may.
    /// </summary>
    public static readonly TimeSpan MaxDefaultTimeout = RequestOptions.MaxTimeout;

    /// <summary>
    /// Gets or sets how long a request waits for its reply when the call sets no <see cref="RequestOptions.Timeout"/>.
    /// Defaults to 30 seconds; must be greater than zero and at most 10 minutes.
    /// </summary>
    /// <remarks>
    /// The timeout also sets the request's deadline, after which the responder neither starts the work nor replies. A
    /// long default holds the caller, and the reply listener's pending call, for that long when a responder is down.
    /// </remarks>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether this host's responders put the exception type and message in a fault reply. Defaults to
    /// <see langword="false"/>, so a caller learns only the fault code.
    /// </summary>
    /// <remarks>
    /// Exception messages can carry internal details such as connection strings, identifiers, or stack-derived text, so
    /// they cross the process boundary only when the responder host opts in.
    /// </remarks>
    public bool IncludeExceptionDetailsInFaults { get; set; }

    internal void CopyTo(RequestReplyOptions target)
    {
        target.DefaultTimeout = DefaultTimeout;
        target.IncludeExceptionDetailsInFaults = IncludeExceptionDetailsInFaults;
    }
}

internal sealed class RequestReplyOptionsValidator : AbstractValidator<RequestReplyOptions>
{
    public RequestReplyOptionsValidator()
    {
        RuleFor(x => x.DefaultTimeout)
            .GreaterThan(TimeSpan.Zero)
            .WithMessage("RequestReply.DefaultTimeout must be greater than zero.")
            .LessThanOrEqualTo(RequestReplyOptions.MaxDefaultTimeout)
            .WithMessage(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"RequestReply.DefaultTimeout must not exceed {RequestReplyOptions.MaxDefaultTimeout.TotalMinutes} minutes."
                )
            );
    }
}
