// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Tests;

/// <summary>
/// The tracked context of the scenarios: two unrelated tables, one entity stored in two tables, the entities of
/// the row-matching scenarios in <c>Tickets.cs</c>, and the rows that refer to others in the ways the
/// referential-action scenarios need.
/// </summary>
internal sealed class LiveDb(DbContextOptions<LiveDb> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<Counter> Counters => Set<Counter>();

    public DbSet<Profile> Profiles => Set<Profile>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Folder> Folders => Set<Folder>();

    public DbSet<Shelf> Shelves => Set<Shelf>();

    public DbSet<Sheet> Sheets => Set<Sheet>();

    public DbSet<Label> Labels => Set<Label>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Memo> Memos => Set<Memo>();

    public DbSet<Badge> Badges => Set<Badge>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<Section> Sections => Set<Section>();

    public DbSet<Keyword> Keywords => Set<Keyword>();

    public DbSet<Binder> Binders => Set<Binder>();

    public DbSet<RingBinder> RingBinders => Set<RingBinder>();

    /// <summary>The rows of the join table between keywords and sheets, which has no entity class.</summary>
    public DbSet<Dictionary<string, object>> SheetKeywords => Set<Dictionary<string, object>>(SheetKeywordsName);

    public const string SheetKeywordsName = "SheetKeywords";

    /// <summary>Stands for a function of the database, which a query calls by this name.</summary>
    public static int Magnitude(int value) => throw new NotSupportedException("Runs in the database only.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(() => Magnitude(0)).HasName("abs").IsBuiltIn();

        modelBuilder.Entity<Todo>(todo =>
        {
            todo.Property(x => x.Id).ValueGeneratedNever();

            // Read through the getter, so a test can make one read of the property throw.
            todo.Property(x => x.Title).UsePropertyAccessMode(PropertyAccessMode.Property);
            todo.Ignore(x => x.FailNextTitleRead);
        });

        modelBuilder.Entity<Counter>().Property(x => x.Id).ValueGeneratedNever();

        modelBuilder.Entity<Profile>(profile =>
        {
            profile.Property(x => x.Id).ValueGeneratedNever();
            profile.ToTable("Profiles").SplitToTable("ProfileDetails", details => details.Property(x => x.Bio));
        });

        modelBuilder.Entity<Ticket>(ticket =>
        {
            // Fixed when the row is inserted, which is what lets a filter on them be matched against a saved row.
            // Read through the getter, so a test can make one read of the property throw.
            _Immutable(ticket.Property(x => x.OwnerId).UsePropertyAccessMode(PropertyAccessMode.Property));
            ticket.Ignore(x => x.FailNextOwnerRead);
            _Immutable(ticket.Property(x => x.Kind));
            _Immutable(ticket.Property(x => x.Tenant));
            _Immutable(ticket.Property(x => x.Urgent));
            _Immutable(ticket.Property(x => x.Region));
            _Immutable(ticket.Property(x => x.Code));
        });

        modelBuilder.Entity<Comment>().Property(x => x.Id).ValueGeneratedNever();

        modelBuilder.Entity<Folder>(folder =>
        {
            folder.Property(x => x.Id).ValueGeneratedNever();
            folder.Navigation(x => x.Shelf).AutoInclude();
        });

        modelBuilder.Entity<Shelf>().Property(x => x.Id).ValueGeneratedNever();

        modelBuilder.Entity<Sheet>().Property(x => x.Id).ValueGeneratedNever();

        modelBuilder.Entity<Label>(label =>
        {
            label.Property(x => x.Id).ValueGeneratedNever();
            label.HasOne(x => x.Shelf).WithMany().HasForeignKey(x => x.ShelfId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Property(x => x.Id).ValueGeneratedNever();
            _Immutable(invoice.Property(x => x.OwnerId));

            // Reads other rows of the table the query is over, so the SQL names no second table.
            invoice.HasQueryFilter(x => !Invoices.Any(other => other.Voids == x.Id));
        });

        modelBuilder.Entity<Badge>(badge =>
        {
            badge.HasKey(x => new { x.Shelf, x.Code });
            _Immutable(badge.Property(x => x.OwnerId));
        });

        modelBuilder.Entity<Bookmark>(bookmark =>
        {
            bookmark.Property(x => x.Id).ValueGeneratedNever();
            bookmark
                .HasOne(x => x.Sheet)
                .WithMany()
                .HasForeignKey(x => x.SheetId)
                .OnDelete(DeleteBehavior.ClientCascade);
        });

        modelBuilder.Entity<Section>(section =>
        {
            section.Property(x => x.Id).ValueGeneratedNever();
            section.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Keyword>(keyword =>
        {
            keyword.Property(x => x.Id).ValueGeneratedNever();
            keyword.HasMany(x => x.Sheets).WithMany().UsingEntity(SheetKeywordsName);
        });

        modelBuilder.Entity<Binder>(binder =>
        {
            binder.UseTptMappingStrategy();
            binder.Property(x => x.Id).ValueGeneratedNever();
            binder.HasOne(x => x.Shelf).WithMany().HasForeignKey(x => x.ShelfId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RingBinder>().ToTable("RingBinders");

        modelBuilder.Entity<Memo>(memo =>
        {
            memo.Property(x => x.Id).ValueGeneratedNever();
            _Immutable(memo.Property(x => x.OwnerId));
            memo.HasQueryFilter(x => !x.Hidden);
        });
    }

    private static void _Immutable<T>(PropertyBuilder<T> property)
    {
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

internal sealed class Todo
{
    public int Id { get; set; }

    public string Title
    {
        get
        {
            if (FailNextTitleRead)
            {
                FailNextTitleRead = false;

                throw new InvalidOperationException("Simulated failure reading a property of a tracked entity.");
            }

            return field;
        }
        set;
    } = "";

    /// <summary>Makes the next read of <see cref="Title" /> throw, once.</summary>
    public bool FailNextTitleRead { get; set; }
}

internal sealed class Counter
{
    public int Id { get; set; }

    public int Value { get; set; }
}

internal sealed class Profile
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Bio { get; set; }
}

/// <summary>A second tracked context type over the same database, as a second module of one application has.</summary>
internal sealed class NotesDb(DbContextOptions<NotesDb> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<TicketRow> TicketRows => Set<TicketRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().Property(x => x.Id).ValueGeneratedNever();

        // The table belongs to LiveDb's model, which creates it.
        modelBuilder.Entity<TicketRow>().ToTable("Tickets", table => table.ExcludeFromMigrations());
    }
}

internal sealed class Note
{
    public int Id { get; set; }

    public required string Text { get; set; }
}
