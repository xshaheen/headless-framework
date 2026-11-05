// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Renders the shell document for every non-asset, non-API path under the base path, so the sign-in page and every
/// deep link load the same shell: a fresh nonce, the import map, the entry script, the inlined page state, and the
/// Content Security Policy, never cached.
/// </summary>
internal sealed class ShellDocument(
    DashboardAssetCatalog assets,
    DashboardManifestProvider manifest,
    IOptions<HeadlessDashboardOptions> options
)
{
    /// <summary>The <c>id</c> of the inlined page-state block.</summary>
    public const string StateElementId = "headless-dashboard-state";

    // Read once: the build is embedded in the assembly and cannot change while the host runs.
    private readonly Lazy<ShellTemplate> _template = new(() => ShellTemplate.Load(assets.Shell));

    /// <summary>
    /// Returns the dashboard's absolute path for <paramref name="request"/>: its <c>PathBase</c>, which carries
    /// <c>UsePathBase</c> and a forwarded prefix, plus the base path.
    /// </summary>
    public static string GetPrefix(HttpRequest request, string basePath)
    {
        return request.PathBase.Add(new PathString(DashboardSpaHelper.NormalizeBasePath(basePath))).ToUriComponent();
    }

    /// <summary>Writes the document for <paramref name="context"/>'s request.</summary>
    /// <exception cref="InvalidOperationException">The package was built without a valid shell build.</exception>
    public async Task WriteAsync(HttpContext context)
    {
        var template = _template.Value;
        var settings = options.Value;
        var prefix = GetPrefix(context.Request, settings.BasePath);

        // Resolved per request, so a session reader that depends on scoped services works too.
        var snapshot = context.RequestServices.GetRequiredService<IDashboardSessionSnapshot>();
        var session = await snapshot.GetAsync(context, context.RequestAborted).ConfigureAwait(false);

        var state = BootstrapState.Create(prefix, manifest.Manifest, session, settings, assets);
        var nonce = ContentSecurityPolicy.CreateNonce();
        var html = template.Before + _RenderHead(template.Build, prefix, nonce, state) + template.After;
        var body = Encoding.UTF8.GetBytes(html);

        var response = context.Response;
        var headers = response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy.Build(nonce, settings.ContentSecurityPolicy);

        // The document carries the session's state and a single-use nonce, so no cache may keep it.
        headers.CacheControl = "no-store";
        headers.XContentTypeOptions = "nosniff";
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength = body.Length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }

    private static string _RenderHead(ShellBuildManifest build, string prefix, string nonce, BootstrapState state)
    {
        var encoder = HtmlEncoder.Default;
        var head = new StringBuilder(2048);

        // The UI library inserts a <style> element of its own and reads its nonce from this element. The nonce goes
        // in the attribute, which the browser hides from scripts and selectors, not in a content attribute anyone
        // could read.
        head.Append("<meta name=\"csp-nonce\" nonce=\"").Append(nonce).Append("\">\n");

        // Order matters: the import map must precede every module load, preloads included.
        head.Append("<script type=\"importmap\" nonce=\"")
            .Append(nonce)
            .Append("\">")
            .Append(ImportMap.Render(build.Imports, prefix))
            .Append("</script>\n");

        foreach (var preload in build.Preload)
        {
            head.Append("<link rel=\"modulepreload\" href=\"")
                .Append(encoder.Encode(prefix + "/" + preload))
                .Append("\" nonce=\"")
                .Append(nonce)
                .Append("\">\n");
        }

        foreach (var css in build.Css)
        {
            head.Append("<link rel=\"stylesheet\" href=\"").Append(encoder.Encode(prefix + "/" + css)).Append("\">\n");
        }

        // A data block runs no code, so it needs no nonce; its JSON is escaped so a value cannot end the element.
        head.Append("<script type=\"application/json\" id=\"")
            .Append(StateElementId)
            .Append("\">")
            .Append(JsonSerializer.Serialize(state, ShellJson.BootstrapStateInfo))
            .Append("</script>\n");

        head.Append("<script type=\"module\" src=\"")
            .Append(encoder.Encode(prefix + "/" + build.Entry))
            .Append("\" nonce=\"")
            .Append(nonce)
            .Append("\"></script>");

        return head.ToString();
    }
}

