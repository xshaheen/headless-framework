// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application models, services, and variables the utilities guide's examples assume.

global using static UtilitiesAmbient;
using FluentValidation;
using FluentValidation.Results;
using Headless.Redis;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using StackExchange.Redis;

public sealed class User
{
    public string PhoneNumber { get; set; } = "";

    public string Email { get; set; } = "";

    public IEnumerable<string> Roles { get; set; } = [];

    public string ApiEndpoint { get; set; } = "";
}

public sealed class Order;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed class MyOptions;

public sealed class MyOptionsValidator : AbstractValidator<MyOptions>;

public sealed class AppOptions;

public sealed class AppOptionsValidator : AbstractValidator<AppOptions>;

public interface IMyService;

public sealed class BetterMyService : IMyService;

public interface IService;

public interface IDep;

public sealed class Impl(IDep dep) : IService
{
    public IDep Dep { get; } = dep;
}

public sealed class NewImpl : IService;

public sealed class NullFallback : IService;

public sealed class DefaultImpl : IService;

public sealed class LoggingService(IService inner) : IService
{
    public IService Inner { get; } = inner;
}

public sealed class IncrementWithExpireScript : RedisScriptDefinition
{
    public static IncrementWithExpireScript Instance { get; } = new();

    private IncrementWithExpireScript()
        : base("return redis.call('incrby', @key, @value)") { }
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class UtilitiesAmbient
{
    public static ValidationResult result => null!;

    public static bool condition => false;

    public static Func<IServiceCollection, IServiceCollection> ifAction => null!;

    public static Func<IServiceCollection, IServiceCollection> elseAction => null!;

    public static Polygon polygon => null!;

    public static Geometry geom1 => null!;

    public static Geometry geom2 => null!;

    public static IConnectionMultiplexer redis => null!;

    public static HeadlessRedisScriptsLoader scriptsLoader => null!;
}
