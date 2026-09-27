// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Tests.Logging;

/// <summary>
/// Finds <c>[LoggerMessage]</c> methods whose event ID is missing or shared across a set of assemblies. Event IDs are
/// how operators filter and alert on a message, so two messages under one ID are indistinguishable.
/// </summary>
/// <remarks>
/// It depends only on reflection and <c>Microsoft.Extensions.Logging</c>, so another package family audits itself by
/// passing its own assemblies.
/// </remarks>
internal static class LoggerMessageEventIdAudit
{
    private const BindingFlags _AllDeclaredMethods =
        BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Static
        | BindingFlags.Instance
        | BindingFlags.DeclaredOnly;

    /// <summary>
    /// Returns one line per problem: a message without an explicit event ID, or an event ID used by more than one
    /// message. An empty result means every message has its own ID.
    /// </summary>
    public static IReadOnlyList<string> FindViolations(IEnumerable<Assembly> assemblies)
    {
        var messages = assemblies
            .Distinct()
            .SelectMany(_LoadableTypes)
            .SelectMany(type => type.GetMethods(_AllDeclaredMethods))
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(message => message.Attribute is not null)
            .Select(message => (message.Attribute!.EventId, Site: _Site(message.Method)))
            .ToList();

        var missing = messages.Where(message => message.EventId < 0).Select(message => $"No EventId: {message.Site}");
        var shared = messages
            .Where(message => message.EventId >= 0)
            .GroupBy(message => message.EventId)
            .Where(group => group.Skip(1).Any())
            .Select(group =>
                $"EventId {group.Key} is shared by: {string.Join(", ", group.Select(x => x.Site).Order(StringComparer.Ordinal))}"
            );

        return [.. missing.Concat(shared).Order(StringComparer.Ordinal)];
    }

    private static string _Site(MethodInfo method) =>
        $"{method.DeclaringType!.Assembly.GetName().Name}:{method.DeclaringType.FullName}.{method.Name}";

    private static IEnumerable<Type> _LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
