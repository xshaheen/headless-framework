// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;

namespace Headless.Api.Surfaces;

/// <summary>Configuration consumed immediately by AddHeadlessApiSurfaces, not by the deferred options pipeline.</summary>
[PublicAPI]
public sealed class ApiSurfaceOptions
{
    private readonly Dictionary<string, ApiSurfaceBuilder> _surfaces = new(StringComparer.OrdinalIgnoreCase);

    public ApiSurfaceOptions AddSurface(string surfaceName, Action<ApiSurfaceBuilder>? configure = null)
    {
        Argument.IsNotNullOrWhiteSpace(surfaceName);
        var builder = new ApiSurfaceBuilder(surfaceName);
        if (!_surfaces.TryAdd(surfaceName, builder))
        {
            throw new InvalidOperationException($"API surface '{surfaceName}' is already configured.");
        }

        configure?.Invoke(builder);
        return this;
    }

    public IReadOnlyCollection<ApiSurfaceBuilder> Surfaces => _surfaces.Values;
}

internal sealed class ApiSurfaceOptionsValidator : AbstractValidator<ApiSurfaceOptions>
{
    public ApiSurfaceOptionsValidator()
    {
        RuleForEach(x => x.Surfaces)
            .ChildRules(surface =>
            {
                surface
                    .RuleFor(x => x.SurfaceName)
                    .Must(_IsSafeName)
                    .WithMessage(
                        "Surface names must contain only ASCII letters, digits, hyphens, underscores, or periods."
                    );
                surface
                    .RuleFor(x => x.SurfaceName)
                    .Must(name =>
                        !new[] { "unknown", "unclassified", "infrastructure" }.Contains(
                            name,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                    .WithMessage("The surface name is reserved for telemetry classification.");
                surface.RuleFor(x => x.TenancyMode).IsInEnum();
                surface
                    .RuleFor(x => x.OpenApi.DocumentName)
                    .Must(_IsSafeName)
                    .WithMessage(
                        "Document names must be safe URL segments containing ASCII letters, digits, hyphens, underscores, or periods."
                    );
                surface.RuleFor(x => x.OpenApi.Title).NotEmpty();
            });
        RuleFor(x => x.Surfaces)
            .Must(surfaces =>
                surfaces.Select(x => x.OpenApi.DocumentName).ToHashSet(StringComparer.OrdinalIgnoreCase).Count
                == surfaces.Count
            )
            .WithMessage("API surface document names must be unique (case-insensitive).");
    }

    private static bool _IsSafeName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name is not "." and not ".."
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
