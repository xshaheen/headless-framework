// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application requests, services, job functions, and variables the jobs guide's examples assume.

global using static JobsAmbient;
using Headless.Jobs;
using Headless.Jobs.Base;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Interfaces.Managers;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed record Order(string Id);

public sealed record OrderPlaced(string OrderId);

public sealed record OrderRequest(string OrderId);

public sealed record OrderReminder(string OrderId);

public sealed record OrderReminderRequest(string OrderId);

public sealed record ProcessOrder(string OrderId);

public sealed record ChargeCard(string OrderId);

public sealed record SendReceipt(string OrderId);

public sealed record RefundPayment(string OrderId);

public sealed record InvoiceRequest(string InvoiceId);

public interface IOrderService
{
    Task ProcessAsync(OrderRequest request, CancellationToken cancellationToken);
}

public interface IReportService
{
    Task BuildAsync(string reportKind, CancellationToken cancellationToken);
}

public interface IAppTenantDirectory
{
    Task<IReadOnlyList<string>> ListActiveTenantIdsAsync(CancellationToken cancellationToken);
}

public sealed class MyJobExceptionHandler : IJobExceptionHandler
{
    public Task HandleExceptionAsync(
        Exception exception,
        Guid jobId,
        JobType jobType,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task HandleCanceledExceptionAsync(
        Exception exception,
        Guid jobId,
        JobType jobType,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;
}

public sealed class AuditScheduleMiddleware : IJobScheduleMiddleware
{
    public Task InvokeAsync(
        JobScheduleContext context,
        JobScheduleNext next,
        CancellationToken cancellationToken = default
    ) => next(cancellationToken);
}

public sealed class ExternalInvoiceMiddleware : IJobExecuteMiddleware
{
    public Task InvokeAsync(
        JobExecuteContext context,
        JobExecuteNext next,
        CancellationToken cancellationToken = default
    ) => next(cancellationToken);
}

public sealed class InvoiceExecutionMiddleware : IJobExecuteMiddleware
{
    public Task InvokeAsync(
        JobExecuteContext context,
        JobExecuteNext next,
        CancellationToken cancellationToken = default
    ) => next(cancellationToken);
}

// The requestless application job whose generated AppJobs.Cleanup handle the scheduling examples enqueue.
public sealed class CleanupJob
{
    [JobFunction("Cleanup")]
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// Top-level examples already get a Program; this gives the declaration-only examples one for ILogger<Program>.
public partial class Program;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class JobsAmbient
{
    public static string conn => null!;

    public static string connectionString => null!;

    public static string orderId => null!;

    public static bool isPermamentFailure => false;

    public static Order order => null!;

    public static AppDbContext db => null!;

    public static IUnitOfWorkFactory factory => null!;

    public static TimeProvider timeProvider => TimeProvider.System;

    public static IJobScheduler scheduler => null!;

    public static ITimeJobManager<TimeJobEntity> timeJobManager => null!;

    public static ICronJobManager<CronJobEntity> cronJobManager => null!;

    public static JobsRequestSerializationOptions requestSerialization => null!;

    public static bool IsConfigurationValid() => true;

    public static Task ProcessAsync(OrderRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public static Task ProcessSinceAsync(DateTime since, CancellationToken cancellationToken) => Task.CompletedTask;
}
