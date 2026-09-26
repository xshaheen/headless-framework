// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application types, test host, and variables the testing guide's examples assume.

// Test-framework and provider namespaces a consumer's test project imports; its IDE adds these usings.
global using Microsoft.Extensions.Time.Testing;
global using Npgsql;
global using StackExchange.Redis;
global using static TestingAmbient;
global using Xunit;
using Bogus;
using Headless.Testing.AspNetCore;

public partial class Program;

public sealed class Order
{
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class OrderService(ILogger logger)
{
    public Task<Order> CreateAsync(Order order, CancellationToken cancellationToken) => Task.FromResult(order);
}

public sealed class ExpirationService(TimeProvider timeProvider)
{
    public bool IsExpired() => timeProvider.GetUtcNow() > DateTimeOffset.MinValue;
}

public static class OrderFakerExtensions
{
    public static Faker<Order> OrderFaker(this Faker faker) => new();
}

public static class MyFeatureSetup
{
    public static IServiceCollection AddMyFeature(this IServiceCollection services) => services;
}

// The project-wrapper convenience the guide describes; HeadlessTestServer itself takes an explicit delta.
public static class TestServerTimeExtensions
{
    public static DateTimeOffset AdvanceTime<TProgram>(this HeadlessTestServer<TProgram> app)
        where TProgram : class => app.AdvanceTime(TimeSpan.FromHours(1));
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class TestingAmbient
{
    public static HeadlessTestServer<Program> App => null!;

    public static string tenantId => "tenant-1";

    public static Order result => null!;

    public static DateTimeOffset expected => default;
}
