// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// The provider-neutral contract of the encryption key stores, run against a real database. A provider leaf derives
/// from it, supplies its <see cref="IEncryptionFixture" />, and overrides every test with <c>[Fact]</c>.
/// </summary>
/// <remarks>
/// Tests that share the default table use their own tenant scopes; tests that touch every key (re-wrap, paging) get a
/// table of their own.
/// </remarks>
public abstract class EncryptionConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IEncryptionFixture
{
    private const string _SharedTable = "enc_shared";
    private const string _Purpose = "Beneficiary.NationalId";
    private const string _NationalId = "29001011234567";

    protected TFixture Fixture { get; } = fixture;

    public virtual async Task should_provision_the_host_keys_and_round_trip_a_tenant_value()
    {
        await using var host = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);

        // The deploy step ran; the host-keys check a starting host runs now passes.
        await host
            .Services.GetServices<IResourceProvisioner>()
            .Single(p => string.Equals(p.Kind, "EncryptionKeys", StringComparison.Ordinal))
            .VerifyAsync(AbortToken);

        var hostKeys = await host.Store.ListScopeAsync(FieldContext.HostScope, AbortToken);
        hostKeys.Select(k => k.Kind).Should().BeEquivalentTo([DataKeyKind.Data, DataKeyKind.Lookup]);

