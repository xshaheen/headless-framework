// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Publishes a member typed by a generic type parameter as nullable exactly when the type argument at the use site is
/// a nullable reference type, giving <c>G&lt;X&gt;</c> and <c>G&lt;X?&gt;</c> separate components when a document uses
/// both.
/// </summary>
/// <remarks>
/// <para>
/// The native generator builds one schema per runtime type, and <c>DataEnvelope&lt;User&gt;</c> and
/// <c>DataEnvelope&lt;User?&gt;</c> are one runtime type. It cannot see the annotation on the type argument, so it
/// publishes every reference-typed <c>T</c> member as nullable, and a generated client types <c>data</c> as
/// <c>User | null</c> for every envelope. The annotation survives only where the type is used: the action's or
/// handler's return value, the body parameter, and the members of the types they reach.
/// </para>
/// <para>
/// So, per operation, this walks each response and JSON body schema together with the use of its type and resolves
/// every generic component against that use. The component named for the type (<c>DataEnvelopeOfUser</c>) describes
/// the use with non-nullable arguments, the way the generator names it; a use with a nullable reference argument gets
/// its own component, named with <c>Nullable</c> before that argument (<c>DataEnvelopeOfNullableUser</c>), unless its
/// schema comes out the same. A member declared <c>T?</c> stays nullable, and value-type arguments are left alone,
/// because their nullability is part of the runtime type. A use with no annotation, such as
/// <c>[ProducesResponseType(typeof(...))]</c> on an <c>IActionResult</c> action, is read as non-nullable, as the
/// generator reads an oblivious type.
/// </para>
/// <para>
/// It runs as an operation transformer because only then is the operation's API description at hand, and after the
/// generator has resolved the operation's schemas into components, which wrap a nullable reference in
/// <c>oneOf: [null, $ref]</c> only after every schema transformer has run. The document pass then drops a component
/// left with no reference once its uses moved to a variant.
/// </para>
/// </remarks>
internal sealed class GenericNullabilityTransformer : IOpenApiOperationTransformer, IOpenApiDocumentTransformer
{
    // One walker per document generation; the transformer itself is shared by every request for the document.
    private static readonly ConditionalWeakTable<OpenApiDocument, DocumentWalker> _Walkers = [];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.Document is not { } document)
        {
            return Task.CompletedTask;
        }

        var walker = _Walkers.GetValue(document, d => new DocumentWalker(d, context.ApplicationServices));
        var description = context.Description;
        var nullability = new NullabilityInfoContext();
        var method = _GetMethod(description);
        var returned = method is null ? null : TypeUse.From(nullability.Create(method.ReturnParameter));

        foreach (var responseType in description.SupportedResponseTypes)
        {
            var key = responseType.IsDefaultResponse
                ? "default"
                : responseType.StatusCode.ToString(CultureInfo.InvariantCulture);

            if (
                responseType.Type is not { } type
                || type == typeof(void)
                || operation.Responses?.TryGetValue(key, out var response) != true
                || response?.Content is not { } content
            )
            {
                continue;
            }

            var use = returned?.Find(type) ?? TypeUse.Oblivious(type);
            _VisitContent(walker, content, use);
        }

        var body = description.ParameterDescriptions.FirstOrDefault(parameter =>
            parameter.Source == BindingSource.Body
        );

        if (body?.Type is { } bodyType && operation.RequestBody?.Content is { } bodyContent)
        {
            var use = body.ParameterDescriptor is IParameterInfoParameterDescriptor { ParameterInfo: { } parameter }
                ? TypeUse.From(nullability.Create(parameter)).Find(bodyType)
                : null;
            _VisitContent(walker, bodyContent, use ?? TypeUse.Oblivious(bodyType));
        }

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        if (_Walkers.TryGetValue(document, out var walker))
        {
            walker.RemoveUnreferencedReplacedComponents();
            _Walkers.Remove(document);
        }

        return Task.CompletedTask;
    }

    private static void _VisitContent(DocumentWalker walker, IDictionary<string, OpenApiMediaType> content, TypeUse use)
    {
        foreach (var media in content.Values)
        {
            if (media.Schema is { } schema)
            {
                media.Schema = walker.Visit(schema, use);
            }
        }
    }

    // The same method the native generator reads a response's own nullability from.
    private static MethodInfo? _GetMethod(ApiDescription description)
    {
        return description.ActionDescriptor is ControllerActionDescriptor controller
            ? controller.MethodInfo
            : description.ActionDescriptor.EndpointMetadata.OfType<MethodInfo>().FirstOrDefault();
    }

    private sealed class DocumentWalker
    {
        private const byte _Annotated = 2;

        private readonly OpenApiDocument _document;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly NullabilityInfoContext _nullability = new();

        // The component of each generic type as the generator published it, before this walker rewrote it.
        private readonly Dictionary<string, OpenApiSchema> _originals = new(StringComparer.Ordinal);

        // The component chosen for each (component id, use) pair, so every use resolves once.
        private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);
        private readonly HashSet<string> _building = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reenteredWhileBuilding = new(StringComparer.Ordinal);

        private readonly HashSet<string> _walked = new(StringComparer.Ordinal);
        private readonly HashSet<string> _variants = new(StringComparer.Ordinal);
        private readonly HashSet<string> _replaced = new(StringComparer.Ordinal);

        public DocumentWalker(OpenApiDocument document, IServiceProvider services)
        {
            _document = document;

            // The serializer options the generator builds schemas with, copied because reading type info locks them.
            var source =
                services.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? JsonSerializerOptions.Web;
            _jsonOptions = new JsonSerializerOptions(source);
            _jsonOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        }

        /// <summary>Resolves the schema of one use, and returns the node to put in its place.</summary>
        public IOpenApiSchema Visit(IOpenApiSchema node, TypeUse use)
        {
            switch (node)
            {
                case OpenApiSchemaReference { Reference.Id: { Length: > 0 } id } reference:
                {
                    var target = _ResolveComponent(id, use);

                    if (string.Equals(target, id, StringComparison.Ordinal))
                    {
                        return reference;
                    }

                    _replaced.Add(id);

                    return new OpenApiSchemaReference(target, _document)
                    {
                        Description = reference.Description,
                        Default = reference.Default,
                        Examples = reference.Examples,
                        ReadOnly = reference.ReadOnly,
                        WriteOnly = reference.WriteOnly,
                    };
                }
                case OpenApiSchema schema:
                    _VisitInline(schema, use);

                    return schema;
                default:
                    return node;
            }
        }

        /// <summary>
        /// Removes each component whose references all moved to a variant. A component that is still referenced, from a
        /// use read as non-nullable or from anything this walker did not visit, stays.
        /// </summary>
        public void RemoveUnreferencedReplacedComponents()
        {
            if (_document.Components?.Schemas is not { } schemas)
            {
                return;
            }

            var candidates = _replaced
                .Where(schemas.ContainsKey)
                .ToDictionary(id => id, id => schemas[id], StringComparer.Ordinal);

            // A removed component may be the only one referring to another candidate, so repeat until nothing changes.
            while (candidates.Count > 0)
            {
                foreach (var id in candidates.Keys)
                {
                    schemas.Remove(id);
                }

                var json = _Serialize(_document);
                var restored = candidates
                    .Where(candidate =>
                        json.Contains($"\"#/components/schemas/{candidate.Key}\"", StringComparison.Ordinal)
                    )
                    .ToList();

                foreach (var (id, schema) in restored)
                {
                    schemas[id] = schema;
                    candidates.Remove(id);
                }

                if (restored.Count == 0)
                {
                    break;
                }
            }
        }

        private string _ResolveComponent(string id, TypeUse use)
        {
            if (
                _variants.Contains(id)
                || _document.Components?.Schemas?.TryGetValue(id, out var found) != true
                || found is not OpenApiSchema component
            )
            {
                return id;
            }

            var typeInfo = _GetTypeInfo(use.Type);

            if (
                !use.Type.IsGenericType
                || Nullable.GetUnderlyingType(use.Type) is not null
                || typeInfo.Kind != JsonTypeInfoKind.Object
            )
            {
                // A non-generic component means the same at every use: walk it once, in place, for the generic types
                // its members use.
                if (_walked.Add(id))
                {
                    _VisitInline(component, use);
                }

                return id;
            }

            use = use.WithNullable(false);
            var key = id + "|" + use.Key;

            if (_resolved.TryGetValue(key, out var resolved))
            {
                if (_building.Contains(key))
                {
                    _reenteredWhileBuilding.Add(key);
                }

                return resolved;
            }

            if (!_originals.TryGetValue(id, out var original))
            {
                _originals[id] = original = (OpenApiSchema)_DeepClone(component);
            }

            var baseUse = use.WithoutNullableReferences();

            if (string.Equals(baseUse.Key, use.Key, StringComparison.Ordinal))
            {
                // The component the generator named for this type describes the use with non-nullable arguments.
                _resolved[key] = id;
                _building.Add(key);
                var rebuilt = _Build(original, use, typeInfo);
                _building.Remove(key);
                component.Properties = rebuilt.Properties;
                component.Required = rebuilt.Required;

                return id;
            }

            // Settle the base first, so a variant that comes out the same as it can reuse it.
            _ResolveComponent(id, baseUse);

            var variantId = _FreeId(use.SchemaId);
            _resolved[key] = variantId;
            _building.Add(key);
            var variant = _Build(original, use, typeInfo);
            _building.Remove(key);

            if (
                !_reenteredWhileBuilding.Contains(key)
                && string.Equals(_Serialize(variant), _Serialize(component), StringComparison.Ordinal)
            )
            {
                _resolved[key] = id;

                return id;
            }

            _document.AddComponent<IOpenApiSchema>(variantId, variant);
            _variants.Add(variantId);

            return variantId;
        }

        private OpenApiSchema _Build(OpenApiSchema original, TypeUse use, JsonTypeInfo typeInfo)
        {
            var schema = (OpenApiSchema)_DeepClone(original);
            _VisitInline(schema, use, typeInfo);

            return schema;
        }

        private void _VisitInline(OpenApiSchema schema, TypeUse use, JsonTypeInfo? typeInfo = null)
        {
            typeInfo ??= _GetTypeInfo(use.Type);

            // A nullable reference is oneOf [null, $ref], and both describe the same use.
            _VisitAll(schema.OneOf, use);
            _VisitAll(schema.AllOf, use);

            if (schema.AnyOf is { Count: > 0 } anyOf)
            {
                // The generator lists a polymorphic type's derived types in anyOf, in declaration order.
                var derived = typeInfo.PolymorphismOptions?.DerivedTypes;

                for (var i = 0; i < anyOf.Count; i++)
                {
                    anyOf[i] = Visit(
                        anyOf[i],
                        derived is not null && i < derived.Count ? TypeUse.Oblivious(derived[i].DerivedType) : use
                    );
                }
            }

            if (typeInfo.ElementType is { } elementType)
            {
                var elementUse = use
                    .Arguments.Select(argument => argument.Find(elementType))
                    .FirstOrDefault(found => found is not null);
                elementUse ??= TypeUse.Oblivious(elementType);

                if (schema.Items is { } items)
                {
                    schema.Items = Visit(items, elementUse);
                }

                if (schema.AdditionalProperties is { } additionalProperties)
                {
                    schema.AdditionalProperties = Visit(additionalProperties, elementUse);
                }
            }

            if (typeInfo.Kind == JsonTypeInfoKind.Object && schema.Properties is { Count: > 0 } properties)
            {
                _VisitProperties(schema, properties, typeInfo, use);
            }
        }

        private void _VisitAll(IList<IOpenApiSchema>? schemas, TypeUse use)
        {
            if (schemas is null)
            {
                return;
            }

            for (var i = 0; i < schemas.Count; i++)
            {
                schemas[i] = Visit(schemas[i], use);
            }
        }

        private void _VisitProperties(
            OpenApiSchema schema,
            IDictionary<string, IOpenApiSchema> properties,
            JsonTypeInfo typeInfo,
            TypeUse use
        )
        {
            foreach (var property in typeInfo.Properties)
            {
                if (
                    !properties.TryGetValue(property.Name, out var node)
                    || property.AttributeProvider is not (PropertyInfo or FieldInfo)
                )
                {
                    continue;
                }

                var member = (MemberInfo)property.AttributeProvider;
                var (memberUse, argumentIsNullable) = _GetMemberUse(member, use);

                if (argumentIsNullable == false)
                {
                    // The member is the type argument itself, which this use declares non-nullable.
                    node = OpenApiSchemaNullability.RemoveNullability(node);
                    (schema.Required ??= new HashSet<string>(StringComparer.Ordinal)).Add(property.Name);
                }

                properties[property.Name] = Visit(node, memberUse);
            }
        }

        /// <summary>
        /// The use of a member's type inside the given use of its declaring type, and, for a member typed directly by a
        /// type parameter whose argument is a reference type, whether that argument is nullable.
        /// </summary>
        private (TypeUse Use, bool? ArgumentIsNullable) _GetMemberUse(MemberInfo member, TypeUse owner)
        {
            var declaring = member.DeclaringType;

            if (declaring is not { IsGenericType: true })
            {
                return (TypeUse.From(_CreateNullability(member)), null);
            }

            var declaringUse = _FindBaseUse(owner, declaring) ?? TypeUse.Oblivious(declaring);
            var definition = declaring.GetGenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(member);
            var declaredType = _MemberType(definition);

            if (declaredType.IsGenericParameter && declaredType.GenericParameterPosition < declaringUse.Arguments.Count)
            {
                var argument = declaringUse.Arguments[declaredType.GenericParameterPosition];

                // NullabilityInfoContext reports an unconstrained T as nullable whether it is declared T or T?, so the
                // declaration's own flag tells them apart.
                if (_GetDeclaredFlag(definition) == _Annotated)
                {
                    return (argument.WithNullable(true), null);
                }

                return argument.Type.IsValueType || argument.Type == typeof(object)
                    ? (argument, null)
                    : (argument, argument.IsNullable);
            }

            if (!declaredType.ContainsGenericParameters)
            {
                return (TypeUse.From(_CreateNullability(member)), null);
            }

            return (_Substitute(_MemberType(member), _CreateNullability(definition), declaringUse), null);
        }

        // The use of a member type built from the declaring type's parameters (Envelope<T>, T[]): the parameters take
        // the arguments of the declaring use, and every other node keeps the member's own annotation.
        private static TypeUse _Substitute(Type closed, NullabilityInfo open, TypeUse declaringUse)
        {
            if (open.Type.IsGenericParameter && open.Type.DeclaringMethod is null)
            {
                var position = open.Type.GenericParameterPosition;

                return position < declaringUse.Arguments.Count
                    ? declaringUse.Arguments[position]
                    : TypeUse.Oblivious(closed);
            }

            Type[] closedArguments =
                closed.IsArray ? [closed.GetElementType()!]
                : closed.IsGenericType ? closed.GetGenericArguments()
                : [];
            NullabilityInfo[] openArguments =
                open.Type.IsArray && open.ElementType is { } element ? [element] : open.GenericTypeArguments;

            var arguments =
                closedArguments.Length == openArguments.Length && Nullable.GetUnderlyingType(closed) is null
                    ? closedArguments.Select((argument, i) => _Substitute(argument, openArguments[i], declaringUse))
                    : closedArguments.Select(TypeUse.Oblivious);

            return TypeUse.Create(closed, open.ReadState == NullabilityState.Nullable, [.. arguments]);
        }

        // Follows the base types from the component's type to the type that declares a member, carrying the arguments.
        private static TypeUse? _FindBaseUse(TypeUse owner, Type declaring)
        {
            var current = owner;

            while (current.Type != declaring)
            {
                if (current.Type.BaseType is not { } baseType)
                {
                    return null;
                }

                current = current.Type.IsGenericType
                    ? _SubstituteType(baseType, current.Type.GetGenericTypeDefinition().BaseType!, current)
                    : TypeUse.Oblivious(baseType);
            }

            return current;
        }

        private static TypeUse _SubstituteType(Type closed, Type open, TypeUse declaringUse)
        {
            if (open.IsGenericParameter)
            {
                return open.GenericParameterPosition < declaringUse.Arguments.Count
                    ? declaringUse.Arguments[open.GenericParameterPosition]
                    : TypeUse.Oblivious(closed);
            }

            if (!closed.IsGenericType || !open.IsGenericType)
            {
                return TypeUse.Oblivious(closed);
            }

            var closedArguments = closed.GetGenericArguments();
            var openArguments = open.GetGenericArguments();

            return TypeUse.Create(
                closed,
                isNullable: false,
                [.. closedArguments.Select((argument, i) => _SubstituteType(argument, openArguments[i], declaringUse))]
            );
        }

        private NullabilityInfo _CreateNullability(MemberInfo member)
        {
            return member is PropertyInfo property
                ? _nullability.Create(property)
                : _nullability.Create((FieldInfo)member);
        }

        private static Type _MemberType(MemberInfo member)
        {
            return member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        }

        // The compiler's nullable flag for the member's type: 1 for T, 2 for T?, 0 when oblivious. A member without its
        // own NullableAttribute takes the nearest NullableContextAttribute.
        private static byte _GetDeclaredFlag(MemberInfo member)
        {
            foreach (var attribute in member.CustomAttributes)
            {
                if (
                    string.Equals(
                        attribute.AttributeType.FullName,
                        "System.Runtime.CompilerServices.NullableAttribute",
                        StringComparison.Ordinal
                    )
                )
                {
                    // A type parameter is one node of the type, which the compiler always writes as a single byte.
                    return attribute.ConstructorArguments[0].Value is byte flag ? flag : (byte)0;
                }
            }

            for (MemberInfo? scope = member; scope is not null; scope = scope.DeclaringType)
            {
                foreach (var attribute in scope.CustomAttributes)
                {
                    if (
                        string.Equals(
                            attribute.AttributeType.FullName,
                            "System.Runtime.CompilerServices.NullableContextAttribute",
                            StringComparison.Ordinal
                        ) && attribute.ConstructorArguments[0].Value is byte flag
                    )
                    {
                        return flag;
                    }
                }
            }

            return 0;
        }

        // Every type reached here is one the generator already described with the same options, so this cannot fail
        // where the generator did not.
        private JsonTypeInfo _GetTypeInfo(Type type) => _jsonOptions.GetTypeInfo(type);

        private string _FreeId(string id)
        {
            var schemas = _document.Components?.Schemas;
            var candidate = id;

            // Every use resolves once, so an id already taken, even by another variant, belongs to a different type.
            for (var i = 2; schemas?.ContainsKey(candidate) == true; i++)
            {
                candidate = id + i.ToString(CultureInfo.InvariantCulture);
            }

            return candidate;
        }

        private static IOpenApiSchema _DeepClone(IOpenApiSchema schema)
        {
            if (schema is not OpenApiSchema concrete)
            {
                return schema.CreateShallowCopy();
            }

            var copy = (OpenApiSchema)concrete.CreateShallowCopy();
            copy.Properties = concrete.Properties?.ToDictionary(
                pair => pair.Key,
                pair => _DeepClone(pair.Value),
                StringComparer.Ordinal
            );
            copy.Required = concrete.Required is null
                ? null
                : new HashSet<string>(concrete.Required, StringComparer.Ordinal);
            copy.Items = concrete.Items is null ? null : _DeepClone(concrete.Items);
            copy.AdditionalProperties = concrete.AdditionalProperties is null
                ? null
                : _DeepClone(concrete.AdditionalProperties);
            copy.Not = concrete.Not is null ? null : _DeepClone(concrete.Not);
            copy.OneOf = concrete.OneOf?.Select(_DeepClone).ToList();
            copy.AnyOf = concrete.AnyOf?.Select(_DeepClone).ToList();
            copy.AllOf = concrete.AllOf?.Select(_DeepClone).ToList();

            return copy;
        }

        private static string _Serialize(IOpenApiSerializable element)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            element.SerializeAsV31(new OpenApiJsonWriter(writer));

            return writer.ToString();
        }
    }
}
