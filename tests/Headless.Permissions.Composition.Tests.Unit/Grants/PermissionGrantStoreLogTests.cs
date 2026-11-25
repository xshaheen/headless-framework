// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.Grants;

/// <summary>
/// Cache-key lists log as plain collection properties. Native logging gives a <c>{@Name}</c> hole no meaning for a list
/// of strings and only adds the <c>@</c> to the property key, so the keys stay <c>CacheKeys</c> and <c>Names</c>.
/// </summary>
public sealed class PermissionGrantStoreLogTests : TestBase
{
    [Fact]
    public void should_log_found_cache_keys_under_a_plain_property_name()
    {
        var logger = new CapturingLogger();

        logger.LogFoundInCache(["k1", "k2"]);

        var entry = logger.Entries.Single();
        entry.Message.Should().Be("Found in the cache: k1, k2");
        entry.Properties.Should().Contain(p => p.Key == "CacheKeys");
        entry.Properties.Should().NotContain(p => p.Key.StartsWith('@'));
    }

    [Fact]
    public void should_log_requested_cache_keys_under_a_plain_property_name()
    {
        var logger = new CapturingLogger();

        logger.LogGetCachedItems(["k1"]);

        var entry = logger.Entries.Single();
        entry.Message.Should().Be("PermissionStore._GetCachedItemsAsync: k1");
        entry.Properties.Should().Contain(p => p.Key == "CacheKeys");
        entry.Properties.Should().NotContain(p => p.Key.StartsWith('@'));
    }

    [Fact]
    public void should_log_missing_names_under_a_plain_property_name()
    {
        var logger = new CapturingLogger();

        logger.LogNotFoundInCache(["n1", "n2"]);

        var entry = logger.Entries.Single();
        entry.Message.Should().Be("Not found in the cache: n1, n2");
        entry.Properties.Should().Contain(p => p.Key == "Names");
        entry.Properties.Should().NotContain(p => p.Key.StartsWith('@'));
    }

    private sealed record LogEntry(string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? [.. pairs]
                : Array.Empty<KeyValuePair<string, object?>>();
            Entries.Add(new LogEntry(formatter(state, exception), properties));
        }
    }
}
