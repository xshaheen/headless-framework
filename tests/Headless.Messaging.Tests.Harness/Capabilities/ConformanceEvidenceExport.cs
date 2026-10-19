// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Headless.Checks;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Capabilities;

/// <summary>
/// Writes the conformance manifest, the guarantee catalogue, and each provider's scenario-to-test bindings as JSON for
/// the scheduled run's reports. The quarantine check reads the snapshot to find every issue a quarantined cell links;
/// the drift report joins the bindings with the run's TRX results to tell a simulator that drifted from the real
/// brokers apart from one provider failing. Nothing is written unless <see cref="DirectoryVariable"/> names a
/// directory, so ordinary test runs leave no files behind.
/// </summary>
[PublicAPI]
public static class ConformanceEvidenceExport
{
    /// <summary>The environment variable naming the directory the export writes into.</summary>
    public const string DirectoryVariable = "HEADLESS_CONFORMANCE_EXPORT_DIR";

    /// <summary>The snapshot's file name inside the export directory.</summary>
    public const string SnapshotFileName = "snapshot.json";

    /// <summary>The folder inside the export directory that holds one bindings file per provider and test assembly.</summary>
    public const string BindingsFolderName = "bindings";

    private static readonly JsonSerializerOptions _WriteOptions = new() { WriteIndented = true };

    // JSON readers outside .NET reject a byte-order mark, which Encoding.UTF8 writes.
    private static readonly UTF8Encoding _Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Records the bindings of a manifest profile, with the snapshot beside them, when the export is requested. A
    /// profile built by a test rather than taken from the manifest is ignored, because its cells describe no provider.
    /// </summary>
    public static void RecordBindings(
        TransportConformanceProfile profile,
        IReadOnlyCollection<TransportConformanceTestBinding> bindings
    )
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (
            !TransportConformanceManifest.Providers.TryGetValue(profile.Provider, out var registered)
            || !ReferenceEquals(registered, profile)
        )
        {
            return;
        }

        WriteSnapshot(directory);

        var assemblies = bindings
            .Select(binding => binding.TestClass.Assembly.GetName().Name ?? "unknown")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var document = new JsonObject
        {
            ["provider"] = profile.Provider,
            ["bindings"] = new JsonArray(
                bindings
                    .Select(binding =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["scenario"] = binding.Scenario.ToString(),
                                ["assembly"] = binding.TestClass.Assembly.GetName().Name,
                                ["testClass"] = binding.TestClass.FullName,
                                ["testMethod"] = binding.TestMethod,
                            }
                    )
                    .ToArray()
            ),
        };

        var name = $"{_Slug(string.Join('+', assemblies))}--{_Slug(profile.Provider)}.json";
        _WriteAtomically(Path.Combine(directory, BindingsFolderName, name), document);
    }

    /// <summary>Writes the manifest and guarantee catalogue snapshot into <paramref name="directory"/>.</summary>
    /// <returns>The path of the written snapshot.</returns>
    public static string WriteSnapshot(string directory)
    {
        Argument.IsNotNullOrWhiteSpace(directory);

        var path = Path.Combine(directory, SnapshotFileName);
        _WriteAtomically(path, CreateSnapshot());
        return path;
    }

    /// <summary>Builds the snapshot of every manifest cell and every catalogued guarantee.</summary>
    public static JsonObject CreateSnapshot()
    {
        var providers = new JsonArray();
        foreach (
            var profile in TransportConformanceManifest.Providers.Values.OrderBy(
                p => p.Provider,
                StringComparer.Ordinal
            )
        )
        {
            var cells = new JsonObject();
            foreach (
                var (scenario, support) in profile.Scenarios.OrderBy(
                    cell => cell.Key.ToString(),
                    StringComparer.Ordinal
                )
            )
            {
                cells[scenario.ToString()] = new JsonObject
                {
                    ["status"] = support.Status.ToString(),
                    ["rationale"] = support.Rationale,
                    ["issueUrl"] = support.IssueUrl,
                };
            }

            providers.Add(
                new JsonObject
                {
                    ["name"] = profile.Provider,
                    ["realBrokerLeaf"] = profile.IsRealBrokerLeafEnabled,
                    ["cells"] = cells,
                }
            );
        }

        var guarantees = new JsonArray();
        foreach (var guarantee in MessagingGuarantees.All)
        {
            guarantees.Add(
                new JsonObject
                {
                    ["id"] = guarantee.Id,
                    ["statement"] = guarantee.Statement,
                    ["status"] = guarantee.Status.ToString(),
                    ["rationale"] = guarantee.Rationale,
                    ["issueUrl"] = guarantee.IssueUrl,
                    ["manifestScenarios"] = new JsonArray(
                        guarantee.ManifestScenarios.Select(scenario => (JsonNode)scenario.ToString()).ToArray()
                    ),
                }
            );
        }

        return new JsonObject
        {
            ["simulator"] = SimulatedBroker.ProviderName,
            ["providers"] = providers,
            ["guarantees"] = guarantees,
        };
    }

    private static void _WriteAtomically(string path, JsonNode document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Evidence tests of one assembly run in parallel and each rewrites the snapshot, so a reader must never see a
        // half-written file: write beside it, then swap it in.
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, document.ToJsonString(_WriteOptions), _Utf8NoBom);
        File.Move(temporary, path, overwrite: true);
    }

    private static string _Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '+' ? character : '-');
        }

        return builder.ToString();
    }
}
