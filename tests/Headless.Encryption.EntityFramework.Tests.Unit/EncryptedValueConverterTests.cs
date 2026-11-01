// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class EncryptedValueConverterTests : TestBase
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
    public async Task should_store_the_frame_bytes_and_read_back_an_equal_value()
    {
        var frame = _Frame();
        var id = Guid.CreateVersion7();

        await using (var db = _Context())
        {
            db.Beneficiaries.Add(new Beneficiary { Id = id, NationalId = frame });
            await db.SaveChangesAsync(AbortToken);
        }

        await using (var db = _Context())
        {
            var stored = await db.Beneficiaries.SingleAsync(b => b.Id == id, AbortToken);

            stored.NationalId.Should().Be(frame);
            stored.NationalId.KeyId.Should().Be(frame.KeyId);
            stored.Phone.Should().BeNull();
        }

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT NationalId FROM Beneficiaries";
        ((byte[])(await command.ExecuteScalarAsync(AbortToken))!).Should().Equal(frame.ToArray());
    }

    [Fact]
    public async Task should_store_an_optional_value_and_null()
    {
        var phone = _Frame();
        var id = Guid.CreateVersion7();

        await using (var db = _Context())
        {
            db.Beneficiaries.Add(
                new Beneficiary
                {
                    Id = id,
                    NationalId = _Frame(),
                    Phone = phone,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        }

        await using (var db = _Context())
        {
            (await db.Beneficiaries.SingleAsync(b => b.Id == id, AbortToken)).Phone.Should().Be(phone);
        }
    }

    [Fact]
    public async Task should_refuse_to_save_the_default_value()
    {
        await using var db = _Context();
        db.Beneficiaries.Add(new Beneficiary { Id = Guid.CreateVersion7() });

        var act = () => db.SaveChangesAsync(AbortToken);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("EncryptedValue?");
    }

    [Fact]
    public async Task should_refuse_to_read_bytes_that_are_not_a_frame()
    {
        await using (var command = _connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Beneficiaries (Id, NationalId) VALUES ($id, $value)";
            command.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString().ToUpperInvariant());
            command.Parameters.AddWithValue("$value", new byte[] { 0xF0, 0xF1 });
            await command.ExecuteNonQueryAsync(AbortToken);
        }

        await using var db = _Context();
        var act = () => db.Beneficiaries.ToListAsync(AbortToken);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain(nameof(FormatException));
    }

    [Fact]
    public void should_map_both_shapes_to_a_binary_column()
    {
        using var db = _Context();
        var entity = db.Model.FindEntityType(typeof(Beneficiary))!;

        entity
            .FindProperty(nameof(Beneficiary.NationalId))!
            .GetValueConverter()
            .Should()
            .BeOfType<EncryptedValueConverter>();
        entity
            .FindProperty(nameof(Beneficiary.Phone))!
            .GetValueConverter()
            .Should()
            .BeOfType<EncryptedValueConverter>();
        entity.FindProperty(nameof(Beneficiary.Phone))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(Beneficiary.NationalId))!.GetColumnType().Should().Be("BLOB");
    }

    private TestContext _Context() => new(new DbContextOptionsBuilder<TestContext>().UseSqlite(_connection).Options);

    private static EncryptedValue _Frame()
    {
        var frame = new byte[EncryptedValue.Overhead + 14];
        frame[0] = EncryptedValue.FormatVersion;
        frame[1] = 0x01;
        Guid.CreateVersion7().TryWriteBytes(frame.AsSpan(2, 16), bigEndian: true, out _);
        Random.Shared.NextBytes(frame.AsSpan(18));

        return EncryptedValue.FromBytes(frame);
    }

    private sealed class Beneficiary
    {
        public Guid Id { get; init; }

        public EncryptedValue NationalId { get; init; }

        public EncryptedValue? Phone { get; init; }
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Beneficiary> Beneficiaries => Set<Beneficiary>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.AddEncryptedValueConversions();
        }
    }
}
