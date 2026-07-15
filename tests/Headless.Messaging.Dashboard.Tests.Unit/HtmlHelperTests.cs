using System.Reflection;
// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class HtmlHelperTests : TestBase
{
    [Fact]
    public void should_format_void_method_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.VoidMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("void");
        result.Should().Contain("VoidMethod");
    }

    [Fact]
    public void should_format_async_task_method_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.AsyncTaskMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("async");
        result.Should().Contain("Task");
        result.Should().Contain("AsyncTaskMethod");
    }

    [Fact]
    public void should_format_async_task_with_result_method_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.AsyncTaskWithResultMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("async");
        result.Should().Contain("Task");
        result.Should().Contain("AsyncTaskWithResultMethod");
    }

    [Fact]
    public void should_format_method_with_string_parameter_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.MethodWithStringParam), [typeof(string)])!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("void");
        result.Should().Contain("MethodWithStringParam");
        result.Should().Contain("input");
    }

    [Fact]
    public void should_format_method_with_int_return_type_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.IntReturningMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("IntReturningMethod");
    }

    [Fact]
    public void should_format_method_with_complex_parameter_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.MethodWithComplexParam), [typeof(ComplexType)])!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("public");
        result.Should().Contain("MethodWithComplexParam");
        result.Should().Contain("ComplexType");
    }

    [Fact]
    public void should_include_keyword_span_class_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.VoidMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("<span class=\"keyword\">");
    }

    [Fact]
    public void should_include_type_span_class_for_complex_types_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(nameof(TestClass.MethodWithComplexParam), [typeof(ComplexType)])!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("<span class=\"type\">");
    }

    [Fact]
    public void should_handle_method_with_no_parameters_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.NoParamMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

        // when
        var result = HtmlHelper.MethodEscaped(method);

        // then
        result.Should().Contain("();");
    }

    [Fact]
    public void should_handle_valuetask_method_when_method_escaped()
    {
        // given
        var method = typeof(TestClass).GetMethod(
            nameof(TestClass.ValueTaskMethod),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null
        )!;

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

#pragma warning disable IDE0060 // Remove unused parameter
        public void MethodWithStringParam(string input) { }
#pragma warning restore IDE0060

        public int IntReturningMethod() => 42;

#pragma warning disable IDE0060 // Remove unused parameter
        public void MethodWithComplexParam(ComplexType param) { }
#pragma warning restore IDE0060

        public string NoParamMethod() => "test";

        public async ValueTask ValueTaskMethod() => await ValueTask.CompletedTask;

        public async ValueTask<int> ValueTaskWithResultMethod() => await ValueTask.FromResult(42);
    }

    public class ComplexType
    {
        public required string Name { get; set; }
    }
}