/// <summary>The shell build's document template, split at its placeholder, and its <c>shell.json</c>.</summary>
/// <remarks>
/// <para>The front-end build writes two files at the root of <c>wwwroot/dist</c> that the server reads:</para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>index.html</c>: the document, holding exactly one <c>&lt;!--headless-dashboard:head--&gt;</c> placeholder in its
/// <c>&lt;head&gt;</c> and no <c>&lt;script&gt;</c> or <c>&lt;base&gt;</c> element. The server replaces the placeholder
/// with the <c>csp-nonce</c> meta element, the nonced import map, a nonced <c>modulepreload</c> link per preload, a stylesheet link per stylesheet, the
/// <c>application/json</c> page-state block, and the nonced entry module script, in that order.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>shell.json</c>, shaped like
/// <code>
/// { "entry": "assets/…", "css": ["assets/…"], "preload": ["assets/…"], "imports": { "react": "assets/…" } }
/// </code>
/// Every path is relative to <c>dist</c> and lies under <c>assets/</c>, which the shell serves at
/// <c>{base}/assets/</c>; <c>imports</c> maps each bare specifier of the import map to its file.
/// </description>
/// </item>
/// </list>
/// <para>
/// A script in the template would carry no nonce and be refused by the policy, and a <c>&lt;base&gt;</c> element is
/// inert under <c>base-uri 'none'</c>, so a template with either is refused rather than served broken.
/// </para>
/// </remarks>
internal sealed class ShellTemplate
{
    /// <summary>The marker the server replaces with the document's head content.</summary>
    public const string Placeholder = "<!--headless-dashboard:head-->";

    public const string DocumentFileName = "index.html";

    public const string BuildFileName = "shell.json";

    /// <summary>The folder every served build file lies under, relative to the build output.</summary>
    public const string AssetsFolder = "assets/";

    private ShellTemplate(string before, string after, ShellBuildManifest build)
    {
        Before = before;
        After = after;
        Build = build;
    }

    /// <summary>The template up to the placeholder.</summary>
    public string Before { get; }

    /// <summary>The template after the placeholder.</summary>
    public string After { get; }

    public ShellBuildManifest Build { get; }

    /// <summary>Reads and checks the build in <paramref name="assets"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The build is missing, or its template or <c>shell.json</c> breaks the contract in the remarks.
    /// </exception>
    public static ShellTemplate Load(EmbeddedAssetSet assets)
    {
        var html = _ReadText(assets, DocumentFileName);
        var at = html.IndexOf(Placeholder, StringComparison.Ordinal);

        if (at < 0 || html.IndexOf(Placeholder, at + Placeholder.Length, StringComparison.Ordinal) >= 0)
        {
            throw _Invalid($"{DocumentFileName} must hold exactly one {Placeholder} placeholder.");
        }

        if (html.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            throw _Invalid(
                $"{DocumentFileName} must hold no <script> element; the server emits every script with its nonce."
            );
        }

        if (_HasBaseElement(html))
        {
            throw _Invalid(
                $"{DocumentFileName} must hold no <base> element; base-uri 'none' ignores it, so every URL is absolute."
            );
        }

        ShellBuildManifest build;

        try
        {
            build =
                JsonSerializer.Deserialize(_ReadText(assets, BuildFileName), ShellJson.ShellBuildInfo)
                ?? throw _Invalid($"{BuildFileName} is empty.");
        }
        catch (JsonException e)
        {
            throw _Invalid($"{BuildFileName} is not valid: {e.Message}", e);
        }

        if (string.IsNullOrEmpty(build.Entry))
        {
            throw _Invalid($"{BuildFileName} names no entry.");
        }

        foreach (var path in build.Css.Concat(build.Preload).Concat(build.Imports.Values).Append(build.Entry))
        {
            if (!path.StartsWith(AssetsFolder, StringComparison.Ordinal) || assets.Find(path) is null)
            {
                throw _Invalid($"{BuildFileName} names '{path}', which is not a file under {AssetsFolder}.");
            }
        }

        return new ShellTemplate(html[..at], html[(at + Placeholder.Length)..], build);
    }

    private static string _ReadText(EmbeddedAssetSet assets, string fileName)
    {
        var file = assets.Files.GetFileInfo(fileName);

        if (!file.Exists || file.IsDirectory)
        {
            throw _Invalid(
                $"{fileName} is missing. The package was built without the shell's front-end build in wwwroot/dist."
            );
        }

        using var stream = file.CreateReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    private static bool _HasBaseElement(string html)
    {
        const string tag = "<base";

        for (var at = html.IndexOf(tag, StringComparison.OrdinalIgnoreCase); at >= 0; )
        {
            var next = at + tag.Length;

            // <base followed by a letter is another element, such as <basefont>.
            if (next == html.Length || !char.IsAsciiLetterOrDigit(html[next]))
            {
                return true;
            }

            at = html.IndexOf(tag, next, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static InvalidOperationException _Invalid(string message, Exception? inner = null)
    {
        return new InvalidOperationException("The dashboard shell's build is invalid: " + message, inner);
    }
}

/// <summary>
/// <c>shell.json</c>: the shell build's entry, stylesheets, module preloads, and import-map files. The collections have
/// setters, not <see langword="init"/>, so a list missing from the file keeps its empty default.
/// </summary>
internal sealed record ShellBuildManifest
{
    public string? Entry { get; init; }

    public IReadOnlyList<string> Css { get; set; } = [];

    public IReadOnlyList<string> Preload { get; set; } = [];

    public IReadOnlyDictionary<string, string> Imports { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
