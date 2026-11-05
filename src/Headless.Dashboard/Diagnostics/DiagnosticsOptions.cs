// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Checks;

namespace Headless.Dashboard;

/// <summary>
/// Settings of one diagnostic module instance: its id, whether it serves outside Development, the store it reads, and
/// the view and operate policies every dashboard module carries.
/// </summary>
/// <remarks>
/// <see cref="Id"/>, <see cref="Enabled"/>, and <see cref="Store"/> decide what is registered, so
/// <c>AddDiagnostics</c> reads them once, when the host registers its services; changing them later has no effect.
/// The policies are read like any module's, through the options system.
/// </remarks>
[PublicAPI]
public sealed class DiagnosticsOptions : DashboardModuleOptions
{
    /// <summary>The id the diagnostic module serves under unless <see cref="Id"/> names another.</summary>
    public const string DefaultId = "diagnostics";

    /// <summary>
    /// Gets or sets the module instance's id: its URL segment, API group, and stream topic prefix. Defaults to
    /// <see cref="DefaultId"/>; give a second instance another id, such as <c>diag-b</c>.
    /// </summary>
    public string Id { get; set; } = DefaultId;

    /// <summary>
    /// Gets or sets a value indicating whether the module serves outside the Development environment. In Development
    /// it always serves; elsewhere it is registered only when this is <see langword="true"/>, so it is absent from the
    /// dashboard and its routes answer <c>404</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets the store the instance reads, or <see langword="null"/> for a new store of its own. Set it through
    /// <see cref="UseStore"/>.
    /// </summary>
    public DiagnosticsStore? Store { get; private set; }

    /// <summary>Serves this module instance under <paramref name="id"/> instead of <see cref="DefaultId"/>.</summary>
    /// <param name="id">The instance's id, a lower-case kebab token such as <c>diag-b</c>.</param>
    /// <returns>The same options for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <see langword="null"/>, empty, or white space.</exception>
    public DiagnosticsOptions Instance(string id)
    {
        Id = Argument.IsNotNullOrWhiteSpace(id);

        return this;
    }

    /// <summary>
    /// Reads and writes runs in <paramref name="store"/> instead of a store of the instance's own, so several hosts,
    /// such as two dashboard nodes in a test, share one store as a deployment shares one database.
    /// </summary>
    /// <param name="store">The shared store.</param>
    /// <returns>The same options for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public DiagnosticsOptions UseStore(DiagnosticsStore store)
    {
        Store = Argument.IsNotNull(store);

        return this;
    }
}

internal sealed class DiagnosticsOptionsValidator : AbstractValidator<DiagnosticsOptions>
{
    public DiagnosticsOptionsValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // A white-space policy name passes the shell's "empty means inherit" check but names no host policy, so
        // authorization would fail on every request instead of at startup.
        RuleFor(x => x.ViewPolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("ViewPolicy must name a host authorization policy, or be empty to use the shell's.");

        RuleFor(x => x.OperatePolicy)
            .Must(static policy => string.IsNullOrEmpty(policy) || !string.IsNullOrWhiteSpace(policy))
            .WithMessage("OperatePolicy must name a host authorization policy, or be empty to use the view policy.");
    }
}
