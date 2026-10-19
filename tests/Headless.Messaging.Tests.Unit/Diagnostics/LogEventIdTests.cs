// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Tests.Guarantees;

namespace Tests.Diagnostics;

/// <summary>
/// Operators filter and alert on log event IDs, so one ID must mean one event across every messaging assembly a host
/// can load together; two events sharing an ID make a filter match the wrong thing.
/// </summary>
public sealed class LogEventIdTests : TestBase
{
    [Fact]
    [Guarantee("MSG-OBS-01")]
    public void should_give_each_log_event_id_one_event_across_the_messaging_assemblies()
    {
        MessagingGuarantees.RequireSupport("MSG-OBS-01");

        // given: every messaging assembly shipped beside the tests, provider packages included when referenced
        var assemblies = _MessagingAssemblies();
        assemblies.Should().Contain(assembly => assembly.GetName().Name == "Headless.Messaging");

        // when: the declared events, keyed by ID; the same event declared in two holders is still one event
        var collisions = assemblies
            .SelectMany(_DeclaredEvents)
            .GroupBy(declared => declared.Id)
            .Where(group => group.Select(declared => declared.Name).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(group =>
                $"EventId {group.Key}: "
                + string.Join(", ", group.Select(declared => $"{declared.Name} ({declared.Declaration})").Distinct())
            )
            .Order(StringComparer.Ordinal)
            .ToList();

        // then
        string.Join(Environment.NewLine, collisions).Should().BeEmpty();
    }

    private static List<Assembly> _MessagingAssemblies()
    {
        return
        [
            .. Directory
                .EnumerateFiles(AppContext.BaseDirectory, "Headless.Messaging*.dll")
                .Where(path =>
                    !Path.GetFileName(path).Contains(".Tests.", StringComparison.Ordinal)
                    && !Path.GetFileName(path).Contains("Fixture", StringComparison.Ordinal)
                )
                .Select(Assembly.LoadFrom),
        ];
    }

    private static IEnumerable<DeclaredEvent> _DeclaredEvents(Assembly assembly)
    {
        const BindingFlags all =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        return assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(all | BindingFlags.DeclaredOnly))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(pair => pair.Attribute is { EventId: >= 0 })
            .Select(pair => new DeclaredEvent(
                pair.Attribute!.EventId,
                pair.Attribute.EventName ?? pair.Method.Name,
                $"{assembly.GetName().Name}: {pair.Method.DeclaringType?.Name}.{pair.Method.Name}"
            ));
    }

    private sealed record DeclaredEvent(int Id, string Name, string Declaration);
}
