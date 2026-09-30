// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Domain;

public sealed class AuditedBaseTests
{
    private static readonly DateTimeOffset _Earlier = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _Later = new(2026, 1, 2, 8, 0, 0, TimeSpan.Zero);

    private sealed class Account
    {
        public required string Id { get; init; }
    }

    // Both bases implement the same transition contracts; each test runs against both through these interfaces.
    private interface IAuditedSubject : ISuspendAudit<string, Account>, IDeleteAudit<string, Account>;

    private sealed class AuditedThing : AuditedEntity<Guid, string, Account>, IAuditedSubject;

    private sealed class AuditedRoot : AuditedAggregateRoot<Guid, string, Account>, IAuditedSubject;

    public static TheoryData<string> Subjects => [nameof(AuditedThing), nameof(AuditedRoot)];

    private static IAuditedSubject _Create(string kind)
    {
        return kind switch
        {
            nameof(AuditedThing) => new AuditedThing { Id = Guid.NewGuid() },
            nameof(AuditedRoot) => new AuditedRoot { Id = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
    }

    [Fact]
    public void should_implement_every_audit_contract_when_audited_entity()
    {
        var entity = new AuditedThing { Id = Guid.NewGuid() };

        entity.Should().BeAssignableTo<Entity<Guid>>();
        entity.Should().BeAssignableTo<ICreateAudit<string, Account>>();
        entity.Should().BeAssignableTo<IUpdateAudit<string, Account>>();
    }

    [Fact]
    public void should_implement_every_audit_contract_and_emit_events_when_audited_aggregate_root()
    {
        var root = new AuditedRoot { Id = Guid.NewGuid() };

        root.Should().BeAssignableTo<AggregateRoot<Guid>>();
        root.Should().BeAssignableTo<IDomainEventEmitter>();
        root.Should().BeAssignableTo<ICreateAudit<string, Account>>();
        root.Should().BeAssignableTo<IUpdateAudit<string, Account>>();
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_record_suspension_when_suspend(string kind)
    {
        // given
        var subject = _Create(kind);
        var by = new Account { Id = "admin" };

        // when
        subject.Suspend(_Earlier, "admin", by);

        // then
        subject.IsSuspended.Should().BeTrue();
        subject.SuspendedAt.Should().Be(_Earlier);
        subject.SuspendedById.Should().Be("admin");
        subject.SuspendedBy.Should().BeSameAs(by);
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_keep_original_suspension_when_suspend_twice(string kind)
    {
        // given
        var subject = _Create(kind);
        subject.Suspend(_Earlier, "first");

        // when
        subject.Suspend(_Later, "second");

        // then
        subject.SuspendedAt.Should().Be(_Earlier);
        subject.SuspendedById.Should().Be("first");
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_clear_suspension_and_record_unsuspension_when_unsuspend(string kind)
    {
        // given
        var subject = _Create(kind);
        var by = new Account { Id = "support" };
        subject.Suspend(_Earlier, "admin", new Account { Id = "admin" });

        // when
        subject.Unsuspend(_Later, "support", by);

        // then
        subject.IsSuspended.Should().BeFalse();
        subject.SuspendedAt.Should().BeNull();
        subject.SuspendedById.Should().BeNull();
        subject.SuspendedBy.Should().BeNull();
        subject.UnsuspendedAt.Should().Be(_Later);
        subject.UnsuspendedById.Should().Be("support");
        subject.UnsuspendedBy.Should().BeSameAs(by);
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_do_nothing_when_unsuspend_not_suspended(string kind)
    {
        // given
        var subject = _Create(kind);

        // when
        subject.Unsuspend(_Later, "support");

        // then
        subject.IsSuspended.Should().BeFalse();
        subject.UnsuspendedAt.Should().BeNull();
        subject.UnsuspendedById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_record_deletion_when_delete(string kind)
    {
        // given
        var subject = _Create(kind);
        var by = new Account { Id = "admin" };

        // when
        subject.Delete(_Earlier, "admin", by);

        // then
        subject.IsDeleted.Should().BeTrue();
        subject.DeletedAt.Should().Be(_Earlier);
        subject.DeletedById.Should().Be("admin");
        subject.DeletedBy.Should().BeSameAs(by);
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_keep_original_deletion_when_delete_twice(string kind)
    {
        // given
        var subject = _Create(kind);
        subject.Delete(_Earlier, "first");

        // when
        subject.Delete(_Later, "second");

        // then
        subject.DeletedAt.Should().Be(_Earlier);
        subject.DeletedById.Should().Be("first");
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_clear_deletion_and_record_restoration_when_restore(string kind)
    {
        // given
        var subject = _Create(kind);
        var by = new Account { Id = "support" };
        subject.Delete(_Earlier, "admin", new Account { Id = "admin" });

        // when
        subject.Restore(_Later, "support", by);

        // then
        subject.IsDeleted.Should().BeFalse();
        subject.DeletedAt.Should().BeNull();
        subject.DeletedById.Should().BeNull();
        subject.DeletedBy.Should().BeNull();
        subject.RestoredAt.Should().Be(_Later);
        subject.RestoredById.Should().Be("support");
        subject.RestoredBy.Should().BeSameAs(by);
    }

    [Theory]
    [MemberData(nameof(Subjects))]
    public void should_do_nothing_when_restore_not_deleted(string kind)
    {
        // given
        var subject = _Create(kind);

        // when
        subject.Restore(_Later, "support");

        // then
        subject.IsDeleted.Should().BeFalse();
        subject.RestoredAt.Should().BeNull();
        subject.RestoredById.Should().BeNull();
    }
}
