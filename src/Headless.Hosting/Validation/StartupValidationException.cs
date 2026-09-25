// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting.Validation;

/// <summary>
/// Thrown at host startup when two or more <see cref="IHeadlessStartupValidator" /> instances fail. Each failure is one of
/// the <see cref="AggregateException.InnerExceptions" />, with its original type.
/// </summary>
/// <remarks>
/// A single failing validator surfaces its own exception unwrapped, so a typed exception such as
/// <c>MissingRequiredServiceException</c> reaches the host as is.
/// </remarks>
[PublicAPI]
public sealed class StartupValidationException(string message, IEnumerable<Exception> failures)
    : AggregateException(message, failures);
