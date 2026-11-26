// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Primitives;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// Proves on each engine that a lost uniqueness race reports the violated constraint's name and that
/// <c>SaveChangesOrConflictAsync</c> turns only the mapped one into the caller's own conflict.
/// </summary>
public abstract class UniqueConflictSaveTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : IUniqueConflictFixture
{
    protected static readonly ErrorDescriptor EmailTaken = new("members:email_taken", "The email is already taken.");

    [Fact]
    public async Task should_report_the_unique_index_name_when_a_concurrent_insert_took_the_value()
    {
        var email = _NewValue();
        await _InsertAsync(new Member { Email = email, Code = _NewValue() });

        var save = () => _InsertAsync(new Member { Email = email, Code = _NewValue() });

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(UniqueConflictDbContext.EmailIndex);
    }

    [Fact]
    public async Task should_report_the_unique_constraint_name_of_an_alternate_key()
    {
        var code = _NewValue();
        await _InsertAsync(new Member { Email = _NewValue(), Code = code });

        var save = () => _InsertAsync(new Member { Email = _NewValue(), Code = code });

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(UniqueConflictDbContext.CodeKey);
    }

    [Fact]
    public async Task should_report_the_primary_key_name_of_a_duplicate_id()
    {
        var id = Guid.NewGuid();
        await _InsertAsync(
            new Member
            {
                Id = id,
                Email = _NewValue(),
                Code = _NewValue(),
            }
        );

        var save = () =>
            _InsertAsync(
                new Member
                {
                    Id = id,
                    Email = _NewValue(),
                    Code = _NewValue(),
                }
            );

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(UniqueConflictDbContext.PrimaryKey);
    }

    [Fact]
    public async Task should_throw_the_mapped_conflict_when_the_save_violates_the_mapped_index()
    {
        var email = _NewValue();
        await _InsertAsync(new Member { Email = email, Code = _NewValue() });

        await using var db = fixture.CreateContext();
        db.Members.Add(new Member { Email = email, Code = _NewValue() });

        var save = async () =>
            await db.SaveChangesOrConflictAsync(UniqueConflictDbContext.EmailIndex, EmailTaken, AbortToken);

        var conflict = (await save.Should().ThrowAsync<ConflictException>()).Which;
        conflict.Errors.Should().ContainSingle().Which.Should().BeSameAs(EmailTaken);
        conflict.InnerException.Should().BeOfType<DbUpdateException>();
    }

    [Fact]
    public async Task should_propagate_the_violation_unchanged_when_the_save_violates_an_unmapped_constraint()
    {
        var code = _NewValue();
        await _InsertAsync(new Member { Email = _NewValue(), Code = code });

        await using var db = fixture.CreateContext();
        db.Members.Add(new Member { Email = _NewValue(), Code = code });

        var save = async () =>
            await db.SaveChangesOrConflictAsync(UniqueConflictDbContext.EmailIndex, EmailTaken, AbortToken);

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().Be(UniqueConflictDbContext.CodeKey);
    }

    [Fact]
    public async Task should_propagate_a_failure_that_is_not_a_unique_violation()
    {
        await using var db = fixture.CreateContext();
        // The column holds 64 characters, so the engine rejects the row on its length, not on a key.
        db.Members.Add(new Member { Email = new string('x', 65), Code = _NewValue() });

        var save = async () =>
            await db.SaveChangesOrConflictAsync(UniqueConflictDbContext.EmailIndex, EmailTaken, AbortToken);

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        RelationalFaults.IsUniqueViolation(exception).Should().BeFalse();
    }

    [Fact]
    public async Task should_save_normally_when_no_constraint_is_violated()
    {
        await using var db = fixture.CreateContext();
        db.Members.Add(new Member { Email = _NewValue(), Code = _NewValue() });

        var written = await db.SaveChangesOrConflictAsync(UniqueConflictDbContext.EmailIndex, EmailTaken, AbortToken);

        written.Should().Be(1);
    }

    protected async Task _InsertAsync(Member member)
    {
        await using var db = fixture.CreateContext();
        db.Members.Add(member);
        await db.SaveChangesAsync(AbortToken);
    }

