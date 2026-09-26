// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application DbContext and the variables the distributed-locks guide's examples assume.

global using static DistributedLocksAmbient;
// GetDbTransaction is an EF Core extension that EF-using code already imports.
global using Microsoft.EntityFrameworkCore.Storage;
// Provider SDK types the examples name; a consumer's IDE adds these usings.
global using ConnectionMultiplexer = StackExchange.Redis.ConnectionMultiplexer;
global using IConnectionMultiplexer = StackExchange.Redis.IConnectionMultiplexer;
global using SqlConnection = Microsoft.Data.SqlClient.SqlConnection;
global using SqlTransaction = Microsoft.Data.SqlClient.SqlTransaction;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class DistributedLocksAmbient
{
    public static IDistributedLock lockProvider => null!;

    public static IDistributedReadWriteLock readerWriterLocks => null!;

    public static IDistributedSemaphoreProvider semaphoreProvider => null!;

    public static DistributedLockOptions options => null!;

    public static string connectionString => null!;

    public static Npgsql.NpgsqlDataSource dataSource => null!;

    public static Npgsql.NpgsqlConnection connection => null!;

    public static IUnitOfWorkFactory factory => null!;

    public static AppDbContext db => null!;

    public static Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData => null!;
}
