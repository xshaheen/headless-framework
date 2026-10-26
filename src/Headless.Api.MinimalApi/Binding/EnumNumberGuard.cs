// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Headless.Api;

/// <summary>
/// Refuses an enum sent as a number in a route, query, header, or form value of a Minimal API endpoint, answering 400
/// <c>g:invalid_parameter_value</c> naming it, as the JSON body and an MVC action refuse it.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core parses an enum outside the body with <c>Enum.TryParse</c>, which accepts the member name as declared,
/// refuses anything else, and also accepts a number; it offers no way to change that. Every other rule is left to it:
/// the Headless web preset writes enums under their member names, so the text a client copies from a response is the
/// text <c>Enum.TryParse</c> reads. Only the number has to be refused here, before ASP.NET Core binds it.
/// </para>
/// <para>
/// The enum values to check are chosen once, when the endpoint is built, from the handler's parameters, so an endpoint
/// with none is left unwrapped. A request reads its values in place and allocates nothing.
/// </para>
/// </remarks>
internal sealed class EnumNumberGuard
{
    private readonly BoundValue[] _route;
    private readonly BoundValue[] _query;
    private readonly BoundValue[] _headers;
    private readonly BoundValue[] _form;
    private readonly RequestDelegate _next;

    private EnumNumberGuard(List<BoundValue> values, RequestDelegate next)
    {
        _route = [.. values.Where(x => x.Source == ValueSource.Route)];
        _query = [.. values.Where(x => x.Source == ValueSource.Query)];
        _headers = [.. values.Where(x => x.Source == ValueSource.Header)];
        _form = [.. values.Where(x => x.Source == ValueSource.Form)];
        _next = next;
    }

    /// <summary>Wraps <paramref name="endpoint"/>'s request delegate when its handler binds an enum to check.</summary>
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
        var values = new List<BoundValue>();

        foreach (var parameter in method.GetParameters())
        {
            _CollectParameter(parameter, parameter.ParameterType, parameter.Name, routeNames, values);
        }

        if (values.Count > 0)
        {
            endpoint.RequestDelegate = new EnumNumberGuard(values, next)._InvokeAsync;
        }
    }

    private Task _InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var failed = _CheckRoute(request) ?? _CheckQuery(request) ?? _CheckHeaders(request);

        if (failed is not null)
        {
            return _WriteInvalidAsync(context, failed);
        }

        if (_form.Length == 0 || !_HasFormContentType(request.ContentType))
        {
            return _next(context);
        }

        // ASP.NET Core reads the form before it binds, so reading it here costs nothing extra; awaiting it costs a state
        // machine only when the body has not arrived yet.
        var reading = request.ReadFormAsync(context.RequestAborted);

        if (!reading.IsCompletedSuccessfully)
        {
            return _InvokeAfterFormAsync(context, reading);
        }

#pragma warning disable CA1849 // False positive: the task completed successfully above, so Result returns without blocking.
        failed = _CheckForm(reading.Result);
