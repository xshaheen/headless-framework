using System.Data.Common;
using Headless.Messaging;
using Headless.Messaging.Transactions;
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
    public required DbSet<Person> Persons { get; set; }
}

#pragma warning disable EF1001 // Internal EF Core API usage.

public class MessagingNpgsqlRelationalConnection : NpgsqlRelationalConnection
{
    private readonly IOutboxTransactionAccessor _transactionAccessor;

    protected MessagingNpgsqlRelationalConnection(
        RelationalConnectionDependencies dependencies,
        DbDataSource dataSource
    )
        : base(dependencies, dataSource)
    {
        _transactionAccessor = dependencies.CurrentContext.Context.GetService<IOutboxTransactionAccessor>();
    }

#pragma warning restore EF1001

    public override void CommitTransaction()
    {
        if (_transactionAccessor.Current != null)
        {
            _transactionAccessor.Current.Commit();
        }
        else
        {
            base.CommitTransaction();
        }
    }

    public override Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _transactionAccessor.Current != null
            ? _transactionAccessor.Current.CommitAsync(cancellationToken)
            : base.CommitTransactionAsync(cancellationToken);
    }

    public override void RollbackTransaction()
    {
        if (_transactionAccessor.Current != null)
        {
            _transactionAccessor.Current.Rollback();
        }
        else
        {
            base.RollbackTransaction();
        }
    }

    public override Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        return _transactionAccessor.Current != null
            ? _transactionAccessor.Current.RollbackAsync(cancellationToken)
            : base.RollbackTransactionAsync(cancellationToken);
    }
}
