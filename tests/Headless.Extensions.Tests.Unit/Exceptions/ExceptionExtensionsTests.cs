namespace Tests.Exceptions;

public sealed partial class ExceptionExtensionsTests
{
    [Fact]
    public void ReThrow_should_preserve_stack_trace()
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
    public void GetInnermostException_should_return_innermost_exception()
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
}