#pragma warning restore CA1849

        return failed is null ? _next(context) : _WriteInvalidAsync(context, failed);
    }

    private async Task _InvokeAfterFormAsync(HttpContext context, Task<IFormCollection> reading)
    {
        IFormCollection form;

        try
        {
            form = await reading.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            // A malformed or oversized form is ASP.NET Core's to answer: it caches the failed read and turns it into its
            // own 400 when it binds, where escaping here would surface as a 500.
            await _next(context).ConfigureAwait(false);

            return;
        }

        var failed = _CheckForm(form);

        await (failed is null ? _next(context) : _WriteInvalidAsync(context, failed)).ConfigureAwait(false);
    }

    private string? _CheckRoute(HttpRequest request)
    {
        foreach (var value in _route)
        {
            if (
                request.RouteValues.TryGetValue(value.Name, out var raw)
                && raw is string text
                && RequestEnumText.IsNumeric(text)
            )
            {
                return value.Name;
            }
        }

        return null;
    }

    private string? _CheckQuery(HttpRequest request)
    {
        if (_query.Length == 0)
        {
            return null;
        }

        var query = request.Query;

        foreach (var value in _query)
        {
            if (query.TryGetValue(value.Name, out var raw) && _HasNumber(raw))
            {
                return value.Name;
            }
        }

        return null;
    }

    private string? _CheckHeaders(HttpRequest request)
    {
        foreach (var value in _headers)
        {
            if (request.Headers.TryGetValue(value.Name, out var raw) && _HasNumber(raw))
            {
                return value.Name;
            }
        }

        return null;
    }

    private string? _CheckForm(IFormCollection form)
    {
        foreach (var value in _form)
        {
            if (form.TryGetValue(value.Name, out var raw) && _HasNumber(raw))
            {
                return value.Name;
            }
        }

        return null;
    }

    private static bool _HasNumber(StringValues raw)
    {
        for (var i = 0; i < raw.Count; i++)
        {
            if (raw[i] is { } text && RequestEnumText.IsNumeric(text))
            {
                return true;
            }
        }

        return false;
    }

    private static Task _WriteInvalidAsync(HttpContext context, string parameter)
    {
        var creator = context.RequestServices.GetRequiredService<IProblemDetailsCreator>();
        var error = GeneralMessageDescriber.InvalidParameterValue(parameter);

        return TypedResults.Problem(creator.BadRequest(error.Description, error)).ExecuteAsync(context);
    }

    // The two form media types, compared without parsing the header into an object.
    private static bool _HasFormContentType(string? contentType) =>
        contentType is not null
        && (
            contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)
        );

    private static void _CollectParameter(
        ICustomAttributeProvider attributes,
        Type type,
        string? name,
        HashSet<string> routeNames,
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
                _CollectParameter(member, memberType, memberName, routeNames, values);
            }

            return;
        }

        if (metadata.Any(x => x is IFromBodyMetadata or IFromServiceMetadata or FromKeyedServicesAttribute))
        {
            return;
        }

        if (metadata.OfType<IFromRouteMetadata>().FirstOrDefault() is { } fromRoute)
        {
            _AddEnum(ValueSource.Route, fromRoute.Name ?? name, type, values);
        }
        else if (metadata.OfType<IFromQueryMetadata>().FirstOrDefault() is { } fromQuery)
        {
            _AddEnum(ValueSource.Query, fromQuery.Name ?? name, type, values);
        }
        else if (metadata.OfType<IFromHeaderMetadata>().FirstOrDefault() is { } fromHeader)
        {
            _AddEnum(ValueSource.Header, fromHeader.Name ?? name, type, values);
        }
        else if (metadata.OfType<IFromFormMetadata>().FirstOrDefault() is { } fromForm)
        {
            // A simple form value binds under its own name; a form type binds its direct members by name. A member
            // nested deeper is left to ASP.NET Core.
            if (!_AddEnum(ValueSource.Form, fromForm.Name ?? name, type, values) && !_IsSimple(type))
            {
                foreach (var (_, memberType, memberName) in _Members(type))
                {
                    _AddEnum(ValueSource.Form, memberName, memberType, values);
                }
            }
        }
        else
        {
            // ASP.NET Core's inference: a simple value named in the route template is a route value, else a query one.
            var source = routeNames.Contains(name) ? ValueSource.Route : ValueSource.Query;
            _AddEnum(source, name, type, values);
        }
    }

    // Adds the value when it is an enum or a collection of enums, and says whether it was.
    private static bool _AddEnum(ValueSource source, string name, Type type, List<BoundValue> values)
    {
        var element = _ElementType(type);
        element = Nullable.GetUnderlyingType(element) ?? element;

        if (!element.IsEnum)
        {
            return false;
        }

        // A collection's repeated values arrive under the same name, so one lookup checks them all.
        values.Add(new BoundValue(source, name));

        return true;
    }

    // A value ASP.NET Core parses from text rather than maps member by member, so the form walk does not descend into it.
    private static bool _IsSimple(Type type)
    {
        var element = _ElementType(type);
        element = Nullable.GetUnderlyingType(element) ?? element;

        return element.IsPrimitive
            || element == typeof(string)
            || element == typeof(decimal)
            || element == typeof(Uri)
            || element
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Any(x => string.Equals(x.Name, "TryParse", StringComparison.Ordinal));
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

    // The members ASP.NET Core binds for [AsParameters] and a form maps for a complex type: the constructor's
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

    private sealed record BoundValue(ValueSource Source, string Name);

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
