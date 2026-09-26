// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.Internal;

public sealed class MessagingOutboxesTests : TestBase
{
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();

    [Fact]
    public void should_return_the_primary_answer_unchanged_when_no_additional_outbox_is_registered()
    {
        // given
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        _Answer(primary, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.StorageProvider));
        var sut = new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, []);

        // when
        var coordination = sut.Resolve(_unit);

        // then
        coordination.Status.Should().Be(DeliveryCoordinationStatus.Incompatible);
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.StorageProvider);
    }

    [Fact]
    public void should_keep_the_primary_storage_when_the_primary_database_matches()
    {
        // given
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        _Answer(primary, DeliveryCoordination.Compatible(_unit, transaction: null));
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing");
        var sut = new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, [billing]);

        // when
        var coordination = sut.Resolve(_unit);

        // then — no storage is stamped, so the row is written to the primary storage
        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
        coordination.Storage.Should().BeNull();
        ((IDeliveryCoordinationResolver)billing.Storage).DidNotReceive().Resolve(Arg.Any<IUnitOfWork>());
    }

    [Fact]
    public void should_route_to_the_additional_outbox_whose_database_matches_the_unit()
    {
        // given
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        _Answer(primary, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database));
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing");
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping");
        _Answer(billing.Storage, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database));
        _Answer(shipping.Storage, DeliveryCoordination.Compatible(_unit, transaction: null));
        var sut = new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, [billing, shipping]);

        // when
        var coordination = sut.Resolve(_unit);

        // then
        coordination.Status.Should().Be(DeliveryCoordinationStatus.Compatible);
        coordination.UnitOfWork.Should().BeSameAs(_unit);
        coordination.Storage.Should().BeSameAs(shipping.Storage);
    }

    [Fact]
    public void should_report_a_database_mismatch_when_no_outbox_matches_even_if_another_provider_refused_first()
    {
        // given — a SQL Server primary refuses a PostgreSQL unit by provider; the PostgreSQL outbox by database
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        _Answer(primary, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.StorageProvider));
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing");
        _Answer(billing.Storage, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database));
        var sut = new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, [billing]);

        // when
        var coordination = sut.Resolve(_unit);

        // then
        coordination.Status.Should().Be(DeliveryCoordinationStatus.Incompatible);
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.Database);
    }

    [Fact]
    public void should_report_a_completed_transaction_over_a_database_mismatch()
    {
        // given
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        _Answer(primary, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.TransactionCompleted));
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing");
        _Answer(billing.Storage, DeliveryCoordination.Incompatible(DeliveryCoordinationMismatch.Database));
        var sut = new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, [billing]);

        // when
        var coordination = sut.Resolve(_unit);

        // then
        coordination.Mismatch.Should().Be(DeliveryCoordinationMismatch.TransactionCompleted);
    }

    [Fact]
    public void should_treat_a_resource_less_unit_as_no_unit_on_a_host_without_storage()
    {
        // given
        _unit.Resource.Returns((IUnitOfWorkResource?)null);
        var sut = new MessagingOutboxes(primary: null, primaryCoordination: null, []);

        // when
        var coordination = sut.Resolve(_unit);

        // then
        coordination.Status.Should().Be(DeliveryCoordinationStatus.None);
    }

    [Fact]
    public void should_refuse_two_outboxes_on_the_same_database_naming_both_registrations()
    {
        // given
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        var billing = AdditionalOutboxDoubles.CreateOutbox(
            "billing",
            name: "AddOutbox().UseEntityFramework<BillingDb>()"
        );
        var duplicate = AdditionalOutboxDoubles.CreateOutbox("billing", name: "AddOutbox().UsePostgreSql(...)");

        // when
        var act = () => new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, [billing, duplicate]);

        // then
        act.Should()
            .Throw<MessagingConfigurationException>()
            .WithMessage(
                "*'AddOutbox().UseEntityFramework<BillingDb>()' and 'AddOutbox().UsePostgreSql(...)' both resolve to database 'billing'*"
            );
    }

    [Fact]
    public void should_refuse_an_additional_outbox_on_the_primary_database()
    {
        // given — the loopback spelling differs, the database is the same
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders", dataSource: "localhost:5432");
        var orders = AdditionalOutboxDoubles.CreateOutbox(
            "orders",
            dataSource: "127.0.0.1:5432",
            name: "AddOutbox().UsePostgreSql(...)"
        );

        // when
        var act = () => new MessagingOutboxes(primary, (IDeliveryCoordinationResolver)primary, [orders]);

        // then
        act.Should()
            .Throw<MessagingConfigurationException>()
            .WithMessage("*'the primary storage' and 'AddOutbox().UsePostgreSql(...)'*");
    }

    [Fact]
    public void should_refuse_an_additional_outbox_without_a_primary_storage()
    {
        var act = () =>
            new MessagingOutboxes(
                primary: null,
                primaryCoordination: null,
                [AdditionalOutboxDoubles.CreateOutbox("billing")]
            );

        act.Should().Throw<MessagingConfigurationException>().WithMessage("AddOutbox() requires a primary*");
    }

    [Fact]
    public void should_refuse_an_additional_outbox_next_to_a_non_relational_primary_storage()
    {
        // given — an in-memory primary joins every unit, so it would swallow the additional outbox's publishes
        var primary = Substitute.For<IDataStorage, IDeliveryCoordinationResolver>();

        // when
        var act = () =>
            new MessagingOutboxes(
                primary,
                (IDeliveryCoordinationResolver)primary,
                [AdditionalOutboxDoubles.CreateOutbox("billing")]
            );

        // then
        act.Should().Throw<MessagingConfigurationException>().WithMessage("*requires a relational primary*");
    }

    [Fact]
    public void should_refuse_an_outbox_storage_that_cannot_join_a_unit_of_work()
    {
        var act = () =>
            new MessagingOutbox(
                "AddOutbox().UseX()",
                Substitute.For<IDataStorage>(),
                Substitute.For<IStorageInitializer>()
            );

        act.Should().Throw<MessagingConfigurationException>().WithMessage("*'AddOutbox().UseX()'*relational*");
    }

    [Fact]
    public void should_derive_the_same_lock_key_for_the_same_database_and_a_different_one_for_another()
    {
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing");
        var sameBilling = AdditionalOutboxDoubles.CreateOutbox("billing", dataSource: "DB-HOST:5432");
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping");

        billing.LockKey.Should().Be(sameBilling.LockKey).And.HaveLength(16);
        billing.LockKey.Should().NotBe(shipping.LockKey);
    }

    [Fact]
    public void should_fail_messaging_registration_when_an_added_outbox_has_no_storage()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessMessaging(setup => setup.AddOutbox());

        act.Should().Throw<InvalidOperationException>().WithMessage("AddOutbox() was called without a storage*");
    }

    [Fact]
    public void should_refuse_a_second_storage_on_one_added_outbox()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessMessaging(setup =>
            {
                var outbox = setup.AddOutbox();
                outbox.UseStorage("first", _ => AdditionalOutboxDoubles.CreateOutbox("billing"));
                outbox.UseStorage("second", _ => AdditionalOutboxDoubles.CreateOutbox("billing"));
            });

        act.Should().Throw<InvalidOperationException>().WithMessage("This outbox already has a storage*");
    }

    [Fact]
    public void should_give_each_added_outbox_its_own_options_name()
    {
        var services = new ServiceCollection();
        OutboxStorageBuilder? first = null;
        OutboxStorageBuilder? second = null;

        services.AddHeadlessMessaging(setup =>
        {
            first = setup.AddOutbox();
            first.UseStorage("first", _ => AdditionalOutboxDoubles.CreateOutbox("billing"));
            second = setup.AddOutbox();
            second.UseStorage("second", _ => AdditionalOutboxDoubles.CreateOutbox("shipping"));
        });

        first!.OptionsName.Should().NotBe(second!.OptionsName);
        services.Count(descriptor => descriptor.ServiceType == typeof(OutboxStorageRegistration)).Should().Be(2);
    }

    private static void _Answer(IDataStorage storage, DeliveryCoordination answer)
    {
        ((IDeliveryCoordinationResolver)storage).Resolve(Arg.Any<IUnitOfWork>()).Returns(answer);
    }
}
