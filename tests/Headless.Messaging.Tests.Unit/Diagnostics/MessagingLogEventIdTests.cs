// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.Diagnostics;

/// <summary>
/// Operators filter and alert on a log event by its id, so an id two events share makes each filter catch the other
/// event too. Every source-generated log event in the core messaging assembly keeps an id of its own.
/// </summary>
public sealed class MessagingLogEventIdTests : TestBase
{
    [Fact]
    public void should_give_every_log_event_in_the_messaging_assembly_its_own_event_id()
    {
        // given
        var events = typeof(MessagingOptions)
            .Assembly.GetTypes()
            .SelectMany(type =>
                type.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
            )
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToList();

        // when
        var shared = events
            .GroupBy(x => x.Attribute!.EventId)
            .Where(group => group.Skip(1).Any())
            .Select(group =>
                $"{group.Key}: {string.Join(", ", group.Select(x => $"{x.Method.DeclaringType!.Name}.{x.Method.Name}"))}"
            )
            .ToList();

        // then
        events.Should().NotBeEmpty();
        shared.Should().BeEmpty();
    }
}
