using Framework.Abstractions;
using Framework.Orm.EntityFramework.Contexts;
using Framework.Testing.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

public sealed class DbContextBaseTests : IDisposable
{
    private readonly TestDb _db;
    private readonly SqliteConnection _connection;
    private readonly TestClock _clock = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly TestCurrentUser _currentUser = new();
    private readonly SequentialAsStringGuidGenerator _guidGenerator = new();

    public DbContextBaseTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDb>().UseSqlite(_connection).Options;
        _db = new TestDb(_currentUser, _currentTenant, _guidGenerator, _clock, options);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
    }

    private sealed class TestDb(
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        IClock clock,
        DbContextOptions options
    ) : DbContextBase(currentUser, currentTenant, guidGenerator, clock, options)
    {
        public List<EmitterDistributedMessages> EmittedDistributedMessages { get; } = [];

        public List<EmitterLocalMessages> EmittedLocalMessages { get; } = [];

        public override string DefaultSchema => "dbo";

        protected override Task PublishMessagesAsync(
            List<EmitterDistributedMessages> emitters,
            IDbContextTransaction currentTransaction,
            CancellationToken cancellationToken
        )
        {
            EmittedDistributedMessages.AddRange(emitters);

            return Task.CompletedTask;
        }

        protected override void PublishMessages(
            List<EmitterDistributedMessages> emitters,
            IDbContextTransaction currentTransaction
        )
        {
            EmittedDistributedMessages.AddRange(emitters);
        }

        protected override Task PublishMessagesAsync(
            List<EmitterLocalMessages> emitters,
            CancellationToken cancellationToken
        )
        {
            EmittedLocalMessages.AddRange(emitters);

            return Task.CompletedTask;
        }

        protected override void PublishMessages(List<EmitterLocalMessages> emitters)
        {
            EmittedLocalMessages.AddRange(emitters);
        }
    }
}
