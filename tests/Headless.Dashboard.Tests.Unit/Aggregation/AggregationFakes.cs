// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Headless.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Tests.Aggregation;

/// <summary>
/// A module whose every aggregation capability answers through a delegate the test sets, with a manifest built in
/// memory from its id and SDK version.
/// </summary>
internal abstract class FakeModule(string id)
    : IDashboardModule,
        IDashboardAttentionSource,
        IDashboardOverviewSource,
        IDashboardCountSource,
        IDashboardObjectResolver,
        IDashboardRelatedSource,
        IDashboardPayloadSource,
        IDashboardPayloadFormatter,
        IDashboardCommandSource,
        IDashboardOperationRunner
{
    public string Id { get; } = id;

    public string DisplayName => $"Module {Id}";

    /// <summary>The SDK version the module claims it was built against; a major other than 1 makes the shell refuse it.</summary>
    public string SdkVersion { get; set; } = "1.0.0";

    /// <summary>The product the module joins; both fakes join one product unless a test moves one.</summary>
    public string Product { get; set; } = "platform";

    public DashboardModuleAssets Assets =>
        new()
        {
            Files = new SingleFileProvider(
                "manifest.json",
                $$"""{"id":"{{Id}}","sdkVersion":"{{SdkVersion}}","entry":"entry.js","product":"{{Product}}"}"""
            ),
        };

    public IReadOnlyList<string> GroupIds { get; set; } = [];

    public IReadOnlyList<string> ObjectKinds { get; set; } = [];

    public Func<CancellationToken, Task<IReadOnlyDictionary<string, DashboardCount>>> Counts { get; set; } =
        static _ =>
            Task.FromResult<IReadOnlyDictionary<string, DashboardCount>>(
                new Dictionary<string, DashboardCount>(StringComparer.Ordinal)
            );

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    ) => Counts(cancellationToken);

    public Func<CancellationToken, Task<IReadOnlyList<DashboardAttentionItem>>> Attention { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<DashboardAttentionItem>>([]);

    public Func<CancellationToken, Task<IReadOnlyList<DashboardFigure>>> Overview { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<DashboardFigure>>([]);

    public Func<CancellationToken, Task<IReadOnlyList<DashboardScheduleTrack>>> Schedule { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<DashboardScheduleTrack>>([]);

    public Func<string, Task<IReadOnlyList<DashboardObject>>> Resolve { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<DashboardObject>>([]);

    public Func<ObjectRef, Task<IReadOnlyList<DashboardRelatedGroup>>> Related { get; set; } =
        static _ => Task.FromResult<IReadOnlyList<DashboardRelatedGroup>>([]);

    public Func<DashboardPayload, DashboardPayload> Format { get; set; } = static payload => payload;

    public Func<ObjectRef, CancellationToken, Task<DashboardPayload?>> Payload { get; set; } =
        static (_, _) => Task.FromResult<DashboardPayload?>(null);

    public IReadOnlyList<DashboardCommand> Commands { get; set; } = [];

    public void MapEndpoints(DashboardModuleEndpoints endpoints) { }

    public Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlyList<DashboardCommandAvailability>>([]);

    public Task<DashboardCommandResult> ExecuteAsync(
        DashboardCommandRequest request,
        CancellationToken cancellationToken
    ) => Task.FromResult(new DashboardCommandResult { Rows = [] });

    public Task<DashboardOperationScope> PrepareAsync(
        DashboardOperationScopeRequest request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new DashboardOperationScope
            {
                Count = 0,
                Sample = [],
                HighWaterMark = "0",
            }
        );

    public Task<DashboardOperationChunk> RunChunkAsync(
        DashboardOperationChunkRequest request,
        CancellationToken cancellationToken
    ) => Task.FromResult(new DashboardOperationChunk { Rows = [], NextCursor = null });

    public Task<IReadOnlyList<DashboardAttentionItem>> GetAttentionAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    ) => Attention(cancellationToken);

    public Task<IReadOnlyList<DashboardFigure>> GetOverviewAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    ) => Overview(cancellationToken);

    public Task<IReadOnlyList<DashboardScheduleTrack>> GetScheduleAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    ) => Schedule(cancellationToken);

    public Task<IReadOnlyList<DashboardObject>> ResolveAsync(
        DashboardResolveRequest request,
        CancellationToken cancellationToken
    ) => Resolve(request.Query);

    public Task<IReadOnlyList<DashboardRelatedGroup>> GetRelatedAsync(
        DashboardRelatedRequest request,
        CancellationToken cancellationToken
    ) => Related(request.Target);

    public Task<DashboardPayload?> GetPayloadAsync(
        DashboardPayloadRequest request,
        CancellationToken cancellationToken
    ) => Payload(request.Target, cancellationToken);

    public ValueTask<DashboardPayload> FormatAsync(
        DashboardPayloadFormatRequest request,
        CancellationToken cancellationToken
    ) => ValueTask.FromResult(Format(request.Payload));
}

internal sealed class DiagAModule() : FakeModule("diag-a");

/// <summary>
/// The second fake module. It answers attention and Overview through the members that name the sources that did not
/// answer, as a module reading several stores does; the first module keeps the defaults, as a single-store module does.
/// </summary>
internal sealed class DiagBModule() : FakeModule("diag-b"), IDashboardAttentionSource, IDashboardOverviewSource
{
    /// <summary>Gets or sets the sources the attention and Overview answers report as not answering.</summary>
    public IReadOnlyList<string> UnavailableSources { get; set; } = [];

    public async Task<DashboardAttentionAnswer> GetAttentionAnswerAsync(
        DashboardAttentionRequest request,
        CancellationToken cancellationToken
    )
    {
        return new DashboardAttentionAnswer
        {
            Items = await GetAttentionAsync(request, cancellationToken),
            UnavailableSources = UnavailableSources,
        };
    }

    public async Task<DashboardOverviewAnswer> GetOverviewAnswerAsync(
        DashboardOverviewRequest request,
        CancellationToken cancellationToken
    )
    {
        return new DashboardOverviewAnswer
        {
            Figures = await GetOverviewAsync(request, cancellationToken),
            Tracks = await GetScheduleAsync(request, cancellationToken),
            UnavailableSources = UnavailableSources,
        };
    }
}

internal sealed class DiagAModuleOptions : DashboardModuleOptions;

internal sealed class DiagBModuleOptions : DashboardModuleOptions;

/// <summary>A file provider holding one in-memory file, standing in for a module's embedded build output.</summary>
internal sealed class SingleFileProvider(string name, string content) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath)
    {
        return string.Equals(subpath.TrimStart('/'), name, StringComparison.Ordinal)
            ? new MemoryFile(name, Encoding.UTF8.GetBytes(content))
            : new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private sealed class MemoryFile(string name, byte[] bytes) : IFileInfo
    {
        public bool Exists => true;

        public long Length => bytes.Length;

        public string? PhysicalPath => null;

        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
    }
}

/// <summary>
/// Signs the request in as the user named in <c>X-Test-User</c>, with the roles in <c>X-Test-Roles</c>; without the
/// header the request stays anonymous.
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = Request.Headers["X-Test-User"].ToString();

        if (name.Length == 0)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        claims.AddRange(
            Request
                .Headers["X-Test-Roles"]
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static role => new Claim(ClaimTypes.Role, role))
        );

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
