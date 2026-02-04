using Headless.Generator.Primitives;

namespace Tests;

public sealed class InvalidPrimitiveValueExceptionTests
{
    private sealed class TestPrimitive : IPrimitive
    {
        public Type GetUnderlyingPrimitiveType() => typeof(int);
    }

    [Fact]
    public void should_create_with_message_format_including_type_name_and_provided_message()
    {
        // given
        var primitive = new TestPrimitive();
        const string message = "Value must be positive";

        // when
        var exception = new InvalidPrimitiveValueException(message, primitive);

        // then
        exception.Message.Should().Be($"Cannot create instance of '{typeof(TestPrimitive).FullName}'. {message}");
    }

    [Fact]
    public void should_inherit_from_exception()
    {
        // given
        var primitive = new TestPrimitive();

        // when
        var exception = new InvalidPrimitiveValueException("test", primitive);

        // then
        exception.Should().BeAssignableTo<Exception>();
    }
}
