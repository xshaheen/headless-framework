// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using Headless.Logging.Internal;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The entry the redacting logger opens lets an enricher look up a key of the entry's own state, without allocating for
/// the struct states that message templates and generated log methods use.
/// </summary>
public sealed class LoggedEntryTests : TestBase
{
    [Fact]
    public void should_read_a_struct_state_without_allocating()
    {
        // given: the call warmed up once, so only the per-entry cost is measured
        var state = new StructState("TenantId", "acme");
        _HasProperty(state, "TenantId");

        // when
        var before = GC.GetAllocatedBytesForCurrentThread();
        var found = _HasProperty(state, "TenantId");
        var missing = _HasProperty(state, "Other");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // then
        found.Should().BeTrue();
        missing.Should().BeFalse();
        allocated.Should().Be(0);
    }

    [Fact]
    public void should_read_a_reference_state_and_ignore_a_state_that_is_not_a_property_list()
    {
        IReadOnlyList<KeyValuePair<string, object?>> list = [new("TenantId", "acme")];

        _HasProperty(list, "TenantId").Should().BeTrue();
        _HasProperty(list, "Other").Should().BeFalse();
        _HasProperty("a plain string state", "TenantId").Should().BeFalse();
        _HasProperty(42, "TenantId").Should().BeFalse();
    }

    [Fact]
    public void should_see_the_innermost_entry_and_restore_the_outer_one_after_a_nested_log()
    {
        // given: a provider that logs while it handles an entry nests a second one of the same state type
        var outer = new StructState("TenantId", "acme");
        var inner = new StructState("Other", "x");

        // when / then
        LoggedEntry.HasProperty("TenantId").Should().BeFalse("no entry is open outside a logging call");

        using (LoggedEntry.Enter(outer))
        {
            using (LoggedEntry.Enter(inner))
            {
                LoggedEntry.HasProperty("TenantId").Should().BeFalse();
                LoggedEntry.HasProperty("Other").Should().BeTrue();
            }

            LoggedEntry.HasProperty("TenantId").Should().BeTrue();
        }

        LoggedEntry.HasProperty("TenantId").Should().BeFalse();
    }

    private static bool _HasProperty<TState>(TState state, string key)
    {
        using var entry = LoggedEntry.Enter(state);

        return LoggedEntry.HasProperty(key);
    }

    /// <summary>A one-property state shaped like the structs message templates and generated log methods pass.</summary>
    private readonly struct StructState(string key, object? value) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 1;

        public KeyValuePair<string, object?> this[int index] =>
            index == 0 ? new(key, value) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
