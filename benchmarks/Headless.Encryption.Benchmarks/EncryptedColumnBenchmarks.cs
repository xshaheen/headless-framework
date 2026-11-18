// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Headless.Encryption.Benchmarks;

/// <summary>
/// Reads and saves rows with three encrypted columns through EF Core on in-memory SQLite, so the bytes allocated per
/// row include every frame copy the converter and <see cref="EncryptedValue" /> make.
/// </summary>
#pragma warning disable CA1001 // BenchmarkDotNet owns the lifecycle: GlobalCleanup disposes the connection.
[MemoryDiagnoser]
public class EncryptedColumnBenchmarks
{
    private const int _RowCount = 1000;

    private SqliteConnection _connection = null!;
    private DbContextOptions<BenchmarkContext> _options = null!;
    private Person[] _toSave = [];

    /// <summary>Plaintext length of each column; a frame adds <see cref="EncryptedValue.Overhead" /> bytes.</summary>
    [Params(16, 256)]
    public int PlaintextLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<BenchmarkContext>().UseSqlite(_connection).Options;

        using var db = new BenchmarkContext(_options);
        db.Database.EnsureCreated();
        db.People.AddRange(_People(_RowCount));
        db.SaveChanges();

        _toSave = _People(_RowCount);
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark]
    public int Materialize()
    {
        using var db = new BenchmarkContext(_options);

        return db.People.AsNoTracking().ToList().Count;
    }

    [Benchmark]
    public int Save()
    {
        using var db = new BenchmarkContext(_options);
        db.Saved.AddRange(
            _toSave.Select(static p => new SavedPerson
            {
                Id = Guid.CreateVersion7(),
                NationalId = p.NationalId,
                Phone = p.Phone,
                Email = p.Email,
            })
        );
        var written = db.SaveChanges();
        db.Saved.ExecuteDelete();

        return written;
    }

    private Person[] _People(int count)
    {
        var people = new Person[count];

        for (var i = 0; i < count; i++)
        {
            people[i] = new Person
            {
                Id = Guid.CreateVersion7(),
                NationalId = _Frame(),
                Phone = _Frame(),
                Email = _Frame(),
            };
        }

        return people;
    }

    // A well-formed frame: the converter checks only its shape, so no key ring is needed to read or write it.
    private EncryptedValue _Frame()
    {
        var frame = new byte[EncryptedValue.Overhead + PlaintextLength];
        frame[0] = EncryptedValue.FormatVersion;
        frame[1] = 0x01;
        Random.Shared.NextBytes(frame.AsSpan(2));

        return EncryptedValue.FromBytes(frame);
    }

    public sealed class Person
    {
        public Guid Id { get; init; }

        public EncryptedValue NationalId { get; init; }

        public EncryptedValue Phone { get; init; }

        public EncryptedValue Email { get; init; }
    }

    // Its own table, so the rows a save writes never join the rows a materialization reads.
    public sealed class SavedPerson
    {
        public Guid Id { get; init; }

        public EncryptedValue NationalId { get; init; }

        public EncryptedValue Phone { get; init; }

        public EncryptedValue Email { get; init; }
    }

    private sealed class BenchmarkContext(DbContextOptions<BenchmarkContext> options) : DbContext(options)
    {
        public DbSet<Person> People => Set<Person>();

        public DbSet<SavedPerson> Saved => Set<SavedPerson>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddEncryptedValueConversions();
        }
    }
}
#pragma warning restore CA1001
