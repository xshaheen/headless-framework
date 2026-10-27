// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using FluentValidation.Validators;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Logging;

namespace Headless.OpenApi;

/// <summary>
/// One part of a form body that MVC binds into a property of a <c>[FromForm]</c> model: the FluentValidation rules that
/// constrain it and whether it is always sent.
/// </summary>
/// <remarks>
/// MVC describes each leaf property of a form model as its own parameter, named by its path (<c>Avatar</c>,
/// <c>Name.First</c>), and the generator lists them as the properties of one inline multipart schema. No schema
/// transformer sees that schema, so the rules a JSON body gets from its component are read here instead: from the
/// model's validator under the part's full path (<c>RuleFor(x =&gt; x.Name.First)</c>), and at each nested level from
/// the validator registered for that level's type or attached with <c>SetValidator</c>, following unconditional
/// <c>Include()</c> chains.
/// </remarks>
internal sealed class FormPart
{
    private FormPart(
        IReadOnlyList<(IPropertyValidator Validator, bool IsCollectionRule)> validators,
        string containerName,
        bool isNonNullable,
        bool hasOptionalAncestor
    )
    {
        Validators = validators;
        ContainerName = containerName;
        IsNonNullable = isNonNullable;
        HasOptionalAncestor = hasOptionalAncestor;
    }

    /// <summary>The unconditional property validators that constrain the part.</summary>
    public IReadOnlyList<(IPropertyValidator Validator, bool IsCollectionRule)> Validators { get; }

    /// <summary>The name of the type that declares the part's property, for logging.</summary>
    public string ContainerName { get; }

    /// <summary>Whether the part's property is a non-nullable type, which a JSON body publishes as required.</summary>
    public bool IsNonNullable { get; }

    /// <summary>
    /// Whether an object on the part's path may be absent, so the part is sent only when that object is: a flat form
    /// cannot say that, so such a part is never listed as required.
    /// </summary>
    public bool HasOptionalAncestor { get; }

    /// <summary>
    /// Describes the form part <paramref name="parameter"/> binds, or returns <see langword="null"/> when it is not a
    /// property of a form model.
    /// </summary>
    public static FormPart? Resolve(ApiParameterDescription parameter, IServiceProvider services, ILogger logger)
    {
        if (
            (parameter.Source != BindingSource.Form && parameter.Source != BindingSource.FormFile)
            || parameter.ModelMetadata
                is not {
                    MetadataKind: ModelMetadataKind.Property,
                    ContainerType: { } containerType,
                    PropertyName: { } propertyName
                }
            || _FindProperty(containerType, propertyName) is not { } leaf
        )
        {
            return null;
        }

        var nullability = new NullabilityInfoContext();
        var rootType = parameter.ParameterDescriptor?.ParameterType;
        var path = rootType is null ? null : _ResolvePath(rootType, parameter.Name, leaf);

        if (path is null)
        {
            // A renamed part or an unknown root: only the declaring type's own validator can be matched to it.
            var validators = services.GetFluentValidator(containerType, logger) is { } container
                ? _Expand(container).SelectMany(v => v.GetValidatorsByPropertyNameIgnoreCase(propertyName)).ToList()
                : [];

            return new FormPart(
                validators,
                containerType.Name,
                _IsNonNullable(leaf, nullability),
                hasOptionalAncestor: false
            );
        }

        // The validators that apply at each depth of the path: the root model's first.
        var levels = new List<IReadOnlyList<IValidator>>(path.Count)
        {
            services.GetFluentValidator(rootType!, logger) is { } root ? _Expand(root) : [],
        };

        for (var i = 0; i < path.Count - 1; i++)
        {
            var next = new List<IValidator>();

            if (services.GetFluentValidator(path[i].PropertyType, logger) is { } registered)
            {
                next.AddRange(_Expand(registered));
            }

            foreach (var validator in levels[i])
            {
                foreach (var child in validator.GetChildValidators(path[i].Name))
                {
                    next.AddRange(_Expand(child));
                }
            }

            levels.Add(next);
        }

        var hasOptionalAncestor = false;

        for (var i = 0; i < path.Count - 1; i++)
        {
            var required =
                _IsNonNullable(path[i], nullability)
                || _RulesFor(levels, path, i)
                    .Exists(rule =>
                        !rule.IsCollectionRule && rule.Validator is INotNullValidator or INotEmptyValidator
                    );

            hasOptionalAncestor |= !required;
        }

        return new FormPart(
            _RulesFor(levels, path, path.Count - 1),
            containerType.Name,
            _IsNonNullable(leaf, nullability),
            hasOptionalAncestor
        );
    }

    // Every rule on path[0..end]: a validator at depth k names the segment by its path from k.
    private static List<(IPropertyValidator Validator, bool IsCollectionRule)> _RulesFor(
        List<IReadOnlyList<IValidator>> levels,
        List<PropertyInfo> path,
        int end
    )
    {
        var rules = new List<(IPropertyValidator, bool)>();

        for (var depth = 0; depth <= end; depth++)
        {
            var name = string.Join('.', path.Skip(depth).Take(end - depth + 1).Select(property => property.Name));

            foreach (var validator in levels[depth])
            {
                rules.AddRange(validator.GetValidatorsByPropertyNameIgnoreCase(name));
            }
        }

        return rules;
    }

    // Maps the part name onto the root model's properties. API Explorer names a part by its property path from the
    // model, without the model's own binding name; a renamed property matches no path.
    private static List<PropertyInfo>? _ResolvePath(Type rootType, string name, PropertyInfo leaf)
    {
        var path = new List<PropertyInfo>();
        var type = rootType;

        foreach (var segment in name.Split('.'))
        {
            if (_FindProperty(type, segment) is not { } property)
            {
                return null;
            }

            path.Add(property);
            type = property.PropertyType;
        }

        return
            string.Equals(path[^1].Name, leaf.Name, StringComparison.Ordinal)
            && path[^1].DeclaringType == leaf.DeclaringType
            ? path
            : null;
    }

    private static PropertyInfo? _FindProperty(Type type, string name)
    {
        return Array.Find(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
        );
    }

    // A JSON body lists every non-nullable member as required, an oblivious one included, so a form part does too.
    private static bool _IsNonNullable(PropertyInfo property, NullabilityInfoContext nullability)
    {
        return property.PropertyType.IsValueType
            ? Nullable.GetUnderlyingType(property.PropertyType) is null
            : nullability.Create(property).WriteState != NullabilityState.Nullable;
    }

    private static List<IValidator> _Expand(IValidator validator)
    {
        var validators = new List<IValidator> { validator };
        var seen = new HashSet<IValidator>(ReferenceEqualityComparer.Instance) { validator };

        // Breadth-first, and each validator once, so an include cycle ends.
        for (var i = 0; i < validators.Count; i++)
        {
            foreach (var included in validators[i].GetChildValidators(propertyName: null))
            {
                if (seen.Add(included))
                {
                    validators.Add(included);
                }
            }
        }

        return validators;
    }
}
