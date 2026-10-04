// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Headless.Messaging;
using Headless.Messaging.RequestReply;
using Headless.Testing.Tests;

namespace Tests.RequestReply;

/// <summary>
/// A fault body comes off the wire, from whoever could publish to the caller's reply address, and its text ends up in a
/// commonly logged exception message, so the caller bounds and sanitizes it the way the responder does.
/// </summary>
public sealed class ReplyProtocolTests : TestBase
{
    [Fact]
    public void should_strip_control_characters_and_bound_every_field_of_a_fault_read_from_the_wire()
    {
        // given
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                code = "handler_failed\r\nforged: line",
                exceptionType = "System.Invalid\u2028Operation" + new string('T', 1000),
                detail = "db\nslow" + new string('d', 5000),
            }
        );

        // when
        var fault = ReplyProtocol.ReadFault(body);

        // then
        fault.Should().NotBeNull();
        fault!.Code.Should().Be("handler_failedforged: line");
        fault.ExceptionType.Should().StartWith("System.InvalidOperation").And.EndWith("...");
        fault.ExceptionType!.Length.Should().BeLessThanOrEqualTo(256);
        fault.Detail.Should().StartWith("dbslow").And.EndWith("...");
        fault.Detail!.Length.Should().BeLessThanOrEqualTo(1024);
        new[] { fault.Code, fault.ExceptionType, fault.Detail }
            .Should()
            .AllSatisfy(value => value.Should().NotContainAny("\r", "\n", "\u2028"));
    }

    [Fact]
    public void should_bound_an_oversized_fault_code()
    {
        // given
        var body = Encoding.UTF8.GetBytes($$"""{"code":"{{new string('c', 500)}}"}""");

        // when
        var fault = ReplyProtocol.ReadFault(body);

        // then
        fault!.Code.Length.Should().BeLessThanOrEqualTo(64);
    }

    [Fact]
    public void should_keep_a_well_formed_fault_unchanged()
    {
        // given
        var body = ReplyProtocol.WriteFault(
            new ReplyFault(RequestFaultCodes.HandlerFailed, "System.TimeoutException", "db slow")
        );

        // when
        var fault = ReplyProtocol.ReadFault(body);

        // then
        fault.Should().Be(new ReplyFault(RequestFaultCodes.HandlerFailed, "System.TimeoutException", "db slow"));
    }

    [Fact]
    public void should_not_read_a_fault_whose_code_is_only_control_characters()
    {
        // given
        var body = Encoding.UTF8.GetBytes("""{"code":"\r\n\t"}""");

        // when
        var fault = ReplyProtocol.ReadFault(body);

        // then
        fault.Should().BeNull();
    }
}
