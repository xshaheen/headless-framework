// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Testing.Tests;
using Headless.Messaging.Dashboard;

namespace Tests;

public sealed class HtmlHelperTests : TestBase
{
    [Fact]
    public void MethodEscaped_should_format_void_method()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.VoidMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("void");
        result.Should().Contain("VoidMethod");
    }

    [Fact]
    public void MethodEscaped_should_format_async_task_method()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.AsyncTaskMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("async");
        result.Should().Contain("Task");
        result.Should().Contain("AsyncTaskMethod");
    }

    [Fact]
    public void MethodEscaped_should_format_async_task_with_result_method()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.AsyncTaskWithResultMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("async");
        result.Should().Contain("Task");
        result.Should().Contain("AsyncTaskWithResultMethod");
    }

    [Fact]
    public void MethodEscaped_should_format_method_with_string_parameter()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.MethodWithStringParam))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("void");
        result.Should().Contain("MethodWithStringParam");
        result.Should().Contain("input");
    }

    [Fact]
    public void MethodEscaped_should_format_method_with_int_return_type()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.IntReturningMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("IntReturningMethod");
    }

    [Fact]
    public void MethodEscaped_should_format_method_with_complex_parameter()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.MethodWithComplexParam))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("MethodWithComplexParam");
        result.Should().Contain("ComplexType");
    }

    [Fact]
    public void MethodEscaped_should_include_keyword_span_class()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.VoidMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("<span class=\"keyword\">");
    }

    [Fact]
    public void MethodEscaped_should_include_type_span_class_for_complex_types()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.MethodWithComplexParam))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("<span class=\"type\">");
    }

    [Fact]
    public void MethodEscaped_should_handle_method_with_no_parameters()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.NoParamMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("();");
    }

    [Fact]
    public void MethodEscaped_should_handle_valuetask_method()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.ValueTaskMethod))!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("async");
        result.Should().Contain("ValueTaskMethod");
    }

    // Test class with various method signatures for testing
    public class TestClass
    {
        public void VoidMethod() { }

        public async Task AsyncTaskMethod() => await Task.CompletedTask;

        public async Task<string> AsyncTaskWithResultMethod() => await Task.FromResult("result");

        public void MethodWithStringParam(string input) { }

        public int IntReturningMethod() => 42;

        public void MethodWithComplexParam(ComplexType param) { }

        public string NoParamMethod() => "test";

        public async ValueTask ValueTaskMethod() => await ValueTask.CompletedTask;

        public async ValueTask<int> ValueTaskWithResultMethod() => await ValueTask.FromResult(42);
    }

    public class ComplexType
    {
        public required string Name { get; set; }
    }
}
