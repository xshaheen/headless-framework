// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.MultiTenancy;
using Headless.Testing.Tests;

namespace Tests;

public sealed class BlindIndexerBatchTests : TestBase
{
    private const string _Purpose = "Beneficiary.NationalId";
    private const string _Scope = "charity-a";

    // A repeated value proves each position gets its own result rather than a deduplicated set.
    private static readonly string[] _Values = ["29001011234567", "29001011234568", "29001011234567", "30112311234561"];

    private readonly InMemoryDataKeyStore _store = new();

    [Theory]
    [InlineData(false, BlindIndexContext.MaxLength)]
    [InlineData(false, 16)]
    [InlineData(true, BlindIndexContext.MaxLength)]
    public async Task should_hash_each_value_as_the_single_call_does_in_input_order(bool hostScope, int length)
    {
        // given
        using var host = new EncryptionTestHost(_store);
        var context = new BlindIndexContext(_Purpose)
        {
            Scope = hostScope ? FieldContext.HostScope : _Scope,
            Length = length,
        };

        // when
        var batch = await host.BlindIndexer.ComputeAllAsync(context, _Values, AbortToken);

        // then
        var single = new List<BlindIndex>();

        foreach (var value in _Values)
        {
            single.Add(await host.BlindIndexer.ComputeAsync(context, value, AbortToken));
        }

        batch.Should().Equal(single);
        batch.Should().HaveElementAt(2, batch[0]);
        batch[0].Should().NotBe(batch[1]);
        batch.Should().OnlyContain(x => x.Hash.Length == length && x.KeyVersion == 1);
    }

    [Fact]
    public async Task should_hash_a_batch_under_the_rotated_version_after_a_rotation()
    {
        // given
        using var host = new EncryptionTestHost(_store);
        var context = _Tenant();
        var before = await host.BlindIndexer.ComputeAllAsync(context, _Values, AbortToken);
        await host.Manager.RotateAsync(_Scope, DataKeyKind.Lookup, AbortToken);

        // when
        var after = await host.BlindIndexer.ComputeAllAsync(context, _Values, AbortToken);

        // then
        after.Should().OnlyContain(x => x.KeyVersion == 2);
        after.Should().HaveElementAt(0, await host.BlindIndexer.ComputeAsync(context, _Values[0], AbortToken));
        after[0].Hash.Should().NotEqual(before[0].Hash);
    }

    [Fact]
    public async Task should_read_the_active_key_once_for_a_batch_on_a_cold_host()
    {
        // given - another host created the key, so this host's cache is empty
        using (var writer = new EncryptionTestHost(_store))
        {
            await writer.BlindIndexer.ComputeAsync(_Tenant(), _Values[0], AbortToken);
        }

        using var host = new EncryptionTestHost(_store);
        var readsBefore = _store.ActiveReads;

        // when
        var batch = await host.BlindIndexer.ComputeAllAsync(_Tenant(), _Values, AbortToken);

        // then
        (_store.ActiveReads - readsBefore)
            .Should()
            .Be(1);
        _store.ScopeListings.Should().Be(0);
        batch.Should().HaveCount(_Values.Length);
    }

    [Fact]
    public async Task should_create_one_lookup_key_for_the_first_batch_of_a_scope()
    {
        using var host = new EncryptionTestHost(_store);

        var batch = await host.BlindIndexer.ComputeAllAsync(_Tenant(), _Values, AbortToken);

        batch.Should().OnlyContain(x => x.KeyVersion == 1);
        _store.Snapshot().Should().ContainSingle(k => k.Kind == DataKeyKind.Lookup && k.Scope == _Scope);
    }

