// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Exceptions;
using Framework.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace FluentAssertions.Specialized;

[PublicAPI]
public static class AsyncFunctionAssertionsExtensions
{
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
            assertions.Which.Key.Should().Be(message);
        }

        return assertions;
    }
}
