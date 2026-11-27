// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard.Internal;
using Headless.Emails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Headless.DevInbox.Dashboard;

/// <summary>
/// An email's HTML preview and its downloads: the whole message as <c>.eml</c>, and each kept attachment. Every
/// download is an attachment that no cache keeps and no browser sniffs, so captured bytes never run in the dashboard's
/// origin.
/// </summary>
internal sealed class EmailContentEndpoints(EmailChannel? channel)
{
    private readonly EmailChannel? _channel = channel;

    public void Map(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        var routes = endpoints.Routes;

        routes.MapGet("/emails/{id}/html", _Html);
        routes.MapGet("/emails/{id}/eml", _Eml);
        routes.MapGet("/emails/{id}/attachments/{index}", _Attachment);
    }

    private IResult _Html(string id)
    {
        if (_channel is null)
        {
            return DevInboxEndpoints.Unconfigured(DevInboxEndpoints.EmailSetup);
        }

        return _Find(id) is { HtmlBody: { Length: > 0 } html } email
            ? DashboardResults.HtmlPreview(CidImageInliner.Inline(html, email.Attachments))
            : TypedResults.NotFound();
    }

    private IResult _Eml(string id)
    {
        if (_channel is null)
        {
            return DevInboxEndpoints.Unconfigured(DevInboxEndpoints.EmailSetup);
        }

        return _Find(id) is { } email
            ? new DownloadResult(
                $"{email.Id:D}.eml",
                "message/rfc822",
                (stream, cancellationToken) => email.WriteEmlAsync(stream, cancellationToken)
            )
            : TypedResults.NotFound();
    }

    private IResult _Attachment(string id, string index)
    {
        if (_channel is null)
        {
            return DevInboxEndpoints.Unconfigured(DevInboxEndpoints.EmailSetup);
        }

        if (
            _Find(id) is not { } email
            || !int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var position)
            || position >= email.Attachments.Count
            || email.Attachments[position] is not { Content: { } content } attachment
        )
        {
            return TypedResults.NotFound();
        }

        return new DownloadResult(
            string.IsNullOrEmpty(attachment.FileName)
                ? "attachment-" + position.ToString(CultureInfo.InvariantCulture)
                : attachment.FileName,
            EmailAttachmentTypes.Served(EmailAttachmentTypes.Resolve(attachment)),
            (stream, cancellationToken) => stream.WriteAsync(content, cancellationToken).AsTask()
        );
    }

    private CapturedEmail? _Find(string id) =>
        DevInboxEndpoints.TryParseId(id, out var parsed) ? _channel!.Get(parsed) : null;

    private sealed class DownloadResult(
        string fileName,
        string contentType,
        Func<Stream, CancellationToken, Task> write
    ) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            var disposition = new ContentDispositionHeaderValue("attachment");
            // Encodes the name, so a line break in a captured file name cannot start another header.
            disposition.SetHttpFileName(fileName);
            response.Headers.ContentDisposition = disposition.ToString();
            response.Headers.XContentTypeOptions = "nosniff";
            // The bytes are captured content, such as reset links, that no cache may keep.
            response.Headers.CacheControl = "no-store";
            response.ContentType = contentType;

            await write(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
