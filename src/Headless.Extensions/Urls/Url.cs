// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;

namespace Headless.Urls;

/// <summary>
/// A mutable object for fluently building and parsing URLs.
/// </summary>
[PublicAPI]
public sealed class Url
{
    private readonly string? _originalString;
    private bool _parsed;

    private string _scheme = "";
    private string _userInfo = "";
    private string _host = "";
    private List<string> _pathSegments = [];
    private QueryParamCollection _queryParams = [];
    private string _fragment = "";
    private int? _port;
    private bool _leadingSlash;
    private bool _trailingSlash;
    private bool _trailingQmark;
    private bool _trailingHash;

    #region public properties
    /// <summary>
    /// The scheme of the URL, i.e. "http". Does not include ":" delimiter. Empty string if the URL is relative.
    /// </summary>
    public string Scheme
    {
        get => _EnsureParsed()._scheme;
        set => _EnsureParsed()._scheme = value;
    }

    /// <summary>
    /// i.e. "user:pass" in "https://user:pass@www.site.com". Empty string if not present.
    /// </summary>
    public string UserInfo
    {
        get => _EnsureParsed()._userInfo;
        set => _EnsureParsed()._userInfo = value;
    }

    /// <summary>
    /// i.e. "www.site.com" in "https://www.site.com:8080/path". Does not include user info or port.
    /// </summary>
    public string Host
    {
        get => _EnsureParsed()._host;
        set => _EnsureParsed()._host = value;
    }

    /// <summary>
    /// Port number of the URL. Null if not explicitly specified.
    /// </summary>
    public int? Port
    {
        get => _EnsureParsed()._port;
        set => _EnsureParsed()._port = value;
    }

    /// <summary>
    /// i.e. "www.site.com:8080" in "https://www.site.com:8080/path". Includes both user info and port, if included.
    /// </summary>
    public string Authority =>
        string.Concat(UserInfo, UserInfo.Length > 0 ? "@" : "", Host, Port.HasValue ? ":" : "", Port);

    /// <summary>
    /// i.e. "https://www.site.com:8080" in "https://www.site.com:8080/path" (everything before the path).
    /// </summary>
    public string Root =>
        string.Concat(Scheme, Scheme.Length > 0 ? ":" : "", Authority.Length > 0 ? "//" : "", Authority);

