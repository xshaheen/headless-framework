// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Encryption;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class BlindIndexerTests : TestBase
{
    private const string _Purpose = "Beneficiary.NationalId";
    private const string _NationalId = "29001011234567";

    private readonly InMemoryDataKeyStore _store = new();

    [Fact]
    public async Task should_hash_the_same_value_the_same_way_under_one_purpose_and_scope()
    {
        using var host = new EncryptionTestHost(_store);
        var context = new BlindIndexContext(_Purpose) { Scope = "charity-a" };

        var first = await host.BlindIndexer.ComputeAsync(context, _NationalId, AbortToken);
        var second = await host.BlindIndexer.ComputeAsync(context, _NationalId, AbortToken);

        first.Hash.Should().Equal(second.Hash);
        first.Hash.Should().HaveCount(BlindIndexContext.MaxLength);
        first.KeyVersion.Should().Be(1);
        _store.Snapshot().Should().ContainSingle(k => k.Kind == DataKeyKind.Lookup && k.Scope == "charity-a");
    }

    [Fact]
    public async Task should_hash_differently_per_purpose_and_per_value()
    {
        using var host = new EncryptionTestHost(_store);

        var nationalId = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);
        var otherPurpose = await host.BlindIndexer.ComputeAsync(
            new BlindIndexContext("Beneficiary.Phone") { Scope = "charity-a" },
            _NationalId,
            AbortToken
        );
        var otherValue = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), "29001011234568", AbortToken);

        otherPurpose.Hash.Should().NotEqual(nationalId.Hash);
        otherValue.Hash.Should().NotEqual(nationalId.Hash);
    }

    [Fact]
    public async Task should_hash_differently_per_tenant_and_the_same_under_the_host_scope()
    {
        using var host = new EncryptionTestHost(_store);

        var tenantA = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);
        var tenantB = await host.BlindIndexer.ComputeAsync(_Tenant("charity-b"), _NationalId, AbortToken);

        BlindIndex hostFromA;
        BlindIndex hostFromB;

        using (host.CurrentTenant.Change("charity-a"))
        {
            hostFromA = await host.BlindIndexer.ComputeAsync(BlindIndexContext.Host(_Purpose), _NationalId, AbortToken);
        }

        using (host.CurrentTenant.Change("charity-b"))
        {
            hostFromB = await host.BlindIndexer.ComputeAsync(BlindIndexContext.Host(_Purpose), _NationalId, AbortToken);
        }

        tenantA.Hash.Should().NotEqual(tenantB.Hash);
        hostFromA.Hash.Should().Equal(hostFromB.Hash);
        hostFromA.Hash.Should().NotEqual(tenantA.Hash);
    }

    [Fact]
    public async Task should_use_the_current_tenant_when_the_context_names_no_scope()
    {
        using var host = new EncryptionTestHost(_store);
        var explicitScope = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);

        using var _ = host.CurrentTenant.Change("charity-a");
        var ambient = await host.BlindIndexer.ComputeAsync(new BlindIndexContext(_Purpose), _NationalId, AbortToken);

        ambient.Hash.Should().Equal(explicitScope.Hash);
    }

    [Fact]
    public async Task should_refuse_a_tenant_scoped_hash_with_no_tenant_instead_of_using_the_host_key()
    {
        using var host = new EncryptionTestHost(_store);

        var act = async () =>
            await host.BlindIndexer.ComputeAsync(new BlindIndexContext(_Purpose), _NationalId, AbortToken);

        await act.Should().ThrowAsync<MissingTenantContextException>().WithMessage("*BlindIndexContext.Host*");
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_truncate_to_the_requested_length()
    {
        using var host = new EncryptionTestHost(_store);

        var full = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);
        var shortened = await host.BlindIndexer.ComputeAsync(
            new BlindIndexContext(_Purpose) { Scope = "charity-a", Length = 16 },
            _NationalId,
            AbortToken
        );

        shortened.Hash.Should().Equal(full.Hash[..16]);
    }

    [Theory]
    [InlineData(BlindIndexContext.MinLength - 1)]
    [InlineData(BlindIndexContext.MaxLength + 1)]
    public void should_refuse_a_length_outside_the_bounds(int length)
    {
        var act = () => new BlindIndexContext(_Purpose) { Length = length };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task should_return_no_candidates_and_create_no_key_for_a_scope_without_a_lookup_key()
    {
        using var host = new EncryptionTestHost(_store);

        var candidates = await host.BlindIndexer.ComputeCandidatesAsync(_Tenant("charity-a"), _NationalId, AbortToken);

        candidates.Should().BeEmpty();
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_match_rows_under_both_versions_after_a_rotation_and_drop_the_old_one_once_retired()
    {
        using var host = new EncryptionTestHost(_store);
        var context = _Tenant("charity-a");
        var stored = await host.BlindIndexer.ComputeAsync(context, _NationalId, AbortToken);

        await host.Manager.RotateAsync("charity-a", DataKeyKind.Lookup, AbortToken);
        var rehashed = await host.BlindIndexer.ComputeAsync(context, _NationalId, AbortToken);
        var duringRotation = await host.BlindIndexer.ComputeCandidatesAsync(context, _NationalId, AbortToken);

        rehashed.KeyVersion.Should().Be(2);
        rehashed.Hash.Should().NotEqual(stored.Hash);
        duringRotation.Select(c => c.KeyVersion).Should().Equal(2, 1);
        duringRotation[0].Hash.Should().Equal(rehashed.Hash);
        duringRotation[1].Hash.Should().Equal(stored.Hash);

        (await host.Manager.RetireAsync("charity-a", DataKeyKind.Lookup, 1, AbortToken)).Should().BeTrue();
        var afterRetirement = await host.BlindIndexer.ComputeCandidatesAsync(context, _NationalId, AbortToken);

        afterRetirement.Should().ContainSingle().Which.KeyVersion.Should().Be(2);
        afterRetirement[0].Hash.Should().Equal(rehashed.Hash);
    }

    [Fact]
    public async Task should_show_another_hosts_rotation_to_queries_at_once()
    {
        using var hostA = new EncryptionTestHost(_store);
        using var hostB = new EncryptionTestHost(_store);
        var context = _Tenant("charity-a");
        await hostB.BlindIndexer.ComputeCandidatesAsync(context, _NationalId, AbortToken);
        await hostA.BlindIndexer.ComputeAsync(context, _NationalId, AbortToken);

        await hostA.Manager.RotateAsync("charity-a", DataKeyKind.Lookup, AbortToken);
        var written = await hostA.BlindIndexer.ComputeAsync(context, _NationalId, AbortToken);
        var candidates = await hostB.BlindIndexer.ComputeCandidatesAsync(context, _NationalId, AbortToken);

        candidates.Should().Contain(c => c.KeyVersion == written.KeyVersion && c.Hash.SequenceEqual(written.Hash));
    }

    [Fact]
    public async Task should_refuse_to_retire_the_active_version_or_a_data_key()
    {
        using var host = new EncryptionTestHost(_store);
        await host.Manager.EnsureAsync("charity-a", AbortToken);

        var retireActive = () => host.Manager.RetireAsync("charity-a", DataKeyKind.Lookup, 1, AbortToken);
        var retireData = () => host.Manager.RetireAsync("charity-a", DataKeyKind.Data, 1, AbortToken);

        await retireActive.Should().ThrowAsync<InvalidOperationException>().WithMessage("*active*");
        await retireData.Should().ThrowAsync<ArgumentException>().WithMessage("*Only lookup keys*");
        _store.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task should_report_false_when_retiring_a_version_that_does_not_exist()
    {
        using var host = new EncryptionTestHost(_store);
        await host.Manager.EnsureAsync("charity-a", AbortToken);

        var retired = await host.Manager.RetireAsync("charity-a", DataKeyKind.Lookup, 7, AbortToken);

        retired.Should().BeFalse();
    }

    [Fact]
    public async Task should_stop_hashing_for_a_shredded_tenant_and_leave_the_host_hash_unchanged()
    {
        using var host = new EncryptionTestHost(_store);
        var hostBefore = await host.BlindIndexer.ComputeAsync(
            BlindIndexContext.Host(_Purpose),
            _NationalId,
            AbortToken
        );
        await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);

        await host.Manager.ShredAsync("charity-a", AbortToken);

        var write = () => host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken).AsTask();
        await write.Should().ThrowAsync<DataKeyScopeShreddedException>();
        var candidates = await host.BlindIndexer.ComputeCandidatesAsync(_Tenant("charity-a"), _NationalId, AbortToken);
        candidates.Should().BeEmpty();
        var hostAfter = await host.BlindIndexer.ComputeAsync(BlindIndexContext.Host(_Purpose), _NationalId, AbortToken);
        hostAfter.Hash.Should().Equal(hostBefore.Hash);
    }

    [Fact]
    public async Task should_hash_under_a_new_key_after_the_tenant_is_reopened()
    {
        using var host = new EncryptionTestHost(_store);
        var before = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);
        await host.Manager.ShredAsync("charity-a", AbortToken);

        await host.Manager.EnsureAsync("charity-a", AbortToken);
        var after = await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);

        after.Hash.Should().NotEqual(before.Hash);
    }

    [Fact]
    public async Task should_keep_the_value_and_key_material_out_of_logs_and_exceptions()
    {
        using var logs = new CapturingLoggerProvider();
        using var host = new EncryptionTestHost(_store, logs: logs);
        await host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken);
        await host.Manager.RotateAsync("charity-a", DataKeyKind.Lookup, AbortToken);
        await host.Manager.RetireAsync("charity-a", DataKeyKind.Lookup, 1, AbortToken);
        await host.Manager.ShredAsync("charity-a", AbortToken);

        var write = () => host.BlindIndexer.ComputeAsync(_Tenant("charity-a"), _NationalId, AbortToken).AsTask();
        var noTenant = () =>
            host.BlindIndexer.ComputeAsync(new BlindIndexContext(_Purpose), _NationalId, AbortToken).AsTask();
        var shredded = await write.Should().ThrowAsync<DataKeyScopeShreddedException>();
        var missing = await noTenant.Should().ThrowAsync<MissingTenantContextException>();

        logs.Messages.Should().NotBeEmpty();
        logs.Messages.Should().NotContain(m => m.Contains(_NationalId, StringComparison.Ordinal));
        logs.Messages.Should().NotContain(m => m.Contains(EncryptionTestHost.KeyOneValue, StringComparison.Ordinal));
        shredded.Which.Message.Should().NotContain(_NationalId);
        missing.Which.Message.Should().NotContain(_NationalId);
    }

    [Fact]
    public void should_compare_blind_indexes_by_version_and_hash_bytes()
    {
        var index = new BlindIndex(1, [1, 2, 3]);
        var same = new BlindIndex(1, [1, 2, 3]);
        var otherVersion = new BlindIndex(2, [1, 2, 3]);
        var otherHash = new BlindIndex(1, [1, 2, 4]);

        (index == same).Should().BeTrue();
        index.Equals((object)same).Should().BeTrue();
        index.GetHashCode().Should().Be(same.GetHashCode());
        (index != otherVersion).Should().BeTrue();
        (index != otherHash).Should().BeTrue();
        index.Equals("not an index").Should().BeFalse();
        default(BlindIndex).Should().Be(default(BlindIndex));
    }

    private static BlindIndexContext _Tenant(string tenantId) => new(_Purpose) { Scope = tenantId };

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
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
                messages.Enqueue(formatter(state, exception) + exception);
            }
        }
    }
}
