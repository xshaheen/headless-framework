// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json;
using Headless.Api.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Headless.Api;

/// <summary>
/// Gives a Minimal API endpoint the request-value rules an MVC action has: an enum outside the body is read by the
/// names the JSON body accepts, and a value that cannot be bound answers 400 <c>g:invalid_parameter_value</c> naming
/// it.
/// </summary>
/// <remarks>
/// <para>
/// The request delegate factory parses an enum with a case-sensitive <c>Enum.TryParse</c> that also accepts numbers,
/// and it offers no way to replace that. So this wraps the endpoint's built request delegate and runs before the
/// factory binds anything: each enum value is rewritten to the member name <c>Enum.TryParse</c> accepts, or answered
/// with 400 when the body would reject it, and every other simple value is checked with the factory's own parse.
/// </para>
/// <para>
/// The values to check are chosen once, when the endpoint is built, from the handler's parameters, so an endpoint with
/// none is left unwrapped. A request copies the query, headers, or form only when a value has to be rewritten.
/// </para>
/// </remarks>
internal sealed class RequestValueBinder
{
    private const int _MaxFormDepth = 4;

    private static readonly JsonSerializerOptions _DefaultOptions = new JsonOptions().SerializerOptions;

    private readonly BoundValue[] _route;
    private readonly BoundValue[] _query;
    private readonly BoundValue[] _headers;
    private readonly BoundValue[] _form;
    private readonly RequestDelegate _next;

    private RequestValueBinder(List<BoundValue> values, RequestDelegate next)
    {
        _route = [.. values.Where(x => x.Source == ValueSource.Route)];
        _query = [.. values.Where(x => x.Source == ValueSource.Query)];
        _headers = [.. values.Where(x => x.Source == ValueSource.Header)];
        _form = [.. values.Where(x => x.Source == ValueSource.Form)];
        _next = next;
    }

    /// <summary>Wraps <paramref name="endpoint"/>'s request delegate when its handler binds a value to check.</summary>
    public static void Apply(EndpointBuilder endpoint)
    {
        // A surface and an explicit call on the same endpoint must not wrap it twice.
        if (endpoint.Metadata.Contains(AppliedMarker.Instance))
        {
            return;
        }

        endpoint.Metadata.Add(AppliedMarker.Instance);

        if (
            endpoint.RequestDelegate is not { } next
            || endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault() is not { } method
        )
        {
            return;
        }

        var routeNames = endpoint is RouteEndpointBuilder route
            ? route.RoutePattern.Parameters.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        // Without registered options ASP.NET Core reads the body with its defaults, which write enums as numbers.
        var options =
            endpoint.ApplicationServices.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions
            ?? _DefaultOptions;
        var values = new List<BoundValue>();

        foreach (var parameter in method.GetParameters())
        {
            _CollectParameter(parameter, parameter.ParameterType, parameter.Name, routeNames, options, values);
        }

        if (values.Count > 0)
        {
            endpoint.RequestDelegate = new RequestValueBinder(values, next)._InvokeAsync;
        }
    }

    private Task _InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var failed = _NormalizeRoute(request) ?? _NormalizeQuery(request) ?? _NormalizeHeaders(request);

        if (failed is not null)
        {
            return _WriteInvalidAsync(context, failed);
        }

