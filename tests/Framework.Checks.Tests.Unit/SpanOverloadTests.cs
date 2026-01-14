// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public class SpanOverloadTests
{
    // IsNotEmpty Span<T> tests

    [Fact]
    public void is_not_empty_span_with_items_does_not_throw()
    {
        // given
        Span<int> span = stackalloc int[] { 1, 2, 3 };

        // when & then
        TestSpanNotEmpty(span);

        static void TestSpanNotEmpty(Span<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_empty_span_throws()
    {
        // given & when & then
        Assert.Throws<ArgumentException>(() => TestSpanThrows([]));

        static void TestSpanThrows(Span<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_span_with_custom_message_throws()
    {
        // given & when
        const string customMessage = "Span cannot be empty";
        var ex = Assert.Throws<ArgumentException>(() => TestSpanThrowsCustom([], customMessage));

        // then
        ex.Message.Should().Contain(customMessage);

        static void TestSpanThrowsCustom(Span<int> arg, string msg)
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
        TestReadOnlySpanNotEmpty(span);

        static void TestReadOnlySpanNotEmpty(ReadOnlySpan<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_empty_readonly_span_throws()
    {
        // given & when & then
        Assert.Throws<ArgumentException>(() => TestReadOnlySpanThrows([]));

        static void TestReadOnlySpanThrows(ReadOnlySpan<int> arg)
        {
            Argument.IsNotEmpty(arg);
        }
    }

    [Fact]
    public void is_not_empty_readonly_span_with_custom_message_throws()
    {
        // given & when
        const string customMessage = "ReadOnlySpan cannot be empty";
        var ex = Assert.Throws<ArgumentException>(() => TestReadOnlySpanThrowsCustom([], customMessage));

        // then
        ex.Message.Should().Contain(customMessage);

        static void TestReadOnlySpanThrowsCustom(ReadOnlySpan<int> arg, string msg)
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
        TestSpanStringNotEmpty(span);

        static void TestSpanStringNotEmpty(Span<string> arg)
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
        TestReadOnlySpanStringNotEmpty(span);

        static void TestReadOnlySpanStringNotEmpty(ReadOnlySpan<string> arg)
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
        TestIsOneOfReadOnlySpan(value, validValues);

        static void TestIsOneOfReadOnlySpan(int arg, ReadOnlySpan<int> values)
        {
            Argument.IsOneOf(arg, values);
        }
    }

    [Fact]
    public void is_one_of_with_readonly_span_values_and_invalid_argument_throws()
    {
        // given & when & then
        Assert.Throws<ArgumentException>(() => TestIsOneOfThrows(5, stackalloc int[] { 1, 2, 3 }));

        static void TestIsOneOfThrows(int arg, ReadOnlySpan<int> values)
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
        TestIsOneOfReadOnlySpanString(value, validValues);

        static void TestIsOneOfReadOnlySpanString(string arg, ReadOnlySpan<string> values)
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

        // when & then
        Assert.Throws<ArgumentException>(() => TestIsOneOfStringsThrows(value, items.AsSpan()));

        static void TestIsOneOfStringsThrows(string arg, ReadOnlySpan<string> values)
        {
            Argument.IsOneOf(arg, values);
        }
    }

    [Fact]
    public void is_one_of_with_readonly_span_and_custom_message_throws()
    {
        // given & when
        const string customMessage = "Value must be 1, 2, or 3";
        var ex = Assert.Throws<ArgumentException>(
            () => TestIsOneOfCustomMessage(5, stackalloc int[] { 1, 2, 3 }, customMessage)
        );

        // then
        ex.Message.Should().Contain(customMessage);

        static void TestIsOneOfCustomMessage(int arg, ReadOnlySpan<int> values, string msg)
        {
            Argument.IsOneOf(arg, values, msg);
        }
    }
}
