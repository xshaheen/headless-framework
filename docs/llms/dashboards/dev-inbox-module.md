---
domain: Dashboards
packages: DevInbox.Dashboard
---

# Dev inbox module

> Part of [Dashboards](../dashboards.md). The Dev inbox module from `Headless.DevInbox.Dashboard`: the emails, SMS, and push notifications a Development host captured instead of sending, with previews, downloads, and Delete and Clear.

## Headless.DevInbox.Dashboard

The module reads the in-memory capture stores that `UseDevelopment()` registers on emails, SMS, and push notifications, and shows one channel per store the host registered. It runs only in the Development environment, because captured messages hold reset links and one-time codes.

### Register the module

```bash
dotnet add package Headless.DevInbox.Dashboard
```

Register the development providers and the module only when the host runs in Development. Call `AddDevInbox()` on the dashboard builder.

```csharp
using Headless.Dashboard;
using Headless.Emails;
using Headless.PushNotifications;
using Headless.Sms;

if (builder.Environment.IsDevelopment())
{
    // A positive limit keeps attachment bytes, so inline images show and attachments download.
    builder.Services.AddHeadlessEmails(setup => setup.UseDevelopment(options => options.MaxCapturedAttachmentBytes = 256 * 1024));
    builder.Services.AddHeadlessSms(setup => setup.UseDevelopment());
    builder.Services.AddHeadlessPushNotifications(setup => setup.UseDevelopment());
}

if (builder.Environment.IsDevelopment())
{
    // This dashboard exists only for local development, so it runs without sign-in and only in Development.
    builder.Services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddDevInbox());
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapHeadlessDashboard();
}
```

When the host already serves a shared dashboard with its own authentication, keep that registration as it is and put only `dashboard.AddDevInbox()` inside the Development condition. Never call `WithNoAuth()` outside Development.

**Verify:** `GET {base}/api/dev-inbox/channels` (by default `/headless/api/dev-inbox/channels`) answers `200` and lists `email`, `sms`, and `push` with each store's `capacity`, and the shell shows a Dev inbox tab with the `DEV` badge. Started as Production, the host serves no `{base}` route. To see the startup refusal, register `AddDevInbox()` in a host whose environment is not Development: the start fails with `InvalidOperationException`.

- `AddDevInbox()`, `AddDevInbox(Action<DevInboxDashboardOptions>)`, `AddDevInbox(Action<DevInboxDashboardOptions, IServiceProvider>)`, and `AddDevInbox(IConfiguration)` register the module. `DevInboxDashboardOptions` carries the `ViewPolicy` and `OperatePolicy` every module has. A white-space policy fails the start with `OptionsValidationException`.
- Register any combination of the three stores. A channel whose store is absent shows `–` as its sidebar count and an empty state naming the `UseDevelopment()` call it needs.
- Set `DevEmailOptions.MaxCapturedAttachmentBytes` above 0. The default of 0 keeps no attachment bytes, so no inline image shows and no attachment downloads.

### Startup failures

| Host | Start fails with `InvalidOperationException` naming |
| --- | --- |
| Environment is not Development (`Testing`, `Staging`, `Production`) | `AddDevInbox`, Development, and the environment name |
| No `IHostEnvironment` registered | `AddDevInbox`, Development, and the missing `IHostEnvironment` |
| No capture store registered | `UseDevelopment()` on `AddHeadlessEmails`, `AddHeadlessSms`, or `AddHeadlessPushNotifications` |

No option lets the module run in another environment. A test host that adds it sets its environment to Development.

### Object kinds and topics

The object kinds are `email`, `sms`, and `push`, each opened at `emails?open=<id>`, `sms?open=<id>`, or `push?open=<id>`. A capture's id is its GUID, and Go to id resolves it. Stream topics are `dev-inbox.email`, `dev-inbox.sms`, and `dev-inbox.push`. A new capture streams once with its list row as the projection and its store sequence as the version. A delete streams the id with no projection, so open lists drop the row and an open panel shows the capture as gone. The stores live in one process, so the module serves one node.

### API

Routes sit under `{base}/api/dev-inbox`, behind the module's view policy. Every route answers with `Cache-Control: no-store`.