    /// <summary>
    /// i.e. "/path" in "https://www.site.com/path". Empty string if not present. Leading and trailing "/" retained exactly as specified by user.
    /// </summary>
    public string Path
    {
        get
        {
            _EnsureParsed();
            if (_pathSegments.Count == 0)
            {
                return _leadingSlash ? "/" : "";
            }

            var capacity = (_leadingSlash ? 1 : 0) + (_trailingSlash ? 1 : 0);
            foreach (var seg in _pathSegments)
            {
                capacity += seg.Length + 1;
            }

            var sb = new StringBuilder(capacity);
            if (_leadingSlash)
            {
                sb.Append('/');
            }

            for (var i = 0; i < _pathSegments.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('/');
                }

                sb.Append(_pathSegments[i]);
            }

            if (_trailingSlash)
            {
                sb.Append('/');
            }

            return sb.ToString();
        }
        set
        {
            _EnsureParsed();
            _pathSegments.Clear();
            _trailingSlash = false;
            if (string.IsNullOrEmpty(value))
            {
                _leadingSlash = false;
            }
            else if (string.Equals(value, "/", StringComparison.Ordinal))
            {
                _leadingSlash = true;
            }
            else
            {
                AppendPathSegment(value);
            }
        }
    }

    /// <summary>
    /// The "/"-delimited segments of the path, not including leading or trailing "/" characters.
    /// </summary>
    public IReadOnlyList<string> PathSegments => _EnsureParsed()._pathSegments;

    /// <summary>
    /// i.e. "x=1&amp;y=2" in "https://www.site.com/path?x=1&amp;y=2". Does not include "?".
    /// </summary>
    public string Query
    {
        get => QueryParams.ToString();
        set => _EnsureParsed()._queryParams = new QueryParamCollection(value);
    }

    /// <summary>
    /// Query parsed to name/value pairs.
    /// </summary>
    public QueryParamCollection QueryParams => _EnsureParsed()._queryParams;

    /// <summary>
    /// i.e. "frag" in "https://www.site.com/path?x=y#frag". Does not include "#".
    /// </summary>
    public string Fragment
    {
        get => _EnsureParsed()._fragment;
        set => _EnsureParsed()._fragment = value ?? "";
    }

    /// <summary>
    /// True if URL does not start with a non-empty scheme. i.e. false for "https://www.site.com", true for "//www.site.com".
    /// </summary>
    public bool IsRelative => string.IsNullOrEmpty(Scheme);

    /// <summary>
    /// True if Url is absolute and scheme is https or wss.
    /// </summary>
    public bool IsSecureScheme =>
        !IsRelative && (Scheme.OrdinalEquals("https", true) || Scheme.OrdinalEquals("wss", true));
    #endregion

    #region ctors and parsing methods
    /// <summary>
    /// Constructs a Url object from a string. Parsing is deferred until a component is first accessed, so a malformed
    /// <paramref name="baseUrl"/> surfaces a <see cref="UriFormatException"/> on first access of a parsed member
    /// (any property getter/setter or fluent builder method), not from this constructor.
    /// </summary>
    /// <param name="baseUrl">The URL to use as a starting point.</param>
    public Url(string? baseUrl = null)
    {
        _originalString = baseUrl?.Trim();
    }

    /// <summary>
    /// Constructs a Url object from a System.Uri.
    /// </summary>
    /// <param name="uri">The System.Uri (required)</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null" />.</exception>
    public Url(Uri uri)
    {
        Argument.IsNotNull(uri);
        _originalString = uri.OriginalString;
        _ParseInternal(uri); // parse eagerly, taking advantage of the fact that we already have a parsed Uri
    }

    /// <summary>
    /// Parses a URL string into a <see cref="Url"/> object, eagerly parsing it into its components.
    /// </summary>
    /// <param name="url">The URL string to parse.</param>
    /// <returns>A new <see cref="Url"/> object representing the parsed URL.</returns>
    /// <exception cref="UriFormatException">Thrown when <paramref name="url"/> is not a valid URI.</exception>
    public static Url Parse(string url) => new Url(url)._ParseInternal();

    private Url _EnsureParsed() => _parsed ? this : _ParseInternal();

    private Url _ParseInternal(Uri? uri = null)
    {
        _parsed = true;

        uri ??= new Uri(_originalString ?? "", UriKind.RelativeOrAbsolute);

        if (uri.OriginalString.OrdinalStartsWith("//"))
        {
            _ParseInternal(new Uri("http:" + uri.OriginalString));
            _scheme = "";
        }
        else if (uri.OriginalString.OrdinalStartsWith("/"))
        {
            _ParseInternal(new Uri("http://temp.com" + uri.OriginalString));
            _scheme = "";
            _host = "";
            _leadingSlash = true;
        }
        else if (uri.IsAbsoluteUri)
        {
            _scheme = uri.Scheme;
            _userInfo = uri.UserInfo;
            _host = uri.Host;

            // Root excludes the port here because _port is still null at this point.
            var rootWithoutPort = Root;
            _port =
                _originalString?.OrdinalStartsWith($"{rootWithoutPort}:{uri.Port}", ignoreCase: true) == true
                    ? uri.Port
                    : null; // don't default Port if not included explicitly
            _pathSegments = [];
            if (uri.AbsolutePath.Length > 0 && uri.AbsolutePath != "/")
            {
                AppendPathSegment(uri.AbsolutePath);
            }

            _queryParams = new QueryParamCollection(uri.Query);
            _fragment = uri.Fragment.TrimStart('#'); // quirk - formal def of fragment does not include the #

            // _port is final now; materialize Authority/Root once (mirrors the Root property) and reuse below
            // instead of recomputing these string.Concat results 4-5 times on the parse hot path.
            var authority = Authority;
            var root = string.Concat(
                _scheme,
                _scheme.Length > 0 ? ":" : "",
                authority.Length > 0 ? "//" : "",
                authority
            );

            _leadingSlash = uri.OriginalString.OrdinalStartsWith(root + "/", ignoreCase: true);
            _trailingSlash = _pathSegments.Count > 0 && uri.AbsolutePath.OrdinalEndsWith("/");
            _trailingQmark = string.Equals(uri.Query, "?", StringComparison.Ordinal);
            _trailingHash = string.Equals(uri.Fragment, "#", StringComparison.Ordinal);

            // more quirk fixes
            var hasAuthority = uri.OriginalString.OrdinalStartsWith(_scheme + "://", ignoreCase: true);
            if (hasAuthority && authority.Length == 0 && _pathSegments.Count != 0)
            {
                // Uri didn't parse Authority when it should have
                _host = _pathSegments[0];
                _pathSegments.RemoveAt(0);
            }
            else if (!hasAuthority && authority.Length > 0)
            {
                // Uri parsed Authority when it should not have
                _pathSegments.Insert(0, authority);
                _userInfo = "";
                _host = "";
                _port = null;
            }
        }
        // if it's relative, System.Uri refuses to parse any of it. these hacks will force the matter
        else
        {
            _ParseInternal(new Uri("http://temp.com/" + uri.OriginalString));
            _scheme = "";
            _host = "";
            _leadingSlash = false;
        }

        return this;
    }

    /// <summary>
    /// Parses a URL query into a <see cref="QueryParamCollection"/>.
    /// </summary>
    /// <param name="query">The URL query to parse. A <see langword="null"/> value yields an empty collection.</param>
    /// <returns>A <see cref="QueryParamCollection"/> containing the parsed name/value pairs.</returns>
    public static QueryParamCollection ParseQueryParams(string? query) => UrlParser.ParseQueryParams(query);

    /// <summary>
    /// Splits the given path into segments, encoding illegal characters, "?", and "#".
    /// </summary>
    /// <param name="path">The path to split.</param>
    /// <returns>The "/"-delimited segments of the encoded path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static IEnumerable<string> ParsePathSegments(string path) => UrlParser.ParsePathSegments(path);
    #endregion

    #region fluent builder methods
    /// <summary>
    /// Appends a segment to the URL path, ensuring there is one and only one '/' character as a separator.
    /// </summary>
    /// <param name="segment">The segment to append</param>
    /// <param name="fullyEncode">If true, URL-encodes reserved characters such as '/', '+', and '%'. Otherwise, only encodes strictly illegal characters (including '%' but only when not followed by 2 hex characters).</param>
    /// <returns>the Url object with the segment appended</returns>
    /// <exception cref="ArgumentNullException"><paramref name="segment"/> is <see langword="null" />.</exception>
    public Url AppendPathSegment(object segment, bool fullyEncode = false)
    {
        Argument.IsNotNull(segment);

        _EnsureParsed();

        if (fullyEncode)
        {
            _pathSegments.Add(Uri.EscapeDataString(segment.ToInvariantString()));
            _trailingSlash = false;
        }
        else
        {
            var subpath = segment.ToInvariantString();
            foreach (var s in ParsePathSegments(subpath))
            {
                _pathSegments.Add(s);
            }

            _trailingSlash = subpath.OrdinalEndsWith("/");
        }

        _leadingSlash |= !IsRelative;
        return this;
    }

    /// <summary>
    /// Appends multiple segments to the URL path, ensuring there is one and only one '/' character as a separator.
    /// </summary>
    /// <param name="segments">The segments to append</param>
    /// <returns>the Url object with the segments appended</returns>
    /// <exception cref="ArgumentNullException">Thrown when any segment in <paramref name="segments"/> is <see langword="null"/>.</exception>
    public Url AppendPathSegments(params object[] segments)
    {
        foreach (var segment in segments)
        {
            AppendPathSegment(segment);
        }

        return this;
    }

    /// <summary>
    /// Appends multiple segments to the URL path, ensuring there is one and only one '/' character as a separator.
    /// </summary>
    /// <param name="segments">The segments to append</param>
    /// <returns>the Url object with the segments appended</returns>
    /// <exception cref="ArgumentNullException">Thrown when any segment in <paramref name="segments"/> is <see langword="null"/>.</exception>
    public Url AppendPathSegments(IEnumerable<object> segments)
    {
        foreach (var s in segments)
        {
            AppendPathSegment(s);
        }

        return this;
    }

    /// <summary>
    /// Removes the last path segment from the URL.
    /// </summary>
    /// <returns>The Url object.</returns>
    public Url RemovePathSegment()
    {
        _EnsureParsed();
        if (_pathSegments.Count > 0)
        {
            _pathSegments.RemoveAt(_pathSegments.Count - 1);
        }

        return this;
    }

    /// <summary>
    /// Removes the entire path component of the URL, including the leading slash.
    /// </summary>
    /// <returns>The Url object.</returns>
    public Url RemovePath()
    {
        _EnsureParsed();
        _pathSegments.Clear();
        _leadingSlash = _trailingSlash = false;
        return this;
    }

    /// <summary>
    /// Adds a parameter to the query, overwriting the value if name exists.
    /// </summary>
    /// <param name="name">Name of query parameter</param>
    /// <param name="value">Value of query parameter</param>
    /// <param name="nullValueHandling">Indicates how to handle null values. Defaults to Remove (any existing)</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url SetQueryParam(string name, object? value, NullValueHandling nullValueHandling = NullValueHandling.Remove)
    {
        QueryParams.AddOrReplace(name, value, false, nullValueHandling);
        return this;
    }

    /// <summary>
    /// Adds a parameter to the query, overwriting the value if name exists.
    /// </summary>
    /// <param name="name">Name of query parameter</param>
    /// <param name="value">Value of query parameter</param>
    /// <param name="isEncoded">Set to true to indicate the value is already URL-encoded</param>
    /// <param name="nullValueHandling">Indicates how to handle null values. Defaults to Remove (any existing)</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url SetQueryParam(
        string name,
        string? value,
        bool isEncoded = false,
        NullValueHandling nullValueHandling = NullValueHandling.Remove
    )
    {
        QueryParams.AddOrReplace(name, value, isEncoded, nullValueHandling);
        return this;
    }

    /// <summary>
    /// Adds a parameter without a value to the query, removing any existing value.
    /// </summary>
    /// <param name="name">Name of query parameter</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url SetQueryParam(string name)
    {
        QueryParams.AddOrReplace(name, value: null, isEncoded: false, NullValueHandling.NameOnly);

        return this;
    }

    /// <summary>
    /// Parses values (usually an anonymous object or dictionary) into name/value pairs and adds them to the query, overwriting any that already exist.
    /// </summary>
    /// <param name="values">Typically an anonymous object, ie: new { x = 1, y = 2 }</param>
    /// <param name="nullValueHandling">Indicates how to handle null values. Defaults to Remove (any existing)</param>
    /// <returns>The Url object with the query parameters added</returns>
    [RequiresUnreferencedCode("Uses reflection to extract properties when passing anonymous objects.")]
    public Url SetQueryParams(object? values, NullValueHandling nullValueHandling = NullValueHandling.Remove)
    {
        if (values is null)
        {
            return this;
        }

        if (values is string s)
        {
            return SetQueryParam(s);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in values.ToKeyValuePairs())
        {
            if (visited.Add(key))
            {
                SetQueryParam(key, value, nullValueHandling); // overwrite existing key(s)
            }
            else
            {
                AppendQueryParam(key, value, nullValueHandling); // unless they're in this same collection (#370)
            }
        }

        return this;
    }

    /// <summary>
    /// Adds multiple parameters without values to the query.
    /// </summary>
    /// <param name="names">Names of query parameters.</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url SetQueryParams(IEnumerable<string>? names)
    {
        if (names is null)
        {
            return this;
        }

        foreach (var name in names.Where(n => !string.IsNullOrEmpty(n)))
        {
            SetQueryParam(name);
        }

        return this;
    }

    /// <summary>
    /// Adds multiple parameters without values to the query.
    /// </summary>
    /// <param name="names">Names of query parameters</param>
    /// <returns>The Url object with the query parameter added.</returns>
    public Url SetQueryParams(params string[] names) => SetQueryParams(names as IEnumerable<string>);

    /// <summary>
    /// Adds a parameter to the query.
    /// </summary>
    /// <param name="name">Name of query parameter</param>
    /// <param name="value">Value of query parameter</param>
    /// <param name="nullValueHandling">Indicates how to handle null values. Defaults to Remove (any existing)</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url AppendQueryParam(
        string name,
        object? value,
        NullValueHandling nullValueHandling = NullValueHandling.Remove
    )
    {
        QueryParams.Add(name, value, false, nullValueHandling);
        return this;
    }

    /// <summary>
    /// Adds a parameter to the query.
    /// </summary>
    /// <param name="name">Name of query parameter</param>
    /// <param name="value">Value of query parameter</param>
    /// <param name="isEncoded">Set to true to indicate the value is already URL-encoded</param>
    /// <param name="nullValueHandling">Indicates how to handle null values. Defaults to Remove (any existing)</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url AppendQueryParam(
        string name,
        string? value,
        bool isEncoded = false,
        NullValueHandling nullValueHandling = NullValueHandling.Remove
    )
    {
        QueryParams.Add(name, value, isEncoded, nullValueHandling);
        return this;
    }

    /// <summary>
    /// Adds a parameter without a value to the query.
    /// </summary>
    /// <param name="name">Name of query parameter</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url AppendQueryParam(string name)
    {
        QueryParams.Add(name, null, false, NullValueHandling.NameOnly);
        return this;
    }

    /// <summary>
    /// Parses values (usually an anonymous object or dictionary) into name/value pairs and adds them to the query.
    /// </summary>
    /// <param name="values">Typically an anonymous object, ie: new { x = 1, y = 2 }</param>
    /// <param name="nullValueHandling">Indicates how to handle null values. Defaults to Remove (any existing)</param>
    /// <returns>The Url object with the query parameters added</returns>
    [RequiresUnreferencedCode("Uses reflection to extract properties when passing anonymous objects.")]
    public Url AppendQueryParam(object? values, NullValueHandling nullValueHandling = NullValueHandling.Remove)
    {
        if (values is null)
        {
            return this;
        }

        if (values is string s)
        {
            return AppendQueryParam(s);
        }

        foreach (var kv in values.ToKeyValuePairs())
        {
            AppendQueryParam(kv.Key, kv.Value, nullValueHandling);
        }

        return this;
    }

    /// <summary>
    /// Adds multiple parameters without values to the query.
    /// </summary>
    /// <param name="names">Names of query parameters.</param>
    /// <returns>The Url object with the query parameter added</returns>
    public Url AppendQueryParam(IEnumerable<string>? names)
    {
        if (names is null)
        {
            return this;
        }

        foreach (var name in names.Where(n => !string.IsNullOrEmpty(n)))
        {
            AppendQueryParam(name);
        }

        return this;
    }

    /// <summary>
    /// Adds multiple parameters without values to the query.
    /// </summary>
    /// <param name="names">Names of query parameters</param>
    /// <returns>The Url object with the query parameter added.</returns>
    public Url AppendQueryParam(params string[] names) => AppendQueryParam(names as IEnumerable<string>);

    /// <summary>
    /// Removes a name/value pair from the query by name.
    /// </summary>
    /// <param name="name">Query string parameter name to remove</param>
    /// <returns>The Url object with the query parameter removed</returns>
    public Url RemoveQueryParam(string name)
    {
        QueryParams.Remove(name);
        return this;
    }

    /// <summary>
    /// Removes multiple name/value pairs from the query by name.
    /// </summary>
    /// <param name="names">Query string parameter names to remove</param>
    /// <returns>The Url object.</returns>
    public Url RemoveQueryParams(params string[] names)
    {
        foreach (var name in names)
        {
            QueryParams.Remove(name);
        }

        return this;
    }

    /// <summary>
    /// Removes multiple name/value pairs from the query by name.
    /// </summary>
    /// <param name="names">Query string parameter names to remove</param>
    /// <returns>The Url object with the query parameters removed</returns>
    public Url RemoveQueryParams(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            QueryParams.Remove(name);
        }

        return this;
    }

    /// <summary>
    /// Removes the entire query component of the URL.
    /// </summary>
    /// <returns>The Url object.</returns>
    public Url RemoveQuery()
    {
        QueryParams.Clear();
        return this;
    }

    /// <summary>
    /// Set the URL fragment fluently.
    /// </summary>
    /// <param name="fragment">The part of the URL after #</param>
    /// <returns>The Url object with the new fragment set</returns>
    public Url SetFragment(string? fragment)
    {
        Fragment = fragment ?? "";
        return this;
    }

    /// <summary>
    /// Removes the URL fragment including the #.
    /// </summary>
    /// <returns>The Url object with the fragment removed</returns>
    public Url RemoveFragment() => SetFragment("");

    /// <summary>
    /// Resets the URL to its root, including the scheme, any user info, host, and port (if specified).
    /// </summary>
    /// <returns>The Url object trimmed to its root.</returns>
    public Url ResetToRoot()
    {
        _EnsureParsed();
        _pathSegments.Clear();
        QueryParams.Clear();
        Fragment = "";
        _leadingSlash = false;
        _trailingSlash = false;
        return this;
    }

    /// <summary>
    /// Resets the URL to its original state as set in the constructor.
    /// </summary>
    public Url Reset()
    {
        if (_parsed)
        {
            _scheme = "";
            _userInfo = "";
            _host = "";
            _port = null;
            _pathSegments = [];
            _queryParams = [];
            _fragment = "";
            _leadingSlash = false;
            _trailingSlash = false;
            _parsed = false;
        }
        return this;
    }

    /// <summary>
    /// Creates a copy of this Url.
    /// </summary>
    public Url Clone()
    {
        _EnsureParsed();

        // Copy the already-parsed state instead of serializing to a string and re-parsing it through new Uri(...).
        var clone = new Url
        {
            _scheme = _scheme,
            _userInfo = _userInfo,
            _host = _host,
            _port = _port,
            _pathSegments = [.. _pathSegments],
            _queryParams = new QueryParamCollection(_queryParams.ToString()),
            _fragment = _fragment,
            _leadingSlash = _leadingSlash,
            _trailingSlash = _trailingSlash,
            _trailingQmark = _trailingQmark,
            _trailingHash = _trailingHash,
            _parsed = true,
        };

        return clone;
    }

    #endregion

    #region conversion, equality, etc.
    /// <summary>
    /// Converts this Url object to its string representation.
    /// </summary>
    /// <param name="encodeSpaceAsPlus">Indicates whether to encode spaces with the "+" character instead of "%20"</param>
    public string ToString(bool encodeSpaceAsPlus)
    {
        if (!_parsed)
        {
            return _originalString ?? "";
        }

        var sb = new StringBuilder();

        // Append Root components inline: scheme + authority
        if (_scheme.Length > 0)
        {
            sb.Append(_scheme);
            sb.Append(':');
        }

        var hasUserInfo = _userInfo.Length > 0;
        var hasAuthority = hasUserInfo || _host.Length > 0 || _port.HasValue;
        if (hasAuthority)
        {
            sb.Append("//");
            if (hasUserInfo)
            {
                sb.Append(_userInfo);
                sb.Append('@');
            }

            sb.Append(_host);
            if (_port.HasValue)
            {
                sb.Append(':');
                sb.Append(_port.Value);
            }
        }

        // Append Path inline
        if (_leadingSlash)
        {
            sb.Append('/');
        }

        for (var i = 0; i < _pathSegments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('/');
            }

            var segment = _pathSegments[i];
            if (encodeSpaceAsPlus)
            {
                sb.Append(segment.Replace("%20", "+", StringComparison.Ordinal));
            }
            else
            {
                sb.Append(segment);
            }
        }

        if (_trailingSlash && _pathSegments.Count > 0)
        {
            sb.Append('/');
        }

        // Append Query
        if (_trailingQmark || _queryParams.Count != 0)
        {
            sb.Append('?');
            _queryParams.AppendTo(sb, encodeSpaceAsPlus);
        }

        // Append Fragment
        if (_trailingHash || _fragment.Length > 0)
        {
            sb.Append('#');
            sb.Append(_fragment);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts this Url object to its string representation.
    /// </summary>
    public override string ToString() => ToString(false);

    /// <summary>
    /// Converts this Url object to System.Uri
    /// </summary>
    /// <returns>The <see cref="System.Uri"/> object.</returns>
    /// <exception cref="UriFormatException">Thrown when the string representation of this URL is not a valid URI.</exception>
    public Uri ToUri() => new(ToString(), UriKind.RelativeOrAbsolute);

    /// <summary>
    /// Implicit conversion from Url to String.
    /// </summary>
    /// <param name="url">The Url object</param>
    /// <returns>The string representation of <paramref name="url"/>, or <see langword="null"/> if <paramref name="url"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(url))]
    public static implicit operator string?(Url? url) => url?.ToString();

    /// <summary>
    /// Implicit conversion from String to Url.
    /// </summary>
    /// <param name="url">The String representation of the URL</param>
    /// <returns>A new <see cref="Url"/> object built from <paramref name="url"/>.</returns>
    public static implicit operator Url(string? url) => new(url);

    /// <summary>
    /// Builds a <see cref="Url"/> from its string representation. Named alternate for the implicit string-to-Url conversion.
    /// </summary>
    /// <param name="url">The String representation of the URL.</param>
    /// <returns>A new <see cref="Url"/> object built from <paramref name="url"/>.</returns>
    public static Url FromString(string? url) => url;

    /// <summary>
    /// Implicit conversion from System.Uri to <see cref="Url"/>.
    /// </summary>
    /// <param name="uri">The <see cref="System.Uri"/> to convert.</param>
    /// <returns>A new <see cref="Url"/> object built from <paramref name="uri"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static implicit operator Url(Uri uri)
    {
        Argument.IsNotNull(uri);

        return new(uri.ToString());
    }

    /// <summary>
    /// Builds a <see cref="Url"/> from a <see cref="System.Uri"/>. Named alternate for the implicit Uri-to-Url conversion.
    /// </summary>
    /// <param name="uri">The <see cref="System.Uri"/> to convert.</param>
    /// <returns>A new <see cref="Url"/> object built from <paramref name="uri"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    public static Url FromUri(Uri uri)
    {
        Argument.IsNotNull(uri);

        return uri;
    }

    /// <summary>
    /// True if obj is an instance of Url and its string representation is equal to this instance's string representation.
    /// </summary>
    /// <param name="obj">The object to compare to this instance.</param>
    public override bool Equals(object? obj) => obj is Url url && ToString().OrdinalEquals(url.ToString());

    /// <summary>
    /// Returns the hashcode for this Url.
    /// </summary>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());
    #endregion

    #region static utility methods
    /// <summary>
    /// Basically a Path.Combine for URLs. Ensures exactly one '/' separates each segment,
    /// and exactly one '&amp;' separates each query parameter.
    /// URL-encodes illegal characters but not reserved characters.
    /// </summary>
    /// <param name="parts">URL parts to combine.</param>
    /// <returns>The combined, illegal-character-encoded URL string.</returns>
    public static string Combine(string?[] parts) => Combine(parts.AsSpan());

    /// <summary>
    /// Basically a Path.Combine for URLs. Ensures exactly one '/' separates each segment,
    /// and exactly one '&amp;' separates each query parameter.
    /// URL-encodes illegal characters but not reserved characters.
    /// </summary>
    /// <param name="parts">URL parts to combine.</param>
    /// <returns>The combined, illegal-character-encoded URL string.</returns>
    [OverloadResolutionPriority(1)]
    public static string Combine(params ReadOnlySpan<string?> parts)
    {
        // Pre-calculate capacity to avoid reallocations
        var capacity = 0;
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part))
            {
                capacity += part.Length + 1; // +1 for potential separator
            }
        }

        if (capacity == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(capacity);
        var inQuery = false;
        var inFragment = false;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            if (_EndsWithChar(sb, '?') || part.OrdinalStartsWith("?"))
            {
                _AppendWithSingleSeparator(sb, part, '?');
            }
            else if (_EndsWithChar(sb, '#') || part.OrdinalStartsWith("#"))
            {
                _AppendWithSingleSeparator(sb, part, '#');
            }
            else if (inFragment)
            {
                sb.Append(part);
            }
            else if (inQuery)
            {
                _AppendWithSingleSeparator(sb, part, '&');
            }
            else
            {
                _AppendWithSingleSeparator(sb, part, '/');
            }

            if (part.OrdinalContains("#"))
            {
                inQuery = false;
                inFragment = true;
            }
            else if (!inFragment && part.OrdinalContains("?"))
            {
                inQuery = true;
            }
        }

        return EncodeIllegalCharacters(sb.ToString());
    }

    private static bool _EndsWithChar(StringBuilder sb, char c) => sb.Length > 0 && sb[^1] == c;

    private static void _AppendWithSingleSeparator(StringBuilder sb, string part, char separator)
    {
        // Trim trailing separator from existing content
        while (sb.Length > 0 && sb[^1] == separator)
        {
            sb.Length--;
        }

        // Skip leading separator from part
        var startIndex = 0;
        while (startIndex < part.Length && part[startIndex] == separator)
        {
            startIndex++;
        }

        // Add separator if we have existing content
        if (sb.Length > 0 && startIndex < part.Length)
        {
            sb.Append(separator);
        }

        // Append the rest of the part
        if (startIndex < part.Length)
        {
            sb.Append(part, startIndex, part.Length - startIndex);
        }
        else if (sb.Length == 0)
        {
            // Edge case: part was only separators, append original if sb is empty
            sb.Append(part);
        }
    }

    /// <summary>
    /// Decodes a URL-encoded string.
    /// </summary>
    /// <param name="s">The URL-encoded string.</param>
    /// <param name="interpretPlusAsSpace">If true, any '+' character will be decoded to a space.</param>
    [return: NotNullIfNotNull(nameof(s))]
    public static string? Decode(string? s, bool interpretPlusAsSpace) => UrlEncoder.Decode(s, interpretPlusAsSpace);

    /// <summary>
    /// URL-encodes a string, including reserved characters such as '/' and '?'.
    /// </summary>
    /// <param name="s">The string to encode.</param>
    /// <param name="encodeSpaceAsPlus">If true, spaces will be encoded as + signs. Otherwise, they'll be encoded as %20.</param>
    /// <returns>The encoded URL.</returns>
    [return: NotNullIfNotNull(nameof(s))]
    public static string? Encode(string? s, bool encodeSpaceAsPlus = false) => UrlEncoder.Encode(s, encodeSpaceAsPlus);

    /// <summary>
    /// URL-encodes characters in a string that are neither reserved nor unreserved. Avoids encoding reserved characters such as '/' and '?'. Avoids encoding '%' if it begins a %-hex-hex sequence (i.e. avoids double-encoding).
    /// </summary>
    /// <param name="s">The string to encode.</param>
    /// <param name="encodeSpaceAsPlus">If true, spaces will be encoded as + signs. Otherwise, they'll be encoded as %20.</param>
    /// <returns>The encoded URL.</returns>
    [return: NotNullIfNotNull(nameof(s))]
    public static string? EncodeIllegalCharacters(string? s, bool encodeSpaceAsPlus = false) =>
        UrlEncoder.EncodeIllegalCharacters(s, encodeSpaceAsPlus);

    /// <summary>
    /// Checks if a string is a well-formed absolute URL.
    /// </summary>
    /// <param name="url">The string to check</param>
    /// <returns>true if the string is a well-formed absolute URL</returns>
    public static bool IsValid([NotNullWhen(true)] string? url) =>
        !string.IsNullOrWhiteSpace(url)
        &&
        // TryCreate will succeed for URLs starting with "//". We want to require a scheme to be considered "absolute".
        !url.Trim().StartsWith('/')
        &&
        // Don't be tempted to use IsWellFormedUriString - it's known to return false positives on some platforms:
        // https://github.com/dotnet/runtime/issues/72632
        Uri.TryCreate(url, UriKind.Absolute, out _);

    #endregion
}