        using (host.CurrentTenant.Change(_Scope("round-trip")))
        {
            var encrypted = await host.Encryptor.EncryptAsync(_NationalId, new FieldContext(_Purpose), AbortToken);
            var decrypted = await host.Encryptor.DecryptAsync(encrypted, new FieldContext(_Purpose), AbortToken);

            decrypted.GetString().Should().Be(_NationalId);
            decrypted.KeyIsCurrent.Should().BeTrue();
        }
    }

    public virtual async Task should_create_one_data_key_when_two_hosts_race_on_a_new_tenant()
    {
        await using var hostA = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        var scope = _Scope("race");
        var context = new FieldContext(_Purpose) { Scope = scope };

        var frames = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(i =>
                    Task.Run(
                        async () =>
                            await (i % 2 == 0 ? hostA : hostB).Encryptor.EncryptAsync(_NationalId, context, AbortToken),
                        AbortToken
                    )
                )
        );

        var keys = await hostA.Store.ListScopeAsync(scope, AbortToken);
        keys.Should().ContainSingle().Which.Version.Should().Be(1);
        frames.Select(f => f.KeyId).Distinct().Should().ContainSingle().Which.Should().Be(keys[0].KeyId);
        (await hostB.Encryptor.DecryptStringAsync(frames[0], context, AbortToken)).Should().Be(_NationalId);
        (await hostA.Encryptor.DecryptStringAsync(frames[1], context, AbortToken)).Should().Be(_NationalId);
    }

    public virtual async Task should_decrypt_both_versions_and_flag_the_old_one_after_rotation()
    {
        await using var host = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        var scope = _Scope("rotate");
        var context = new FieldContext(_Purpose) { Scope = scope };
        var old = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);

        var newKeyId = await host.Manager.RotateAsync(scope, DataKeyKind.Data, AbortToken);
        var fresh = await host.Encryptor.EncryptAsync(_NationalId, context, AbortToken);

        fresh.KeyId.Should().Be(newKeyId).And.NotBe(old.KeyId);
        (await host.Store.GetActiveAsync(scope, DataKeyKind.Data, AbortToken))!.Version.Should().Be(2);

        var oldDecryption = await host.Encryptor.DecryptAsync(old, context, AbortToken);
        oldDecryption.GetString().Should().Be(_NationalId);
        oldDecryption.KeyIsCurrent.Should().BeFalse();
        (await host.Encryptor.DecryptAsync(fresh, context, AbortToken)).KeyIsCurrent.Should().BeTrue();
    }

    public virtual async Task should_stop_decrypting_on_every_host_within_the_cache_duration_after_a_shred()
    {
        await using var hostA = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        await using var hostB = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        var shredded = _Scope("shred");
        var kept = _Scope("kept");
        var shreddedContext = new FieldContext(_Purpose) { Scope = shredded };
        var keptContext = new FieldContext(_Purpose) { Scope = kept };
        var gone = await hostA.Encryptor.EncryptAsync(_NationalId, shreddedContext, AbortToken);
        var stays = await hostA.Encryptor.EncryptAsync(_NationalId, keptContext, AbortToken);
        (await hostB.Encryptor.DecryptStringAsync(gone, shreddedContext, AbortToken)).Should().Be(_NationalId);

        (await hostA.Manager.ShredAsync(shredded, AbortToken)).Should().Be(1);

        (await hostA.Store.ListScopeAsync(shredded, AbortToken)).Should().BeEmpty();
        await _ShouldBeMissingAsync(hostA, gone, shreddedContext);

        hostB.Clock.Advance(TimeSpan.FromMinutes(5));

        await _ShouldBeMissingAsync(hostB, gone, shreddedContext);
        (await hostB.Encryptor.DecryptStringAsync(stays, keptContext, AbortToken)).Should().Be(_NationalId);
    }

    public virtual async Task should_rewrap_every_key_so_old_values_decrypt_under_the_new_key_encryption_key()
    {
        var table = EncryptionFixtureExtensions.UniqueTable();
        var context = new FieldContext(_Purpose) { Scope = _Scope("rewrap") };
        EncryptedValue encrypted;

        await using (var before = await Fixture.CreateHostAsync(table, cancellationToken: AbortToken))
        {
            encrypted = await before.Encryptor.EncryptAsync(_NationalId, context, AbortToken);
        }

        await using (
            var rotating = await Fixture.CreateHostAsync(
                table,
                EncryptionFixtureExtensions.UseBothKeysWithKeyTwoActive,
                AbortToken
            )
        )
        {
            // The host Data and Lookup keys plus the tenant's Data key.
            (await rotating.Manager.RewrapAsync(AbortToken))
                .Should()
                .Be(3);
            (await rotating.Manager.RewrapAsync(AbortToken)).Should().Be(0);
            (await rotating.Store.ListAsync(null, 100, AbortToken))
                .Should()
                .OnlyContain(k => k.WrappedKey.KekId == EncryptionFixtureExtensions.KeyTwo);
        }

        await using var after = await Fixture.CreateHostAsync(
            table,
            EncryptionFixtureExtensions.UseKeyTwoOnly,
            AbortToken
        );
        (await after.Encryptor.DecryptStringAsync(encrypted, context, AbortToken)).Should().Be(_NationalId);
    }

    public virtual async Task should_refuse_an_insert_whose_version_is_taken()
    {
        await using var host = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        var scope = _Scope("insert");

        (await host.Store.TryInsertAsync(_Key(scope, version: 1), AbortToken)).Should().BeTrue();
        (await host.Store.TryInsertAsync(_Key(scope, version: 1), AbortToken)).Should().BeFalse();
        (await host.Store.TryInsertAsync(_Key(scope, version: 2), AbortToken)).Should().BeTrue();

        var active = await host.Store.GetActiveAsync(scope, DataKeyKind.Data, AbortToken);
        active!.Version.Should().Be(2);
        (await host.Store.GetAsync(active.KeyId, AbortToken))!.WrappedKey.Ciphertext.Should().Equal(1, 2, 3);
        (await host.Store.GetActiveAsync(scope, DataKeyKind.Lookup, AbortToken)).Should().BeNull();
        (await host.Store.GetAsync(Guid.CreateVersion7(), AbortToken)).Should().BeNull();
    }

    public virtual async Task should_keep_scopes_that_differ_only_by_case_apart()
    {
        await using var host = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        var lower = _Scope("case");
        var upper = lower.ToUpperInvariant();

        await host.Manager.EnsureAsync(lower, AbortToken);
        await host.Manager.EnsureAsync(upper, AbortToken);
        (await host.Manager.ShredAsync(upper, AbortToken)).Should().Be(2);

        (await host.Store.ListScopeAsync(lower, AbortToken)).Should().HaveCount(2);
    }

    public virtual async Task should_page_every_key_in_id_order()
    {
        var table = EncryptionFixtureExtensions.UniqueTable();
        await using var host = await Fixture.CreateHostAsync(table, cancellationToken: AbortToken);

        for (var i = 0; i < 4; i++)
        {
            await host.Manager.EnsureAsync(_Scope("page"), AbortToken);
        }

        var all = new List<StoredDataKey>();
        Guid? after = null;

        while (true)
        {
            var page = await host.Store.ListAsync(after, 3, AbortToken);
            all.AddRange(page);

            if (page.Count < 3)
            {
                break;
            }

            after = page[^1].KeyId;
        }

        // Two host keys and two keys for each of four tenants, each read once.
        all.Should().HaveCount(10);
        all.Should().OnlyHaveUniqueItems(k => k.KeyId);
    }

    public virtual async Task should_replace_the_wrapped_form_of_one_key()
    {
        await using var host = await Fixture.CreateHostAsync(_SharedTable, cancellationToken: AbortToken);
        var key = _Key(_Scope("replace"), version: 1);
        await host.Store.TryInsertAsync(key, AbortToken);

        var replaced = await host.Store.ReplaceWrappedKeyAsync(
            key.KeyId,
            "Other",
            new WrappedDataKey { KekId = "kek-9", Ciphertext = [0xF0, 0xF1] },
            AbortToken
        );

        replaced.Should().BeTrue();
        var stored = await host.Store.GetAsync(key.KeyId, AbortToken);
        stored!.KekProvider.Should().Be("Other");
        stored.WrappedKey.KekId.Should().Be("kek-9");
        stored.WrappedKey.Ciphertext.Should().Equal([0xF0, 0xF1]);
        (await host.Store.ReplaceWrappedKeyAsync(Guid.CreateVersion7(), "Other", stored.WrappedKey, AbortToken))
            .Should()
            .BeFalse();
    }

    public virtual async Task should_fail_the_start_naming_the_deploy_step_when_the_key_table_is_missing()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessEncryption(setup =>
        {
            Fixture.ConfigureKeyStore(setup, EncryptionFixtureExtensions.UniqueTable());
            setup.UseLocalKey(EncryptionFixtureExtensions.UseKeyOne);
        });
        using var host = builder.Build();

        await FluentActions
            .Awaiting(() => host.StartAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*resources provision*");
    }

    public virtual async Task should_report_an_unreachable_database_from_the_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessEncryption(setup =>
        {
            Fixture.ConfigureUnreachableKeyStore(setup);
            setup.UseLocalKey(EncryptionFixtureExtensions.UseKeyOne);
        });
        await using var provider = services.BuildServiceProvider();
        var encryptor = provider.GetRequiredService<IFieldEncryptor>();

        var act = () => encryptor.EncryptAsync(_NationalId, FieldContext.Host(_Purpose), AbortToken).AsTask();

        await act.Should().ThrowAsync<Exception>();
    }

    private async Task _ShouldBeMissingAsync(EncryptionHost host, EncryptedValue value, FieldContext context)
    {
        var act = () => host.Encryptor.DecryptAsync(value, context, AbortToken).AsTask();

        (await act.Should().ThrowAsync<FieldDecryptionException>())
            .Which.Failure.Should()
            .Be(FieldDecryptionFailure.KeyNotFound);
    }

    private static string _Scope(string name) => $"{name}-{Guid.NewGuid():N}"[..Math.Min(40, name.Length + 33)];

    private static StoredDataKey _Key(string scope, int version) =>
        new()
        {
            KeyId = Guid.CreateVersion7(),
            Scope = scope,
            Kind = DataKeyKind.Data,
            Version = version,
            KekProvider = "Local",
            WrappedKey = new WrappedDataKey { KekId = "kek-1", Ciphertext = [1, 2, 3] },
        };
}
