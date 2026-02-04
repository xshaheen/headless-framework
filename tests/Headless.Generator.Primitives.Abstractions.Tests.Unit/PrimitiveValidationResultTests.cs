// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Generator.Primitives;

namespace Tests;

public sealed class PrimitiveValidationResultTests
{
    [Fact]
    public void should_create_success_result()
    {
        var result = PrimitiveValidationResult.Ok;

        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void should_create_failure_result()
    {
        var result = PrimitiveValidationResult.Error("invalid value");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("invalid value");
    }

    [Fact]
    public void should_convert_string_to_error_result_implicitly()
    {
        PrimitiveValidationResult result = "validation failed";

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("validation failed");
    }

    [Fact]
    public void should_report_equal_when_both_ok()
    {
        var result1 = PrimitiveValidationResult.Ok;
        var result2 = PrimitiveValidationResult.Ok;

        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
        (result1 != result2).Should().BeFalse();
        result1.GetHashCode().Should().Be(result2.GetHashCode());
    }

    [Fact]
    public void should_report_equal_when_same_error_message()
    {
        var result1 = PrimitiveValidationResult.Error("same error");
        var result2 = PrimitiveValidationResult.Error("same error");

        result1.Should().Be(result2);
        (result1 == result2).Should().BeTrue();
        (result1 != result2).Should().BeFalse();
        result1.GetHashCode().Should().Be(result2.GetHashCode());
    }

    [Fact]
    public void should_report_not_equal_when_different_error_messages()
    {
        var result1 = PrimitiveValidationResult.Error("error1");
        var result2 = PrimitiveValidationResult.Error("error2");

        result1.Should().NotBe(result2);
        (result1 == result2).Should().BeFalse();
        (result1 != result2).Should().BeTrue();
    }

    [Fact]
    public void should_report_not_equal_when_ok_vs_error()
    {
        var ok = PrimitiveValidationResult.Ok;
        var error = PrimitiveValidationResult.Error("error");

        ok.Should().NotBe(error);
        (ok == error).Should().BeFalse();
        (ok != error).Should().BeTrue();
    }

    [Fact]
    public void should_convert_from_string_using_factory_method()
    {
        var result = PrimitiveValidationResult.FromString("error message");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("error message");
    }

    [Fact]
    public void should_implement_object_equals()
    {
        var result1 = PrimitiveValidationResult.Error("msg");
        var result2 = PrimitiveValidationResult.Error("msg");
        object boxed = result2;

        result1.Equals(boxed).Should().BeTrue();
        result1.Equals((object?)null).Should().BeFalse();
        result1.Equals("not a result").Should().BeFalse();
    }
}
