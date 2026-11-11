// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>The tracked context of the scenarios: two unrelated tables and one entity stored in two tables.</summary>
internal sealed class LiveDb(DbContextOptions<LiveDb> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<Counter> Counters => Set<Counter>();

    public DbSet<Profile> Profiles => Set<Profile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().Property(x => x.Id).ValueGeneratedNever();
    }
}

internal sealed class Note
{
    public int Id { get; set; }

    public required string Text { get; set; }
}
