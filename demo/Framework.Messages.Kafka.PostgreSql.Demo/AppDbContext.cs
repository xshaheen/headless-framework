using System.Data.Common;
using Framework.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql.EntityFrameworkCore.PostgreSQL.Storage.Internal;

namespace Demo;

public class Person
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public int Age { get; set; }

    public override string ToString()
    {
        return $"Name:{Name}, Age:{Age}";
    }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Person> Persons { get; set; }
}

#pragma warning disable EF1001 // Internal EF Core API usage.

public class CapNpgsqlRelationalConnection : NpgsqlRelationalConnection
{
    private readonly IOutboxPublisher _publisher;

    protected CapNpgsqlRelationalConnection(RelationalConnectionDependencies dependencies, DbDataSource dataSource)
        : base(dependencies, dataSource)
    {
        _publisher = dependencies.CurrentContext.Context.GetService<IOutboxPublisher>();
    }

#pragma warning restore EF1001

    public override void CommitTransaction()
    {
        if (_publisher.Transaction != null)
        {
            _publisher.Transaction.Commit();
        }
        else
        {
            base.CommitTransaction();
        }
    }

    public override Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _publisher.Transaction != null
            ? _publisher.Transaction.CommitAsync(cancellationToken)
            : base.CommitTransactionAsync(cancellationToken);
    }

    public override void RollbackTransaction()
    {
        if (_publisher.Transaction != null)
        {
            _publisher.Transaction.Rollback();
        }
        else
        {
            base.RollbackTransaction();
        }
    }

    public override Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _publisher.Transaction != null
            ? _publisher.Transaction.RollbackAsync(cancellationToken)
            : base.RollbackTransactionAsync(cancellationToken);
    }
}
