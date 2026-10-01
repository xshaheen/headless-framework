// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Dashboard;
using Headless.Testing.Tests;

namespace Tests;

public sealed class HtmlHelperTests : TestBase
{
    [Fact]
    public void should_render_consume_signature_with_typed_context_when_message_type_is_known()
    {
        // when
        var result = HtmlHelper.MethodEscaped("ConsumeAsync", typeof(ComplexType));

        // then
        result
            .Should()
            .Be(
                "<span class=\"keyword\">public</span> <span class=\"type\">ValueTask</span> ConsumeAsync("
                    + "<span class=\"type\">ConsumeContext</span><<span class=\"type\">ComplexType</span>> context);"
            );
    }

    [Fact]
    public void should_render_primitive_message_type_as_keyword_when_method_escaped()
    {
        // when
        var result = HtmlHelper.MethodEscaped("ConsumeAsync", typeof(string));

        // then
        result.Should().Contain("<span class=\"type\">ConsumeContext</span><<span class=\"keyword\">string</span>>");
    }

    [Fact]
    public void should_render_untyped_context_when_message_type_is_unknown()
    {
        // when
        var result = HtmlHelper.MethodEscaped("ConsumeAsync", messageType: null);

        // then
        result
            .Should()
            .Be(
                "<span class=\"keyword\">public</span> <span class=\"type\">ValueTask</span> ConsumeAsync("
                    + "<span class=\"type\">ConsumeContext</span> context);"
            );
    }

    [Fact]
    public void should_render_the_given_method_name_when_method_escaped()
    {
        // when
        var result = HtmlHelper.MethodEscaped("HandleRuntimeSubscription", typeof(ComplexType));

        // then
        result.Should().Contain(" HandleRuntimeSubscription(");
        result.Should().NotContain("ConsumeAsync");
    }

    public sealed class ComplexType
    {
        public required string Name { get; set; }
    }
}