    [Fact]
    public async Task should_refuse_a_batch_for_a_shredded_scope_without_reopening_it()
    {
        // given
        using var host = new EncryptionTestHost(_store);
        await host.BlindIndexer.ComputeAsync(_Tenant(), _Values[0], AbortToken);
        await host.Manager.ShredAsync(_Scope, AbortToken);

        // when
        var write = () => host.BlindIndexer.ComputeAllAsync(_Tenant(), _Values, AbortToken).AsTask();

        // then
        await write.Should().ThrowAsync<DataKeyScopeShreddedException>();
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_each_values_candidates_as_the_single_call_does_across_versions()
    {
        // given - rows hashed under version 1, then a rotation, so a query needs both versions
        using var host = new EncryptionTestHost(_store);
        var context = _Tenant();
        await host.BlindIndexer.ComputeAsync(context, _Values[0], AbortToken);
        await host.Manager.RotateAsync(_Scope, DataKeyKind.Lookup, AbortToken);

        // when
        var batch = await host.BlindIndexer.ComputeAllCandidatesAsync(context, _Values, AbortToken);

        // then
        batch.Should().HaveCount(_Values.Length);

        for (var i = 0; i < _Values.Length; i++)
        {
            var single = await host.BlindIndexer.ComputeCandidatesAsync(context, _Values[i], AbortToken);
            batch[i].Should().Equal(single);
            batch[i].Select(x => x.KeyVersion).Should().Equal(2, 1);
        }

        batch[0].Should().Equal(batch[2]);
    }

    [Fact]
    public async Task should_list_the_scope_once_for_a_batch_of_candidates_where_single_calls_list_it_per_value()
    {
        // given
        using var host = new EncryptionTestHost(_store);
        await host.BlindIndexer.ComputeAsync(_Tenant(), _Values[0], AbortToken);
        var listingsBefore = _store.ScopeListings;

        // when
        await host.BlindIndexer.ComputeAllCandidatesAsync(_Tenant(), _Values, AbortToken);
        var batchListings = _store.ScopeListings - listingsBefore;

        foreach (var value in _Values)
        {
            await host.BlindIndexer.ComputeCandidatesAsync(_Tenant(), value, AbortToken);
        }

        var singleListings = _store.ScopeListings - listingsBefore - batchListings;

        // then
        batchListings.Should().Be(1);
        singleListings.Should().Be(_Values.Length);
    }

    [Fact]
    public async Task should_see_another_hosts_rotation_in_the_next_batch_of_candidates()
    {
        // given
        using var hostA = new EncryptionTestHost(_store);
        using var hostB = new EncryptionTestHost(_store);
        var context = _Tenant();
        await hostA.BlindIndexer.ComputeAsync(context, _Values[0], AbortToken);
        await hostB.BlindIndexer.ComputeAllCandidatesAsync(context, _Values, AbortToken);

        // when
        await hostA.Manager.RotateAsync(_Scope, DataKeyKind.Lookup, AbortToken);
        var written = await hostA.BlindIndexer.ComputeAsync(context, _Values[1], AbortToken);
        var candidates = await hostB.BlindIndexer.ComputeAllCandidatesAsync(context, _Values, AbortToken);

        // then
        candidates[1].Should().Contain(written);
    }

    [Fact]
    public async Task should_return_empty_candidates_per_value_and_create_no_key_for_a_scope_without_a_lookup_key()
    {
        using var host = new EncryptionTestHost(_store);

        var candidates = await host.BlindIndexer.ComputeAllCandidatesAsync(_Tenant(), _Values, AbortToken);

        candidates.Should().HaveCount(_Values.Length).And.OnlyContain(x => x.Count == 0);
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_empty_candidates_per_value_for_a_shredded_scope()
    {
        // given
        using var host = new EncryptionTestHost(_store);
        await host.BlindIndexer.ComputeAsync(_Tenant(), _Values[0], AbortToken);
        await host.Manager.ShredAsync(_Scope, AbortToken);

        // when
        var candidates = await host.BlindIndexer.ComputeAllCandidatesAsync(_Tenant(), _Values, AbortToken);

        // then
        candidates.Should().HaveCount(_Values.Length).And.OnlyContain(x => x.Count == 0);
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_return_empty_for_an_empty_batch_without_reading_the_store()
    {
        using var host = new EncryptionTestHost(_store);

        var indexes = await host.BlindIndexer.ComputeAllAsync(_Tenant(), [], AbortToken);
        var candidates = await host.BlindIndexer.ComputeAllCandidatesAsync(_Tenant(), [], AbortToken);

        indexes.Should().BeEmpty();
        candidates.Should().BeEmpty();
        _store.ActiveReads.Should().Be(0);
        _store.ScopeListings.Should().Be(0);
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_batch_with_a_lone_surrogate_before_creating_a_key()
    {
        // given - the bad value is last, so a check made while hashing would already have created the key
        using var host = new EncryptionTestHost(_store);
        string[] values = [.. _Values, "a\uD800"];

        // when
        var write = async () => await host.BlindIndexer.ComputeAllAsync(_Tenant(), values, AbortToken);
        var query = async () => await host.BlindIndexer.ComputeAllCandidatesAsync(_Tenant(), values, AbortToken);

        // then
        (await write.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("values"))
            .Which.Message.Should()
            .Contain($"index {_Values.Length}")
            .And.NotContain(_Values[0]);
        await query.Should().ThrowExactlyAsync<ArgumentException>().WithParameterName("values");
        _store.Snapshot().Should().BeEmpty();
        _store.ScopeListings.Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_a_batch_with_a_null_value_before_creating_a_key()
    {
        using var host = new EncryptionTestHost(_store);
        string[] values = [_Values[0], null!];

        var write = async () => await host.BlindIndexer.ComputeAllAsync(_Tenant(), values, AbortToken);
        var query = async () => await host.BlindIndexer.ComputeAllCandidatesAsync(_Tenant(), values, AbortToken);
        var missing = async () => await host.BlindIndexer.ComputeAllAsync(_Tenant(), null!, AbortToken);

        await write.Should().ThrowAsync<ArgumentException>().WithParameterName("values");
        await query.Should().ThrowAsync<ArgumentException>().WithParameterName("values");
        await missing.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("values");
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task should_refuse_a_tenant_scoped_batch_with_no_tenant_instead_of_using_the_host_key()
    {
        using var host = new EncryptionTestHost(_store);
        var context = new BlindIndexContext(_Purpose);

        var write = async () => await host.BlindIndexer.ComputeAllAsync(context, _Values, AbortToken);
        var query = async () => await host.BlindIndexer.ComputeAllCandidatesAsync(context, _Values, AbortToken);

        await write.Should().ThrowAsync<MissingTenantContextException>();
        await query.Should().ThrowAsync<MissingTenantContextException>();
        _store.Snapshot().Should().BeEmpty();
    }

    private static BlindIndexContext _Tenant() => new(_Purpose) { Scope = _Scope };
}
