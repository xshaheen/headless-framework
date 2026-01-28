// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class SpanOverloadTests
{
    // IsNotEmpty Span<T> tests

    [Fact]
    public void is_not_empty_span_with_items_does_not_throw()
    {
        // given
        Span<int> span = stackalloc int[] { 1, 2, 3 };

        // when & then
        testSpanNotEmpty(span);

        return;

        static void testSpanNotEmpty(Span<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_empty_span_throws()
    {
        // given & when
        var testCode = () => testSpanThrows([]);

        // then
        testCode.Should().ThrowExactly<ArgumentException>();

        return;

        static void testSpanThrows(Span<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_span_with_custom_message_throws()
    {
        // given
        const string customMessage = "Span cannot be empty";
        var testCode = () => testSpanThrowsCustom([], customMessage);

        // when
        var ex = testCode.Should().ThrowExactly<ArgumentException>().And;

        // then
        ex.Message.Should().Contain(customMessage);

        return;

        static void testSpanThrowsCustom(Span<int> arg, string msg)
        {
            Argument.IsNotEmpty(arg, msg);
        }
    }

    // IsNotEmpty ReadOnlySpan<T> tests

    [Fact]
    public void is_not_empty_readonly_span_with_items_does_not_throw()
    {
        // given
        ReadOnlySpan<int> span = stackalloc int[] { 1, 2, 3 };

        // when & then
        testReadOnlySpanNotEmpty(span);

        return;

        static void testReadOnlySpanNotEmpty(ReadOnlySpan<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_empty_readonly_span_throws()
    {
        // given & when
        var testCode = () => testReadOnlySpanThrows([]);

        // then
        testCode.Should().ThrowExactly<ArgumentException>();

        return;

        static void testReadOnlySpanThrows(ReadOnlySpan<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_readonly_span_with_custom_message_throws()
    {
        // given
        const string customMessage = "ReadOnlySpan cannot be empty";
        var testCode = () => testReadOnlySpanThrowsCustom([], customMessage);

        // when
        var ex = testCode.Should().ThrowExactly<ArgumentException>().And;

        // then
        ex.Message.Should().Contain(customMessage);

        return;

        static void testReadOnlySpanThrowsCustom(ReadOnlySpan<int> arg, string msg)
        {
            Argument.IsNotEmpty(arg, msg);
        }
    }

    [Fact]
    public void is_not_empty_span_of_strings_with_items_does_not_throw()
    {
        // given
        string[] items = ["a", "b", "c"];
        Span<string> span = items;

        // when & then
        testSpanStringNotEmpty(span);

        return;

        static void testSpanStringNotEmpty(Span<string> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_readonly_span_of_strings_with_items_does_not_throw()
    {
        // given
        string[] items = ["a", "b", "c"];
        ReadOnlySpan<string> span = items;

        // when & then
        testReadOnlySpanStringNotEmpty(span);

        return;

        static void testReadOnlySpanStringNotEmpty(ReadOnlySpan<string> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    // IsOneOf ReadOnlySpan<T> parameter tests

    [Fact]
    public void is_one_of_with_readonly_span_values_and_valid_argument_does_not_throw()
    {
        // given
        const int value = 2;
        ReadOnlySpan<int> validValues = stackalloc int[] { 1, 2, 3 };

        // when & then
        testIsOneOfReadOnlySpan(value, validValues);

        return;

        static void testIsOneOfReadOnlySpan(int arg, ReadOnlySpan<int> values)
        {
            Argument.IsOneOf(arg, values);
        }
    }

    [Fact]
    public void is_one_of_with_readonly_span_values_and_invalid_argument_throws()
    {
        // given & when & then
        var testCode = () => testIsOneOfThrows(5, stackalloc int[] { 1, 2, 3 });

        testCode.Should().ThrowExactly<ArgumentException>();

        return;

        static void testIsOneOfThrows(int arg, ReadOnlySpan<int> values)
        {
            Argument.IsOneOf(arg, values);
        }
    }

    [Fact]
    public void is_one_of_with_readonly_span_strings_and_valid_argument_does_not_throw()
    {
        // given
        const string value = "b";
        string[] items = ["a", "b", "c"];
        ReadOnlySpan<string> validValues = items;

        // when & then
        testIsOneOfReadOnlySpanString(value, validValues);

        return;

        static void testIsOneOfReadOnlySpanString(string arg, ReadOnlySpan<string> values)
        {
            Argument.IsOneOf(arg, values);
        }
    }

    [Fact]
    public void is_one_of_with_readonly_span_strings_and_invalid_argument_throws()
    {
        // given
        const string value = "d";
        string[] items = ["a", "b", "c"];
        var testCode = () => testIsOneOfStringsThrows(value, items.AsSpan());

        // when & then
        testCode.Should().ThrowExactly<ArgumentException>();

        return;

        static void testIsOneOfStringsThrows(string arg, ReadOnlySpan<string> values)
        {
            Argument.IsOneOf(arg, values);
        }
    }

    [Fact]
    public void is_one_of_with_readonly_span_and_custom_message_throws()
    {
        // given & when
        const string customMessage = "Value must be 1, 2, or 3";
        var testCode = () => testIsOneOfCustomMessage(5, stackalloc int[] { 1, 2, 3 }, customMessage);

        var ex = testCode.Should().ThrowExactly<ArgumentException>().And;

        // then
        ex.Message.Should().Contain(customMessage);

        return;

        static void testIsOneOfCustomMessage(int arg, ReadOnlySpan<int> values, string msg)
        {
            Argument.IsOneOf(arg, values, msg);
        }
    }
}