        return _form.Length > 0 && request.HasFormContentType ? _InvokeWithFormAsync(context) : _next(context);
    }

    private async Task _InvokeWithFormAsync(HttpContext context)
    {
        var request = context.Request;
        var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        Dictionary<string, StringValues>? rewritten = null;

        foreach (var value in _form)
        {
            foreach (var (key, raw) in form)
            {
                if (!value.MatchesFormKey(key))
                {
                    continue;
                }

                if (!value.TryNormalize(raw, out var normalized))
                {
                    await _WriteInvalidAsync(context, key).ConfigureAwait(false);

                    return;
                }

                if (normalized is { } changed)
                {
                    rewritten ??= _Copy(form);
                    rewritten[key] = changed;
                }
            }
        }

        if (rewritten is not null)
        {
            request.Form = new FormCollection(rewritten, form.Files);
        }

        await _next(context).ConfigureAwait(false);
    }

    private string? _NormalizeRoute(HttpRequest request)
    {
        foreach (var value in _route)
        {
            if (request.RouteValues.TryGetValue(value.Name, out var raw) && raw?.ToString() is { } text)
            {
                if (!value.TryNormalize(text, out var normalized))
                {
                    return value.Name;
                }

                if (normalized is { } changed)
                {
                    request.RouteValues[value.Name] = changed.ToString();
                }
            }
        }

        return null;
    }

    private string? _NormalizeQuery(HttpRequest request)
    {
        if (_query.Length == 0)
        {
            return null;
        }

        var query = request.Query;
        Dictionary<string, StringValues>? rewritten = null;

        foreach (var value in _query)
        {
            if (!query.TryGetValue(value.Name, out var raw))
            {
                continue;
            }

            if (!value.TryNormalize(raw, out var normalized))
            {
                return value.Name;
            }

            if (normalized is { } changed)
            {
                rewritten ??= _Copy(query);
                rewritten[value.Name] = changed;
            }
        }

        if (rewritten is not null)
        {
            request.Query = new QueryCollection(rewritten);
        }

        return null;
    }

    private string? _NormalizeHeaders(HttpRequest request)
    {
        foreach (var value in _headers)
        {
            if (!request.Headers.TryGetValue(value.Name, out var raw))
            {
                continue;
            }

            if (!value.TryNormalize(raw, out var normalized))
            {
                return value.Name;
            }

            if (normalized is { } changed)
            {
                request.Headers[value.Name] = changed;
            }
        }

        return null;
    }

    private static Task _WriteInvalidAsync(HttpContext context, string parameter)
    {
        var creator = context.RequestServices.GetRequiredService<IProblemDetailsCreator>();
        var error = GeneralMessageDescriber.InvalidParameterValue(parameter);

        return TypedResults.Problem(creator.BadRequest(error.Description, error)).ExecuteAsync(context);
    }

    private static Dictionary<string, StringValues> _Copy(IEnumerable<KeyValuePair<string, StringValues>> source)
    {
        var copy = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in source)
        {
            copy[key] = value;
        }

        return copy;
    }

    private static void _CollectParameter(
        ICustomAttributeProvider attributes,
        Type type,
        string? name,
        HashSet<string> routeNames,
        JsonSerializerOptions options,
        List<BoundValue> values
    )
    {
        if (name is null)
        {
            return;
        }

        var metadata = attributes.GetCustomAttributes(inherit: true);

        if (metadata.OfType<AsParametersAttribute>().Any())
        {
            foreach (var (member, memberType, memberName) in _Members(type))
            {
                _CollectParameter(member, memberType, memberName, routeNames, options, values);
            }

            return;
        }

        if (metadata.Any(x => x is IFromBodyMetadata or IFromServiceMetadata or FromKeyedServicesAttribute))
        {
            return;
        }

        if (metadata.OfType<IFromRouteMetadata>().FirstOrDefault() is { } fromRoute)
        {
            _AddSimple(ValueSource.Route, fromRoute.Name ?? name, type, options, values);
        }
        else if (metadata.OfType<IFromQueryMetadata>().FirstOrDefault() is { } fromQuery)
        {
            _AddSimple(ValueSource.Query, fromQuery.Name ?? name, type, options, values);
        }
        else if (metadata.OfType<IFromHeaderMetadata>().FirstOrDefault() is { } fromHeader)
        {
            _AddSimple(ValueSource.Header, fromHeader.Name ?? name, type, options, values);
        }
        else if (metadata.OfType<IFromFormMetadata>().FirstOrDefault() is { } fromForm)
        {
            // A simple form value binds under its own name; a complex one binds its members by property path.
            if (!_AddSimple(ValueSource.Form, fromForm.Name ?? name, type, options, values))
            {
                _CollectForm(type, prefix: string.Empty, depth: 0, options, values);
            }
        }
        else
        {
            // The factory's inference: a simple value named in the route template is a route value, else a query one.
            var source = routeNames.Contains(name) ? ValueSource.Route : ValueSource.Query;
            _AddSimple(source, name, type, options, values);
        }
    }

    private static void _CollectForm(
        Type type,
        string prefix,
        int depth,
        JsonSerializerOptions options,
        List<BoundValue> values
    )
    {
        if (depth >= _MaxFormDepth || type == typeof(string) || type.IsValueType || type.IsAbstract)
        {
            return;
        }

        foreach (var (_, memberType, memberName) in _Members(type))
        {
            var key = prefix + memberName;

            if (!_AddSimple(ValueSource.Form, key, memberType, options, values))
            {
                _CollectForm(memberType, key + ".", depth + 1, options, values);
            }
        }
    }

    private static bool _AddSimple(
        ValueSource source,
        string name,
        Type type,
        JsonSerializerOptions options,
        List<BoundValue> values
    )
    {
        var element = _ElementType(type);
        var isCollection = element != type;
        element = Nullable.GetUnderlyingType(element) ?? element;

        if (element.IsEnum)
        {
            // Options that write the enum as a number give the body no names, so the factory's parsing stays.
            if (RequestEnumValue.For(element, options) is { } names)
            {
                values.Add(new BoundValue(source, name, isCollection, names, Parse: null));
            }

            return true;
        }

        if (element == typeof(string))
        {
            return true;
        }

        if (SimpleValueParser.For(element) is { } parse)
        {
            values.Add(new BoundValue(source, name, isCollection, Enum: null, parse));

            return true;
        }

        return false;
    }

    private static Type _ElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType()!;
        }

        if (type.IsGenericType && type != typeof(string))
        {
            var definition = type.GetGenericTypeDefinition();

            if (
                definition == typeof(List<>)
                || definition == typeof(IList<>)
                || definition == typeof(IReadOnlyList<>)
                || definition == typeof(ICollection<>)
                || definition == typeof(IReadOnlyCollection<>)
                || definition == typeof(IEnumerable<>)
            )
            {
                return type.GetGenericArguments()[0];
            }
        }

        return type;
    }

    // The members the factory binds for [AsParameters] and a form maps for a complex type: the constructor's
    // parameters when it has some, else the settable public properties. A constructor parameter's attributes are
    // read from the property it initializes too, where a record declares them.
    private static IEnumerable<(ICustomAttributeProvider Attributes, Type Type, string Name)> _Members(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var constructor = type.GetConstructors().OrderByDescending(x => x.GetParameters().Length).FirstOrDefault();

        if (constructor?.GetParameters() is { Length: > 0 } parameters)
        {
            foreach (var parameter in parameters)
            {
                var property = properties.FirstOrDefault(x =>
                    string.Equals(x.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                );

                yield return (
                    property is null ? parameter : new CombinedAttributes(parameter, property),
                    parameter.ParameterType,
                    property?.Name ?? parameter.Name!
                );
            }

            yield break;
        }

        foreach (var property in properties.Where(x => x.CanWrite && x.GetIndexParameters().Length == 0))
        {
            yield return (property, property.PropertyType, property.Name);
        }
    }

    private enum ValueSource
    {
        Route = 1,
        Query = 2,
        Header = 3,
        Form = 4,
    }

    private sealed record BoundValue(
        ValueSource Source,
        string Name,
        bool IsCollection,
        RequestEnumValue? Enum,
        Func<string, bool>? Parse
    )
    {
        // A collection member arrives as repeated "Name" fields or as indexed "Name[0]" fields.
        public bool MatchesFormKey(string key)
        {
            if (string.Equals(key, Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsCollection
                && key.Length > Name.Length + 2
                && key.StartsWith(Name, StringComparison.OrdinalIgnoreCase)
                && key[Name.Length] == '['
                && key[^1] == ']'
                && key.IndexOf('.', Name.Length) < 0;
        }

        /// <summary>
        /// Checks every value; <paramref name="normalized"/> is the rewritten values when an enum name had to change,
        /// else <see langword="null"/>, so a request whose values are already canonical allocates nothing.
        /// </summary>
        public bool TryNormalize(StringValues raw, out StringValues? normalized)
        {
            normalized = null;
            string?[]? rewritten = null;

            for (var i = 0; i < raw.Count; i++)
            {
                var text = raw[i];

                // An empty value is the factory's to judge: null for an optional parameter, a missing one otherwise.
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                if (Enum is not null)
                {
                    if (!Enum.TryGetCanonical(text, out var canonical))
                    {
                        return false;
                    }

                    if (!string.Equals(canonical, text, StringComparison.Ordinal))
                    {
                        rewritten ??= [.. raw];
                        rewritten[i] = canonical;
                    }
                }
                else if (!Parse!(text))
                {
                    return false;
                }
            }

            if (rewritten is not null)
            {
                normalized = new StringValues(rewritten);
            }

            return true;
        }
    }

    private sealed class CombinedAttributes(ICustomAttributeProvider first, ICustomAttributeProvider second)
        : ICustomAttributeProvider
    {
        public object[] GetCustomAttributes(bool inherit) =>
            [.. first.GetCustomAttributes(inherit), .. second.GetCustomAttributes(inherit)];

        public object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            [
                .. first.GetCustomAttributes(attributeType, inherit),
                .. second.GetCustomAttributes(attributeType, inherit),
            ];

        public bool IsDefined(Type attributeType, bool inherit) =>
            first.IsDefined(attributeType, inherit) || second.IsDefined(attributeType, inherit);
    }

    private sealed class AppliedMarker
    {
        public static readonly AppliedMarker Instance = new();
    }
}
