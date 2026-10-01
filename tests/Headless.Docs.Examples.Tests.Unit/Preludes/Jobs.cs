// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Context for the examples in docs/llms/jobs.md: the namespaces of the Headless packages the guide documents, the
// options builder an example inside AddHeadlessJobs(options => ...) calls, the services an example calls, and
// placeholder application types the examples name but do not declare. An example that declares a type of the same
// name uses its own.

global using Headless.Caching;
global using Headless.Coordination;
global using Headless.Jobs;
global using Headless.Jobs.Base;
global using Headless.Jobs.DbContextFactory;
global using Headless.Jobs.Entities;
global using Headless.Jobs.Enums;
global using Headless.Jobs.Exceptions;
global using Headless.Jobs.Interfaces;
global using Headless.Jobs.Interfaces.Managers;
global using Headless.Jobs.Models;
global using Headless.MultiTenancy;
global using Headless.UnitOfWork;

namespace DocsPrelude
{
    public abstract partial class Ambient
    {
        protected JobsOptionsBuilder<TimeJobEntity, CronJobEntity> options = null!;
        protected IJobScheduler scheduler = null!;
        protected ITimeJobManager<TimeJobEntity> timeJobManager = null!;
        protected ICronJobManager<CronJobEntity> cronJobManager = null!;
        protected JobsRequestSerializationOptions requestSerialization = null!;
        protected IUnitOfWorkFactory factory = null!;
        protected AppDbContext db = null!;
        protected Order order = null!;
        protected string orderId = "";
        protected string conn = "";
    }

    public sealed class Order
    {
        public string Id { get; init; } = "";
    }

    public sealed record OrderRequest(string OrderId);

    public sealed record OrderReminderRequest(string OrderId);

    public sealed record OrderPlaced(string OrderId);

    public sealed record ProcessOrder(string OrderId);

    public sealed record ChargeCard(string OrderId);

    public sealed record SendReceipt(string OrderId);

    public sealed record RefundPayment(string OrderId);

    public sealed record InvoiceRequest(string InvoiceId);

    public sealed record CreateInvoice(string InvoiceId);

    public sealed class CleanupJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public interface IOrderService
    {
        ValueTask ProcessAsync(string orderId, CancellationToken cancellationToken);

        ValueTask ProcessAsync(OrderRequest request, CancellationToken cancellationToken);
    }

    public interface IReportService
    {
        ValueTask BuildDailyAsync(CancellationToken cancellationToken);

        ValueTask BuildAsync(string reportKind, CancellationToken cancellationToken);
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
}
