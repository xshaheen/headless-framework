// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging.Dashboard;

public static partial class MessagingDashboardEndpoints
{
    /// <summary>
    /// Resolves which of the given storage ids are pending scheduled deliveries under KTD8's query, using
    /// the host principal (not the operator actor requirement) so legacy fencing works under every auth mode.
    /// A provider without scheduled-delivery operations support (<see cref="NotSupportedException"/>) has no
    /// pending-scheduled rows to fence.
    /// </summary>
    private static async Task<HashSet<Guid>> _GetPendingScheduledIdsAsync(
        IDataStorage dataStorage,
        Guid[] storageIds,
        CancellationToken cancellationToken
    )
    {
        if (storageIds.Length == 0)
        {
            return [];
        }

        var pending = new HashSet<Guid>();
        try
        {
            var scheduledApi = dataStorage.GetScheduledDeliveryOperationsApi();
            var page = 0;
            while (true)
            {
                var result = await scheduledApi
                    .QueryAsync(
                        new ScheduledDeliveryQuery
                        {
                            StorageIds = storageIds,
                            PageSize = _MaxPageSize,
                            CurrentPage = page,
                        },
                        DashboardOperatorAuthority.HostAuthorizationContext,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

                foreach (var item in result.Items)
                {
                    pending.Add(item.StorageId);
                }

                if (!result.HasNext)
                {
                    break;
                }

                page++;
            }
        }
        catch (NotSupportedException)
        {
            // Provider does not implement scheduled-delivery operations; nothing to fence.
        }

        return pending;
    }

    private static async Task<IResult> _ScheduledList(
        HttpContext httpContext,
        IServiceProvider sp,
        string? name = null,
        MessageLane? lane = null,
        DateTimeOffset? dueFrom = null,
        DateTimeOffset? dueTo = null,
        int perPage = 20,
        int currentPage = 1
    )
    {
        if (!_TryResolveOperatorAuthority(httpContext, out var authorization, out var authorityFailure))
        {
            return authorityFailure!;
        }

        var result = await sp.GetRequiredService<IDataStorage>()
            .GetScheduledDeliveryOperationsApi()
            .QueryAsync(
                new ScheduledDeliveryQuery
                {
                    MessageName = name,
                    Lane = lane,
                    DueFrom = dueFrom,
                    DueTo = dueTo,
                    CurrentPage = Math.Max(currentPage - 1, 0),
                    PageSize = Math.Clamp(perPage, 1, _MaxPageSize),
                },
                authorization,
                httpContext.RequestAborted
            )
            .ConfigureAwait(false);
        return Results.Json(result, _InboxJsonOptions);
    }

    private static async Task<IResult> _ScheduledRevoke(HttpContext httpContext, IServiceProvider sp) =>
        await _ExecuteScheduledDashboardOperationAsync(
                httpContext,
                sp,
                static (api, request, token) => api.RevokeAsync(request, token)
            )
            .ConfigureAwait(false);

    private static async Task<IResult> _ScheduledDispatchNow(HttpContext httpContext, IServiceProvider sp) =>
        await _ExecuteScheduledDashboardOperationAsync(
                httpContext,
                sp,
                static (api, request, token) => api.DispatchNowAsync(request, token)
            )
            .ConfigureAwait(false);

    private static async Task<IResult> _ExecuteScheduledDashboardOperationAsync(
        HttpContext httpContext,
        IServiceProvider sp,
        Func<
            IScheduledDeliveryOperationsApi,
            ScheduledDeliveryOperationRequest,
            CancellationToken,
            ValueTask<ScheduledDeliveryOperationResult>
        > execute
    )
    {
        if (!_TryResolveOperatorAuthority(httpContext, out var authorization, out var authorityFailure))
        {
            return authorityFailure!;
        }
        if (!httpContext.Request.HasJsonContentType())
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ScheduledDashboardOperationRequest? payload;
        try
        {
            payload = await httpContext
                .Request.ReadFromJsonAsync<ScheduledDashboardOperationRequest>(
                    _InboxJsonOptions,
                    httpContext.RequestAborted
                )
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Results.UnprocessableEntity();
        }

        if (payload is null)
        {
            return Results.UnprocessableEntity();
        }

        var request = new ScheduledDeliveryOperationRequest(
            payload.OperationId,
            payload.StorageId,
            payload.ExpectedDueAt,
            payload.Reason ?? string.Empty,
            authorization
        );
        try
        {
            var result = await execute(
                    sp.GetRequiredService<IDataStorage>().GetScheduledDeliveryOperationsApi(),
                    request,
                    httpContext.RequestAborted
                )
                .ConfigureAwait(false);
            var statusCode = result.Outcome switch
            {
                InboxOperationOutcome.Applied => StatusCodes.Status200OK,
                InboxOperationOutcome.NotFound => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status409Conflict,
            };
            return Results.Json(result, _InboxJsonOptions, statusCode: statusCode);
        }
        catch (InvalidOperationException)
        {
            return Results.UnprocessableEntity();
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Unauthorized();
        }
    }

    private sealed class ScheduledDashboardOperationRequest
    {
        public Guid OperationId { get; init; }
        public Guid StorageId { get; init; }
        public DateTimeOffset ExpectedDueAt { get; init; }
        public string? Reason { get; init; }
    }
}
