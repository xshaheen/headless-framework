// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application types and variables the caching guide's examples assume.

// BCL and provider SDK namespaces the examples use; a consumer's IDE adds these usings.
global using System.Buffers;
global using System.Net;
global using static CachingAmbient;
global using StackExchange.Redis;
using Headless.Caching;
using Headless.Serializer;
using Microsoft.AspNetCore.OutputCaching;

public sealed record Report(string Id);

public interface IReportRepository
{
    ValueTask<Report?> BuildExpensiveReportAsync(CancellationToken cancellationToken);
}

public sealed class FailSafeNotifier
{
    public ValueTask FailSafeAsync(CacheFailSafeTrigger trigger, CancellationToken cancellationToken) => default;
}

public sealed class MyBinarySerializer : IBinarySerializer
{
    public void Serialize<T>(T value, IBufferWriter<byte> output) => throw new NotSupportedException();

    public void Serialize(object? value, IBufferWriter<byte> output) => throw new NotSupportedException();

    public T? Deserialize<T>(ReadOnlyMemory<byte> data) => throw new NotSupportedException();

    public T? Deserialize<T>(in ReadOnlySequence<byte> data) => throw new NotSupportedException();

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();

    public object? Deserialize(in ReadOnlySequence<byte> data, Type type) => throw new NotSupportedException();
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class CachingAmbient
{
    public static IConnectionMultiplexer redis => null!;

    public static IConnectionMultiplexer sessionsRedis => null!;

    public static ICache cache => null!;

    public static object product => null!;

    public static ILogger logger => null!;

    public static FailSafeNotifier notifier => null!;

    public static IOutputCacheStore outputCacheStore => null!;

    public static string[] GetProducts() => [];
}