    protected static string _NewValue() => Guid.NewGuid().ToString("N");

    protected TFixture Fixture => fixture;
}

public interface IUniqueConflictFixture
{
    UniqueConflictDbContext CreateContext();
}

public sealed class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Email { get; set; }

    public required string Code { get; set; }
}

public sealed class UniqueConflictDbContext(DbContextOptions<UniqueConflictDbContext> options) : DbContext(options)
{
    public const string PrimaryKey = "PK_UniqueConflictMembers";
    public const string EmailIndex = "IX_UniqueConflictMembers_Email";
    public const string CodeKey = "AK_UniqueConflictMembers_Code";

    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>(member =>
        {
            member.ToTable("UniqueConflictMembers");
            member.HasKey(x => x.Id).HasName(PrimaryKey);
            member.Property(x => x.Email).HasMaxLength(64);
            member.Property(x => x.Code).HasMaxLength(64);
            member.HasIndex(x => x.Email).IsUnique().HasDatabaseName(EmailIndex);
            member.HasAlternateKey(x => x.Code).HasName(CodeKey);
        });
    }
}

[UsedImplicitly]
public sealed class PostgreSqlUniqueConflictFixture : HeadlessPostgreSqlFixture, IUniqueConflictFixture
{
    public UniqueConflictDbContext CreateContext()
    {
        return new(new DbContextOptionsBuilder<UniqueConflictDbContext>().UseNpgsql(ConnectionString).Options);
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }
}

[UsedImplicitly]
public sealed class SqlServerUniqueConflictFixture : HeadlessSqlServerFixture, IUniqueConflictFixture
{
    public UniqueConflictDbContext CreateContext()
    {
        return new(new DbContextOptionsBuilder<UniqueConflictDbContext>().UseSqlServer(ConnectionString).Options);
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }
}

public sealed class PostgreSqlUniqueConflictSaveTests(PostgreSqlUniqueConflictFixture fixture)
    : UniqueConflictSaveTests<PostgreSqlUniqueConflictFixture>(fixture),
        IClassFixture<PostgreSqlUniqueConflictFixture>;

public sealed class SqlServerUniqueConflictSaveTests(SqlServerUniqueConflictFixture fixture)
    : UniqueConflictSaveTests<SqlServerUniqueConflictFixture>(fixture),
        IClassFixture<SqlServerUniqueConflictFixture>
{
    [Fact]
    public async Task should_report_the_violation_without_a_name_when_the_session_language_localizes_the_message()
    {
        var email = _NewValue();
        await _InsertAsync(new Member { Email = email, Code = _NewValue() });

        await using var db = Fixture.CreateContext();
        await db.Database.OpenConnectionAsync(AbortToken);
        await db.Database.ExecuteSqlRawAsync("SET LANGUAGE Deutsch", AbortToken);
        db.Members.Add(new Member { Email = email, Code = _NewValue() });

        var save = () => db.SaveChangesAsync(AbortToken);

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        exception
            .InnerException!.Message.Should()
            .NotStartWith("Cannot insert duplicate key", "the server localized it");
        RelationalFaults.IsUniqueViolation(exception, out var constraintName).Should().BeTrue();
        constraintName.Should().BeNull();
    }

    [Fact]
    public async Task should_propagate_the_violation_when_the_localized_message_hides_the_mapped_name()
    {
        var email = _NewValue();
        await _InsertAsync(new Member { Email = email, Code = _NewValue() });

        await using var db = Fixture.CreateContext();
        await db.Database.OpenConnectionAsync(AbortToken);
        await db.Database.ExecuteSqlRawAsync("SET LANGUAGE Deutsch", AbortToken);
        db.Members.Add(new Member { Email = email, Code = _NewValue() });

        var save = async () =>
            await db.SaveChangesOrConflictAsync(UniqueConflictDbContext.EmailIndex, EmailTaken, AbortToken);

        var exception = (await save.Should().ThrowAsync<DbUpdateException>()).Which;
        RelationalFaults.IsUniqueViolation(exception).Should().BeTrue();
    }
}
