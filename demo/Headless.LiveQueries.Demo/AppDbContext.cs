// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Demo;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<Counter> Counters => Set<Counter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A todo never moves to another owner. Only a column that cannot change after insert may narrow a live
        // query by row: the copy of a row a context holds could otherwise be older than the stored one.
        modelBuilder
            .Entity<Todo>()
            .Property(todo => todo.OwnerId)
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);

        modelBuilder.Entity<Counter>().Property(counter => counter.Version).IsConcurrencyToken();
    }
}