| Route | Answers |
| --- | --- |
| `GET channels` | `{ channels: [{ kind, capacity }] }` for each registered store |
| `GET emails`, `GET sms`, `GET push` | `{ items, nextCursor }`, newest first. Query `search` (the store's case-insensitive match), `limit` (1 to 200, default 50), and `before` (a `nextCursor`). A `limit` outside the range answers a `400` validation problem naming `limit`. |
| `GET emails/{id}`, `GET sms/{id}`, `GET push/{id}` | The capture's detail, or `404` for an unknown or malformed id |
| `GET emails/{id}/html` | The HTML body through `DashboardResults.HtmlPreview`, or `404` when the email has none |
| `GET emails/{id}/eml` | The whole message as `message/rfc822`, as an attachment named `<id>.eml` |
| `GET emails/{id}/attachments/{index}` | The attachment at that position, or `404` when its content was omitted or the index is out of range |

- A channel whose store is absent answers `404` with a problem whose `code` is `dev_inbox:channel_unconfigured`.
- A list row carries no body, HTML, payload, or attachment bytes. The detail lists each attachment's `index`, `fileName`, `contentType`, `sizeBytes`, and `isOmitted`.
- An SMS detail's `segmentation` gives `encoding` (`gsm7` or `ucs2`), `length` in that encoding's units, and `parts`. GSM-7 counts septets, and an extension character such as `€` takes two. UCS-2 counts UTF-16 code units. The view labels them `septets` and `UTF-16 code units`, never characters.
- A push detail gives `payloadSizeBytes`, `exceedsPayloadLimit` (over 4,096 bytes, which Firebase Cloud Messaging and APNs would likely reject), and only the delivery options the app set.
- Downloads send `Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`, and `Cache-Control: no-store`. An attachment captured as an HTML, XML, SVG, or script type downloads as `application/octet-stream`.

### HTML preview and inline images

The preview inlines each `cid:` image the HTML references, in an `src` attribute or a CSS `url()`, as a `data:` URI of a kept attachment. Captures carry no Content-ID, so a reference matches the attachment whose file name equals it, or equals the part before an `@`, ignoring case: `cid:logo.png` and `cid:logo.png@example.com` both match `logo.png`.

- Only kept raster images inline. An SVG, an omitted attachment, or an unmatched reference stays as `cid:` and shows as a missing image.
- One preview generates at most 4 MiB of `data:` URIs. References past that budget stay unresolved, so an email that repeats a large image many times cannot exhaust the host's memory.
- The preview can show an image a real mail client would show only as a file, because matching is by file name.

### Commands

| Command | On | Effect |
| --- | --- | --- |
| `dev-inbox.email.delete`, `dev-inbox.sms.delete`, `dev-inbox.push.delete` | one capture | Removes it. Confirmed, no inverse, optional reason. A capture already gone reports `skipped`. |
| `dev-inbox.email.clear`, `dev-inbox.sms.clear`, `dev-inbox.push.clear` | the channel | Runs as an Operation over `{ "search": null }` (the whole channel) or `{ "search": "<text>" }` (the captures the list search matches), with a required reason. Any other filter property, a repeated `search`, or a `search` that is not text or null is refused before anything is counted. |

- Both need operate permission and write the operator log. The module offers only the commands of the registered channels.
- Clear removes only the captures that existed when the operator confirmed it. A capture that arrives during the clear stays.
- A Clear confirmed before the host restarted is refused with `400` when its chunks run, because the stores start again with the process. Confirm it again.
- Delete is the only write: the module maps no `DELETE` route.

### Test the module

Start a host with its environment set to Development and the providers registered, then read the module's routes. The test host needs `Microsoft.AspNetCore.TestHost`.

```csharp no-compile
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.WebHost.UseTestServer();
builder.Services.AddHeadlessEmails(setup => setup.UseDevelopment());
builder.Services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddDevInbox());

await using var app = builder.Build();
app.MapHeadlessDashboard();
await app.StartAsync();

using var client = app.GetTestClient();
using var channels = await client.GetAsync("/headless/api/dev-inbox/channels");
```

**Verify:** `channels` answers `200` with one `email` channel; started with `EnvironmentName = "Testing"`, `StartAsync` throws `InvalidOperationException`.
