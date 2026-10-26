// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Headless.OpenApi;

/// <summary>
/// Reads the XML documentation file an assembly ships beside itself (<c>GenerateDocumentationFile</c>) and answers the
/// summary of a type, member, or parameter as plain text.
/// </summary>
/// <remarks>
/// The native XML-comment source generator only sees <c>AddOpenApi</c> calls compiled in the application's own project,
/// so a document Headless registers (<c>AddHeadless()</c>, <c>AddHeadlessApiSurfaceDocuments</c>) would carry no
/// comments of the application's types. Reading the file at run time covers every assembly that ships one, Headless's
/// own included. An assembly without a file, or with one that does not parse, contributes nothing.
/// </remarks>
internal static class XmlDocumentation
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, XElement>?> _Files = new();

    public static string? Summary(Type type) => _Text(_Find(type.Assembly, _TypeId(type)), "summary");

    public static string? Summary(MemberInfo member)
    {
        if (member.DeclaringType is not { } declaringType)
        {
            return null;
        }

        var prefix = member switch
        {
            PropertyInfo => "P:",
            FieldInfo => "F:",
            _ => null,
        };

        if (prefix is null)
        {
            return null;
        }

        var id = prefix + _TypeName(_Definition(declaringType)) + "." + member.Name;

        // A positional record documents its properties as primary-constructor parameters on the type.
        return _Text(_Find(declaringType.Assembly, id), "summary") ?? _TypeParameter(declaringType, member.Name);
    }

    public static string? Summary(MethodInfo method) => _Text(_FindMethod(method), "summary");

    public static string? Remarks(MethodInfo method) => _Text(_FindMethod(method), "remarks");

    public static string? Parameter(MethodInfo method, string name) => _ParamText(_FindMethod(method), name);

    private static string? _TypeParameter(Type type, string name) =>
        _ParamText(_Find(type.Assembly, _TypeId(type)), name);

    private static XElement? _FindMethod(MethodInfo method) =>
        method.DeclaringType is { } type ? _Find(type.Assembly, _MethodId(method)) : null;

    private static XElement? _Find(Assembly assembly, string id) =>
        _Files.GetOrAdd(assembly, _Load) is { } members && members.TryGetValue(id, out var member) ? member : null;

    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "A single-file host has no documentation file beside the assembly, so it reads no comments."
    )]
    private static IReadOnlyDictionary<string, XElement>? _Load(Assembly assembly)
    {
        if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
        {
            return null;
        }

        var path = Path.ChangeExtension(assembly.Location, ".xml");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return XDocument
                .Load(path)
                .Descendants("member")
                .Where(x => x.Attribute("name") is not null)
                .GroupBy(x => x.Attribute("name")!.Value, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        }
#pragma warning disable ERP022 // Deliberate: a documentation file is best-effort input, and an unreadable one only leaves its comments out.
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
#pragma warning restore ERP022
    }

    private static string? _ParamText(XElement? member, string name) =>
        member
            ?.Elements("param")
            .FirstOrDefault(x => string.Equals(x.Attribute("name")?.Value, name, StringComparison.Ordinal))
            is { } param
            ? _Render(param)
            : null;

    private static string? _Text(XElement? member, string elementName) =>
        member?.Element(elementName) is { } element ? _Render(element) : null;

    private static string? _Render(XElement element)
    {
        var paragraphs = new List<string>();
        var current = new StringBuilder();

        foreach (var node in element.Nodes())
        {
            if (node is XElement { Name.LocalName: "para" } para)
            {
                _Flush(paragraphs, current);
                current.Append(_Inline(para));
                _Flush(paragraphs, current);
            }
            else
            {
                current.Append(_Inline(node));
            }
        }

        _Flush(paragraphs, current);

        return paragraphs.Count == 0 ? null : string.Join("\n\n", paragraphs);
    }

    private static void _Flush(List<string> paragraphs, StringBuilder current)
    {
        var text = string.Join(' ', current.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        current.Clear();

        if (text.Length > 0)
        {
            paragraphs.Add(text);
        }
    }

    private static string _Inline(XNode node) =>
        node switch
        {
            XText text => text.Value,
            XElement { Name.LocalName: "see" or "seealso" } see => see.Attribute("langword")?.Value
                ?? (see.Attribute("cref")?.Value is { } cref ? _ShortName(cref) : see.Value),
            XElement { Name.LocalName: "paramref" or "typeparamref" } reference => reference.Attribute("name")?.Value
                ?? "",
            XElement other => string.Concat(other.Nodes().Select(_Inline)),
            _ => "",
        };

    // "T:Orders.OrderView" reads as "OrderView", "P:Orders.OrderView.Total" as "Total".
    private static string _ShortName(string cref)
    {
        var name = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var arguments = name.IndexOf('(', StringComparison.Ordinal);
        name = arguments < 0 ? name : name[..arguments];
        var generic = name.IndexOf('`', StringComparison.Ordinal);
        name = generic < 0 ? name : name[..generic];

        return name[(name.LastIndexOf('.') + 1)..];
    }

    private static string _TypeId(Type type) => "T:" + _TypeName(_Definition(type));

    private static Type _Definition(Type type) =>
        type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;

    private static string _TypeName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string _MethodId(MethodInfo method)
    {
        var id = new StringBuilder("M:")
            .Append(_TypeName(_Definition(method.DeclaringType!)))
            .Append('.')
            .Append(method.Name);

        if (method.IsGenericMethod)
        {
            id.Append("``").Append(method.GetGenericArguments().Length.ToString(CultureInfo.InvariantCulture));
        }

        var parameters = method.GetParameters();

        if (parameters.Length > 0)
        {
            id.Append('(').AppendJoin(',', parameters.Select(x => _ParameterTypeName(x.ParameterType))).Append(')');
        }

        return id.ToString();
    }

    private static string _ParameterTypeName(Type type)
    {
        if (type.IsByRef)
        {
            return _ParameterTypeName(type.GetElementType()!) + "@";
        }

        if (type.IsArray)
        {
            return _ParameterTypeName(type.GetElementType()!) + "[]";
        }

        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "`" : "``")
                + type.GenericParameterPosition.ToString(CultureInfo.InvariantCulture);
        }

        if (!type.IsGenericType)
        {
            return _TypeName(type);
        }

        var definition = _TypeName(type.GetGenericTypeDefinition());
        var tick = definition.IndexOf('`', StringComparison.Ordinal);

        return (tick < 0 ? definition : definition[..tick])
            + "{"
            + string.Join(',', type.GetGenericArguments().Select(_ParameterTypeName))
            + "}";
    }
}
