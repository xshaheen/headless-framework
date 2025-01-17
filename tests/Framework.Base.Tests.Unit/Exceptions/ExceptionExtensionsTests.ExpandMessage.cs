namespace Tests.Exceptions;

public sealed partial class ExceptionExtensionsTests
{
    [Fact]
    public void ExpandMessage_should_return_expanded_message()
    {
        // given
        var innerException = _ThrowAndGet(new InvalidOperationException("Inner exception"));
        var outerException = _ThrowAndGet(new InvalidOperationException("Outer exception", innerException));

        // when
        var result = outerException.ExpandMessage();

        // then
        var lines = result.Split(Environment.NewLine);
        lines.Should().HaveCount(4);
        lines[0].Should().Be("InvalidOperationException: Outer exception");
        lines[1].Should().StartWith("   at Tests.Exceptions.ExceptionExtensionsTests._ThrowAndGet[T](T e) in");
        lines[2].Should().Be("InvalidOperationException: Inner exception");
        lines[3].Should().StartWith("   at Tests.Exceptions.ExceptionExtensionsTests._ThrowAndGet[T](T e) in");
    }

    [Fact]
    public void ExpandMessage_should_expand_aggregate_exception_inners()
    {
        // given
        var innerException = _ThrowAndGet(new InvalidOperationException("Exception1 inner"));
        var exception1 = _ThrowAndGet(new InvalidOperationException("Exception1", innerException));
        var exception2 = _ThrowAndGet(new InvalidOperationException("Exception2"));
        var agg = new AggregateException(exception1, exception2);
        var aggregateException = _ThrowAndGet(agg);

        // when
        var result = aggregateException.ExpandMessage();

        // then
        var lines = result.Split(Environment.NewLine);
        lines.Should().HaveCount(6);
        lines[0].Should().Be("### AggregateException:");
        lines[1].Should().Be("InvalidOperationException: Exception1 inner");
        lines[2].Should().StartWith("   at Tests.Exceptions.ExceptionExtensionsTests._ThrowAndGet[T](T e) in");
        lines[3].Should().Be("InvalidOperationException: Exception2");
        lines[4].Should().StartWith("   at Tests.Exceptions.ExceptionExtensionsTests._ThrowAndGet[T](T e) in");
        lines[5].Should().Be("###");
    }

    private static T _ThrowAndGet<T>(T e)
        where T : Exception
    {
        try
        {
            throw e;
        }
        catch (T exception)
        {
            return exception;
        }
    }
}
