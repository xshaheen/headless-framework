// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application types and variables the multi-tenancy guide's examples assume.

// ASP.NET Core and BCL namespaces the examples use; a consumer's IDE adds these usings.
global using System.Text.RegularExpressions;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Mvc;
global using static MultiTenancyAmbient;
using Headless.Api;
using Headless.EntityFramework;
using Headless.Jobs.Interfaces;
using Headless.Messaging;
using Headless.MultiTenancy;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(HeadlessDbContextServices services, DbContextOptions<AppDbContext> options)
    : HeadlessDbContext(services, options)
{
    public override string? DefaultSchema => null;
}

public sealed class Order
{
    public Guid Id { get; init; }

    public string? TenantId { get; init; }
}

public sealed record CreateOrderRequest(decimal Total);

public sealed record OrderPlaced(Guid OrderId);

public sealed record ReportRequest(string ReportKind);

public interface IReportService
{
    Task BuildAsync(string reportKind, CancellationToken cancellationToken);
}

public sealed class OrderPlacedHandler
{
    public Task HandleAsync(OrderPlaced message, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class ReportProcessor
{
    public Task RunAsync() => Task.CompletedTask;
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class MultiTenancyAmbient
{
    public static string connectionString => "";

    public static string tenantId => "";

    public static AppDbContext dbContext => null!;

    public static ICurrentTenant currentTenant => null!;

    public static HeadlessTenancyBuilder tenancy => null!;

    public static HeadlessTenantCatalogResolutionBuilder sources => null!;

    public static IJobScheduler scheduler => null!;

    public static ReportRequest request => null!;

    public static ReportProcessor processor => null!;

    public static IBus publisher => null!;

    public static ConsumeContext<OrderPlaced> context => null!;

    public static OrderPlaced message => null!;

    public static OrderPlacedHandler handler => null!;
}
