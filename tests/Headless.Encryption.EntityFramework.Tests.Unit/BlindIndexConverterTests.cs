// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class BlindIndexConverterTests : TestBase
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _connection.OpenAsync(AbortToken);
        await using var db = _Context();
        await db.Database.EnsureCreatedAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_store_the_index_as_text_and_find_the_row_by_equality()
    {
        var index = _Index(2);
        var id = Guid.CreateVersion7();

        await using (var db = _Context())
        {
            db.Contacts.Add(new Contact { Id = id, PhoneIndex = index });
            await db.SaveChangesAsync(AbortToken);
        }

        await using (var db = _Context())
        {
            var probe = new BlindIndex(index.KeyVersion, [.. index.Hash]);
            var found = await db.Contacts.SingleAsync(c => c.PhoneIndex == probe, AbortToken);

            found.Id.Should().Be(id);
            found.PhoneIndex.Should().Be(index);
            found.EmailIndex.Should().BeNull();
        }

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT PhoneIndex FROM Contacts";
        ((string)(await command.ExecuteScalarAsync(AbortToken))!).Should().Be(index.ToBase64Url());
    }

    [Fact]
    public async Task should_store_an_optional_index_and_null()
    {
        var email = _Index(1);
        var id = Guid.CreateVersion7();

        await using (var db = _Context())
        {
            db.Contacts.Add(
                new Contact
                {
                    Id = id,
                    PhoneIndex = _Index(1),
                    EmailIndex = email,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        }

        await using (var db = _Context())
        {
            (await db.Contacts.SingleAsync(c => c.Id == id, AbortToken)).EmailIndex.Should().Be(email);
        }
    }

    [Fact]
    public async Task should_refuse_to_save_the_default_value()
    {
        await using var db = _Context();
        db.Contacts.Add(new Contact { Id = Guid.CreateVersion7() });

        var act = () => db.SaveChangesAsync(AbortToken);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("BlindIndex?");
    }

    [Fact]
    public async Task should_refuse_to_read_text_that_is_not_a_blind_index()
    {
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Contacts (Id, PhoneIndex) VALUES ($id, $value)";
            command.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$value", "not-an-index");
            await command.ExecuteNonQueryAsync(AbortToken);
        }

        await using var db = _Context();
        var act = () => db.Contacts.ToListAsync(AbortToken);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain(nameof(FormatException));
    }

    private TestContext _Context() => new(new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options);

    private static BlindIndex _Index(int keyVersion)
    {
        var hash = new byte[BlindIndexContext.MaxLength];
        Random.Shared.NextBytes(hash);

        return new BlindIndex(keyVersion, hash);
    }

    private sealed class Contact
    {
        public Guid Id { get; set; }

        public BlindIndex PhoneIndex { get; set; }

        public BlindIndex? EmailIndex { get; set; }
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Contact> Contacts => Set<Contact>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var contact = modelBuilder.Entity<Contact>();
            contact.Property(c => c.Id).ValueGeneratedNever();
            contact.Property(c => c.PhoneIndex).HasConversion<BlindIndexConverter>();
            contact.Property(c => c.EmailIndex).HasConversion<BlindIndexConverter>();
        }
    }
}
