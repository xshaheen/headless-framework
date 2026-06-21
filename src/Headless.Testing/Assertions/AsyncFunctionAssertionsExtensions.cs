// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Exceptions;
using Headless.Primitives;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace AwesomeAssertions.Specialized;

/// <summary>
/// AwesomeAssertions extension methods for asserting Headless domain exceptions on async delegates.
/// </summary>
[PublicAPI]
public static class AsyncFunctionAssertionsExtensions
{
    /// <summary>
    /// Asserts that the async action throws a <c>ConflictException</c> whose
    /// <c>Errors</c> collection contains exactly <paramref name="errorDescriptor"/>.
    /// </summary>
    /// <param name="action">The async assertion subject.</param>
    /// <param name="errorDescriptor">The single expected error descriptor.</param>
    /// <param name="because">Reason text forwarded to the assertion failure message.</param>
    /// <param name="becauseArgs">Arguments for the <paramref name="because"/> format string.</param>
    /// <returns>Assertion object for further chaining.</returns>
    public static Task<ExceptionAssertions<ConflictException>> ThrowConflictExceptionAsync<TTask, TAssertions>(
        this AsyncFunctionAssertions<TTask, TAssertions> action,
        ErrorDescriptor errorDescriptor,
        string because = "",
        params object[] becauseArgs
    )
        where TTask : Task
        where TAssertions : AsyncFunctionAssertions<TTask, TAssertions>
    {
        return ThrowConflictExceptionAsync(action, [errorDescriptor], because, becauseArgs);
    }

    /// <summary>
    /// Asserts that the async action throws a <c>ConflictException</c>. When
    /// <paramref name="errors"/> is non-null, also asserts that the exception's <c>Errors</c>
    /// collection is equivalent to <paramref name="errors"/>.
    /// </summary>
    /// <param name="action">The async assertion subject.</param>
    /// <param name="errors">
    /// Expected error descriptors, or <see langword="null"/> to skip the errors check.
    /// </param>
    /// <param name="because">Reason text forwarded to the assertion failure message.</param>
    /// <param name="becauseArgs">Arguments for the <paramref name="because"/> format string.</param>
    /// <returns>Assertion object for further chaining.</returns>
    public static async Task<ExceptionAssertions<ConflictException>> ThrowConflictExceptionAsync<TTask, TAssertions>(
        this AsyncFunctionAssertions<TTask, TAssertions> action,
        IEnumerable<ErrorDescriptor>? errors = null,
        string because = "",
        params object[] becauseArgs
    )
        where TTask : Task
        where TAssertions : AsyncFunctionAssertions<TTask, TAssertions>
    {
        var assertions = await action.ThrowAsync<ConflictException>(because, becauseArgs);

        if (errors is not null)
        {
            assertions.Which.Errors.Should().BeEquivalentTo(errors);
        }

        return assertions;
    }

    /// <summary>
    /// Asserts that the async action throws an <c>EntityNotFoundException</c>. Optionally
    /// also checks <c>Key</c> and <c>Message</c> on the thrown exception.
    /// </summary>
    /// <param name="action">The async assertion subject.</param>
    /// <param name="key">
    /// Expected value of <c>EntityNotFoundException.Key</c>, or <see langword="null"/> to skip.
    /// </param>
    /// <param name="message">
    /// Expected exception message, or <see langword="null"/> to skip.
    /// </param>
    /// <param name="because">Reason text forwarded to the assertion failure message.</param>
    /// <param name="becauseArgs">Arguments for the <paramref name="because"/> format string.</param>
    /// <returns>Assertion object for further chaining.</returns>
    public static async Task<ExceptionAssertions<EntityNotFoundException>> ThrowEntityNotFoundExceptionAsync<
        TTask,
        TAssertions
    >(
        this AsyncFunctionAssertions<TTask, TAssertions> action,
        string? key = null,
        string? message = null,
        string because = "",
        params object[] becauseArgs
    )
        where TTask : Task
        where TAssertions : AsyncFunctionAssertions<TTask, TAssertions>
    {
        var assertions = await action.ThrowAsync<EntityNotFoundException>(because, becauseArgs);

        if (key is not null)
        {
            assertions.Which.Key.Should().Be(key);
        }

        if (message is not null)
        {
            assertions.Which.Message.Should().Be(message);
        }

        return assertions;
    }
}
