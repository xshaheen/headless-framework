// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Primitives;

namespace Tests.Exceptions;

public sealed class DerivedStatusExceptionTests
{
    private static readonly ErrorDescriptor _Error = new("app_code", "App description");
    private static readonly InvalidOperationException _Inner = new("inner");

    [Fact]
    public void should_keep_own_message_errors_and_inner_when_conflict_is_derived()
    {
        var exception = new AppConflict("own message", [_Error], _Inner);

        exception.Message.Should().Be("own message");
        exception.Errors.Should().ContainSingle().Which.Should().Be(_Error);
        exception.InnerException.Should().BeSameAs(_Inner);
    }

    [Fact]
    public void should_throw_when_derived_conflict_has_no_errors()
    {
        var act = () => new AppConflict("own message", [], innerException: null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_derived_conflict_has_null_message()
    {
        var act = () => new AppConflict(null!, [_Error], innerException: null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_keep_own_message_error_and_inner_when_forbidden_is_derived()
    {
        var exception = new AppForbidden("own message", _Error, _Inner);

        exception.Message.Should().Be("own message");
        exception.Error.Should().Be(_Error);
        exception.InnerException.Should().BeSameAs(_Inner);
        new Func<AppForbidden>(() => new AppForbidden("m", null!, null)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_keep_own_message_error_and_inner_when_unauthorized_is_derived()
    {
        var exception = new AppUnauthorized("own message", _Error, _Inner);

        exception.Message.Should().Be("own message");
        exception.Error.Should().Be(_Error);
        exception.InnerException.Should().BeSameAs(_Inner);
        new Func<AppUnauthorized>(() => new AppUnauthorized("m", null!, null)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_keep_own_message_wait_error_and_inner_when_too_many_requests_is_derived()
    {
        var exception = new AppTooManyRequests("own message", TimeSpan.FromSeconds(7), _Error, _Inner);

        exception.Message.Should().Be("own message");
        exception.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
        exception.Error.Should().Be(_Error);
        exception.InnerException.Should().BeSameAs(_Inner);
    }

    [Fact]
    public void should_keep_own_message_entity_key_and_inner_when_entity_not_found_is_derived()
    {
        var exception = new AppEntityNotFound("own message", "Account", "42", _Inner);

        exception.Message.Should().Be("own message");
        exception.Entity.Should().Be("Account");
        exception.Key.Should().Be("42");
        exception.InnerException.Should().BeSameAs(_Inner);
        new Func<AppEntityNotFound>(() => new AppEntityNotFound("m", null!, "42", null))
            .Should()
            .Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_keep_public_too_many_requests_message_and_values()
    {
        var exception = new TooManyRequestsException(TimeSpan.FromSeconds(3), _Error);

        exception.Message.Should().Be("Too many requests: app_code: App description");
        exception.RetryAfter.Should().Be(TimeSpan.FromSeconds(3));
        exception.Error.Should().Be(_Error);
        new TooManyRequestsException(TimeSpan.FromSeconds(3)).Message.Should().Be("Too many requests.");
    }

    private sealed class AppConflict(string message, IReadOnlyList<ErrorDescriptor> errors, Exception? innerException)
        : ConflictException(message, errors, innerException);

    private sealed class AppForbidden(string message, ErrorDescriptor error, Exception? innerException)
        : ForbiddenException(message, error, innerException);

    private sealed class AppUnauthorized(string message, ErrorDescriptor error, Exception? innerException)
        : UnauthorizedException(message, error, innerException);

    private sealed class AppTooManyRequests(
        string message,
        TimeSpan retryAfter,
        ErrorDescriptor? error,
        Exception? innerException
    ) : TooManyRequestsException(message, retryAfter, error, innerException);

    private sealed class AppEntityNotFound(string message, string entity, string key, Exception? innerException)
        : EntityNotFoundException(message, entity, key, innerException);
}
