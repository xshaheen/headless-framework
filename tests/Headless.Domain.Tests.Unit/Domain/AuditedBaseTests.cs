// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Tests.Domain;

public sealed class AuditedBaseTests
{
    private static readonly DateTimeOffset _Earlier = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _Later = new(2026, 1, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset _Latest = new(2026, 1, 3, 8, 0, 0, TimeSpan.Zero);

    private sealed class Account
    {
        public required string Id { get; init; }
    }

    private sealed class AuditedThing : AuditedEntity<Guid, string, Account>;

    private sealed class AuditedRoot : AuditedAggregateRoot<Guid, string, Account>;

    private sealed class SuspendableThing : SuspendableEntity<Guid, string, Account>;

    private sealed class SuspendableRoot : SuspendableAggregateRoot<Guid, string, Account>;

    private sealed class SoftDeletableThing : SoftDeletableEntity<Guid, string, Account>;

    private sealed class SoftDeletableRoot : SoftDeletableAggregateRoot<Guid, string, Account>;

    public static TheoryData<string> Updatables =>
        [
            nameof(AuditedThing),
            nameof(AuditedRoot),
            nameof(SuspendableThing),
            nameof(SuspendableRoot),
            nameof(SoftDeletableThing),
            nameof(SoftDeletableRoot),
        ];

    public static TheoryData<string> Suspendables => [nameof(SuspendableThing), nameof(SuspendableRoot)];

    public static TheoryData<string> SoftDeletables => [nameof(SoftDeletableThing), nameof(SoftDeletableRoot)];

    private static object _Create(string kind)
    {
        return kind switch
        {
            nameof(AuditedThing) => new AuditedThing { Id = Guid.NewGuid() },
            nameof(AuditedRoot) => new AuditedRoot { Id = Guid.NewGuid() },
            nameof(SuspendableThing) => new SuspendableThing { Id = Guid.NewGuid() },
            nameof(SuspendableRoot) => new SuspendableRoot { Id = Guid.NewGuid() },
            nameof(SoftDeletableThing) => new SoftDeletableThing { Id = Guid.NewGuid() },
            nameof(SoftDeletableRoot) => new SoftDeletableRoot { Id = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null),
        };
    }

    [Fact]
    public void should_carry_only_create_and_update_audit_when_audited_base()
    {
        new AuditedThing { Id = Guid.NewGuid() }
            .Should()
            .BeAssignableTo<Entity<Guid>>();
        new AuditedRoot { Id = Guid.NewGuid() }
            .Should()
            .BeAssignableTo<AggregateRoot<Guid>>();

        foreach (
            var subject in new object[]
            {
                new AuditedThing { Id = Guid.NewGuid() },
                new AuditedRoot { Id = Guid.NewGuid() },
            }
        )
        {
            subject.Should().BeAssignableTo<ICreateAudit<string, Account>>();
            subject.Should().BeAssignableTo<IUpdateAudit<string, Account>>();
            subject.Should().NotBeAssignableTo<ISuspendAudit>();
            subject.Should().NotBeAssignableTo<IDeleteAudit>();
        }
    }

    [Fact]
    public void should_add_only_suspension_when_suspendable_base()
    {
        new SuspendableRoot { Id = Guid.NewGuid() }
            .Should()
            .BeAssignableTo<IDomainEventEmitter>();

        foreach (
            var subject in new object[]
            {
                new SuspendableThing { Id = Guid.NewGuid() },
                new SuspendableRoot { Id = Guid.NewGuid() },
            }
        )
        {
            subject.Should().BeAssignableTo<IUpdateAudit<string, Account>>();
            subject.Should().BeAssignableTo<ISuspendAudit<string, Account>>();
            subject.Should().NotBeAssignableTo<IDeleteAudit>();
        }
    }

    [Fact]
    public void should_add_only_soft_delete_when_soft_deletable_base()
    {
        new SoftDeletableRoot { Id = Guid.NewGuid() }
            .Should()
            .BeAssignableTo<IDomainEventEmitter>();

        foreach (
            var subject in new object[]
            {
                new SoftDeletableThing { Id = Guid.NewGuid() },
                new SoftDeletableRoot { Id = Guid.NewGuid() },
            }
        )
        {
            subject.Should().BeAssignableTo<IUpdateAudit<string, Account>>();
            subject.Should().BeAssignableTo<IDeleteAudit<string, Account>>();
            subject.Should().NotBeAssignableTo<ISuspendAudit>();
        }
    }

    [Theory]
    [MemberData(nameof(Updatables))]
    public void should_record_update_when_update(string kind)
    {
        // given
        var subject = (IUpdateAudit<string, Account>)_Create(kind);
        var by = new Account { Id = "editor" };

        // when
        subject.Update(_Earlier, "editor", by);

        // then
        subject.UpdatedAt.Should().Be(_Earlier);
        subject.UpdatedById.Should().Be("editor");
        subject.UpdatedBy.Should().BeSameAs(by);
    }

    [Theory]
    [MemberData(nameof(Updatables))]
    public void should_replace_previous_update_when_update_again(string kind)
    {
        // given
        var subject = (IUpdateAudit<string, Account>)_Create(kind);
        subject.Update(_Earlier, "first", new Account { Id = "first" });

        // when
        subject.Update(_Later, "second");

        // then
        subject.UpdatedAt.Should().Be(_Later);
        subject.UpdatedById.Should().Be("second");
        subject.UpdatedBy.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Suspendables))]
    public void should_record_suspension_when_suspend(string kind)
    {
        // given
        var subject = (ISuspendAudit<string, Account>)_Create(kind);
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
    [MemberData(nameof(Suspendables))]
    public void should_keep_original_suspension_when_suspend_twice(string kind)
    {
        // given
        var subject = (ISuspendAudit<string, Account>)_Create(kind);
        subject.Suspend(_Earlier, "first");

        // when
        subject.Suspend(_Later, "second");

        // then
        subject.SuspendedAt.Should().Be(_Earlier);
        subject.SuspendedById.Should().Be("first");
    }

    [Theory]
    [MemberData(nameof(Suspendables))]
    public void should_keep_suspension_history_and_record_unsuspension_when_unsuspend(string kind)
    {
        // given
        var subject = (ISuspendAudit<string, Account>)_Create(kind);
        var admin = new Account { Id = "admin" };
        var support = new Account { Id = "support" };
        subject.Suspend(_Earlier, "admin", admin);

        // when
        subject.Unsuspend(_Later, "support", support);

        // then
        subject.IsSuspended.Should().BeFalse();
        subject.SuspendedAt.Should().Be(_Earlier);
        subject.SuspendedById.Should().Be("admin");
        subject.SuspendedBy.Should().BeSameAs(admin);
        subject.UnsuspendedAt.Should().Be(_Later);
        subject.UnsuspendedById.Should().Be("support");
        subject.UnsuspendedBy.Should().BeSameAs(support);
    }

    [Theory]
    [MemberData(nameof(Suspendables))]
    public void should_record_new_suspension_and_keep_last_unsuspension_when_suspend_again(string kind)
    {
        // given
        var subject = (ISuspendAudit<string, Account>)_Create(kind);
        subject.Suspend(_Earlier, "admin");
        subject.Unsuspend(_Later, "support");

        // when
        subject.Suspend(_Latest, "auditor");

        // then
        subject.IsSuspended.Should().BeTrue();
        subject.SuspendedAt.Should().Be(_Latest);
        subject.SuspendedById.Should().Be("auditor");
        subject.UnsuspendedAt.Should().Be(_Later);
        subject.UnsuspendedById.Should().Be("support");
    }

    [Theory]
    [MemberData(nameof(Suspendables))]
    public void should_record_null_actor_when_unsuspend_and_resuspend_without_actor(string kind)
    {
        // given
        var subject = (ISuspendAudit<string, Account>)_Create(kind);
        subject.Suspend(_Earlier, "user-a", new Account { Id = "user-a" });

        // when
        subject.Unsuspend(_Later);
        var unsuspendedById = subject.UnsuspendedById;
        var unsuspendedBy = subject.UnsuspendedBy;
        subject.Suspend(_Latest);

        // then
        unsuspendedById.Should().BeNull();
        unsuspendedBy.Should().BeNull();
        subject.SuspendedAt.Should().Be(_Latest);
        subject.SuspendedById.Should().BeNull();
        subject.SuspendedBy.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Suspendables))]
    public void should_do_nothing_when_unsuspend_not_suspended(string kind)
    {
        // given
        var subject = (ISuspendAudit<string, Account>)_Create(kind);

        // when
        subject.Unsuspend(_Later, "support");

        // then
        subject.IsSuspended.Should().BeFalse();
        subject.UnsuspendedAt.Should().BeNull();
        subject.UnsuspendedById.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(SoftDeletables))]
    public void should_record_deletion_when_delete(string kind)
    {
        // given
        var subject = (IDeleteAudit<string, Account>)_Create(kind);
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
    [MemberData(nameof(SoftDeletables))]
    public void should_keep_original_deletion_when_delete_twice(string kind)
    {
        // given
        var subject = (IDeleteAudit<string, Account>)_Create(kind);
        subject.Delete(_Earlier, "first");

        // when
        subject.Delete(_Later, "second");

        // then
        subject.DeletedAt.Should().Be(_Earlier);
        subject.DeletedById.Should().Be("first");
    }

    [Theory]
    [MemberData(nameof(SoftDeletables))]
    public void should_keep_deletion_history_and_record_restoration_when_restore(string kind)
    {
        // given
        var subject = (IDeleteAudit<string, Account>)_Create(kind);
        var admin = new Account { Id = "admin" };
        var support = new Account { Id = "support" };
        subject.Delete(_Earlier, "admin", admin);

        // when
        subject.Restore(_Later, "support", support);

        // then
        subject.IsDeleted.Should().BeFalse();
        subject.DeletedAt.Should().Be(_Earlier);
        subject.DeletedById.Should().Be("admin");
        subject.DeletedBy.Should().BeSameAs(admin);
        subject.RestoredAt.Should().Be(_Later);
        subject.RestoredById.Should().Be("support");
        subject.RestoredBy.Should().BeSameAs(support);
    }

    [Theory]
    [MemberData(nameof(SoftDeletables))]
    public void should_record_null_actor_when_restore_and_redelete_without_actor(string kind)
    {
        // given
        var subject = (IDeleteAudit<string, Account>)_Create(kind);
        subject.Delete(_Earlier, "user-a", new Account { Id = "user-a" });

        // when
        subject.Restore(_Later);
        var restoredById = subject.RestoredById;
        var restoredBy = subject.RestoredBy;
        subject.Delete(_Latest);

        // then
        restoredById.Should().BeNull();
        restoredBy.Should().BeNull();
        subject.DeletedAt.Should().Be(_Latest);
        subject.DeletedById.Should().BeNull();
        subject.DeletedBy.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(SoftDeletables))]
    public void should_do_nothing_when_restore_not_deleted(string kind)
    {
        // given
        var subject = (IDeleteAudit<string, Account>)_Create(kind);

        // when
        subject.Restore(_Later, "support");

        // then
        subject.IsDeleted.Should().BeFalse();
        subject.RestoredAt.Should().BeNull();
        subject.RestoredById.Should().BeNull();
    }
}
