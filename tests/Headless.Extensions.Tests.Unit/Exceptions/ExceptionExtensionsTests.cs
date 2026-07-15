namespace Tests.Exceptions;

public sealed partial class ExceptionExtensionsTests
{
    [Fact]
    public void should_preserve_stack_trace_when_re_throw()
    {
        // given
        var exception = new InvalidOperationException("Test exception");

        // when
        Action action = () => exception.ReThrow();

        // then
        var assertion = action.Should().Throw<InvalidOperationException>();
        assertion.Which.StackTrace.Should().Be(exception.StackTrace);
        assertion.Which.Message.Should().Be("Test exception");
    }

    [Fact]
    public void should_return_innermost_exception_when_get_innermost_exception()
    {
        // given
        var innerMostException = new InvalidOperationException("Innermost exception");
        var innerException = new InvalidOperationException("Inner exception", innerMostException);
        var outerException = new InvalidOperationException("Outer exception", innerException);

        // when
        var result = outerException.GetInnermostException();

        // then
        result.Should().Be(innerMostException);
    }

    [Fact]
    public void should_return_null_for_null_when_get_innermost_exception()
    {
        // given
        Exception? exception = null;

        // when / then
        exception.GetInnermostException().Should().BeNull();
    }

    [Fact]
    public void should_terminate_on_cyclic_chain_when_get_innermost_exception()
    {
        // given a cyclic InnerException chain (a -> b -> a) that would loop forever without a cycle guard
        var a = new Exception("a");
        var b = new Exception("b");
        _SetInnerException(a, b);
        _SetInnerException(b, a);

        // when
        var result = a.GetInnermostException();

        // then it terminates and returns one of the chain members rather than hanging
        new[] { a, b }
            .Should()
            .Contain(result!);
    }

    [Fact]
    public void should_terminate_on_self_referential_chain_when_get_innermost_exception()
    {
        // given an exception whose InnerException points back to itself
        var self = new Exception("self");
        _SetInnerException(self, self);

        // when / then
        self.GetInnermostException().Should().Be(self);
    }

    // Exception.InnerException is not virtual and is set-once via the ctor, so forge a cycle by writing the
    // private backing field directly — the only way to construct a cyclic InnerException chain for the guard test.
    private static void _SetInnerException(Exception target, Exception inner)
    {
        typeof(Exception)
            .GetField(
                "_innerException",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .SetValue(target, inner);
    }
}
