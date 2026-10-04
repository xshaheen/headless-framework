// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;

namespace Tests.Registration;

// Hand-written equivalents of generated JobsModule types. They stand in for separate module assemblies, so a test can
// put two modules that declare one identity or argument type into a single host.

public sealed record InvoiceArgs(string InvoiceId);

public sealed class BillingCloseDay : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class BillingSendInvoice : IJob<InvoiceArgs>
{
    public ValueTask ExecuteAsync(JobContext<InvoiceArgs> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class OrdersShip : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class RivalCloseDay : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public sealed class RivalSendInvoice : IJob<InvoiceArgs>
{
    public ValueTask ExecuteAsync(JobContext<InvoiceArgs> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

internal static class TestJobs
{
    public const string BillingCloseDay = "billing.close-day";
    public const string BillingSendInvoice = "billing.send-invoice";
    public const string OrdersShip = "orders.ship";
    public const string RivalSendInvoice = "rival.send-invoice";
    public const string BillingNightly = "billing.nightly";

    public static void Add(
        JobsCatalogBuilder catalog,
        string identity,
        Type jobType,
        Type? argsType = null,
        JobPriority priority = JobPriority.Normal,
        int maxConcurrency = 0,
        string cron = ""
    )
    {
        catalog.AddFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [identity] = new()
                {
                    CronExpression = cron,
                    Priority = priority,
                    Delegate = static (_, _, _) => Task.CompletedTask,
                    MaxConcurrency = maxConcurrency,
                    JobType = jobType,
                },
            }
        );
        catalog.AddDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [identity] = new(identity, argsType, cron, priority, maxConcurrency),
            }
        );

        if (argsType is not null)
        {
            catalog.AddRequestTypes(
                new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
                {
                    [identity] = (argsType.FullName!, argsType),
                }
            );
        }
    }
}

public sealed class BillingJobsModule : IJobsModule
{
    private BillingJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog)
    {
        TestJobs.Add(catalog, TestJobs.BillingCloseDay, typeof(BillingCloseDay), maxConcurrency: 1);
        TestJobs.Add(catalog, TestJobs.BillingSendInvoice, typeof(BillingSendInvoice), typeof(InvoiceArgs));
    }
}

public sealed class OrdersJobsModule : IJobsModule
{
    private OrdersJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
        TestJobs.Add(catalog, TestJobs.OrdersShip, typeof(OrdersShip));
}

/// <summary>A second module that declares the billing module's <c>billing.close-day</c> identity.</summary>
public sealed class RivalCloseDayJobsModule : IJobsModule
{
    private RivalCloseDayJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
        TestJobs.Add(catalog, TestJobs.BillingCloseDay, typeof(RivalCloseDay));
}

/// <summary>A second module whose job takes the billing module's <see cref="InvoiceArgs"/>.</summary>
public sealed class RivalInvoiceArgsJobsModule : IJobsModule
{
    private RivalInvoiceArgsJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
        TestJobs.Add(catalog, TestJobs.RivalSendInvoice, typeof(RivalSendInvoice), typeof(InvoiceArgs));
}

/// <summary>A module whose only job is a cron job, to observe seeding on a host that does not run it.</summary>
public sealed class BillingNightlyJobsModule : IJobsModule
{
    private BillingNightlyJobsModule() { }

    static void IJobsModule.Register(JobsCatalogBuilder catalog) =>
        TestJobs.Add(catalog, TestJobs.BillingNightly, typeof(BillingNightly), cron: "0 0 3 * * *");
}

public sealed class BillingNightly : IJob
{
    public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
