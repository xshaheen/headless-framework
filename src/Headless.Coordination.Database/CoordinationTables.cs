// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;

namespace Headless.Coordination;

/// <summary>
/// Provides table and column names for relational coordination storage, formatted per dialect.
/// </summary>
internal sealed class CoordinationTables
{
    public const int ClusterNameMaxLength = CoordinationOptions.ClusterNameMaxLength;
    public const int NodeIdMaxLength = Headless.Coordination.NodeId.MaxLength;
    public const int RoleMaxLength = 200;

    public CoordinationTables(ISqlDialect dialect, string schema)
    {
        GenerationTableName = dialect.Name("CoordinationNodeGeneration");
        DescriptorTableName = dialect.Name("CoordinationDescriptor");
        LivenessTableName = dialect.Name("CoordinationLiveness");
        Generation = dialect.Qualify(schema, GenerationTableName);
        Descriptor = dialect.Qualify(schema, DescriptorTableName);
        Liveness = dialect.Qualify(schema, LivenessTableName);

        string column(string pascal) => dialect.Quote(dialect.Name(pascal));

        ClusterName = column("ClusterName");
        NodeId = column("NodeId");
        Incarnation = column("Incarnation");
        CurrentIncarnation = column("CurrentIncarnation");
        CreatedAt = column("CreatedAt");
        UpdatedAt = column("UpdatedAt");
        HostName = column("HostName");
        Endpoints = column("Endpoints");
        Role = column("Role");
        Metadata = column("Metadata");
        LastBeat = column("LastBeat");
        LeftAt = column("LeftAt");

        NodeKey = [new(ClusterName, "ClusterName"), new(NodeId, "NodeId")];
        IncarnationKey = [new(ClusterName, "ClusterName"), new(NodeId, "NodeId"), new(Incarnation, "Incarnation")];
    }

    public string GenerationTableName { get; }

    public string DescriptorTableName { get; }

    public string LivenessTableName { get; }

    /// <summary>Gets the quoted, qualified generation table name with one permanent row per node identifier.</summary>
    public string Generation { get; }

    /// <summary>Gets the quoted, qualified descriptor table name with one write-once row per incarnation.</summary>
    public string Descriptor { get; }

    /// <summary>Gets the quoted, qualified liveness table name with one row per incarnation, pruned after retention expires.</summary>
    public string Liveness { get; }

    public string ClusterName { get; }

    public string NodeId { get; }

    public string Incarnation { get; }

    public string CurrentIncarnation { get; }

    public string CreatedAt { get; }

    public string UpdatedAt { get; }

    public string HostName { get; }

    public string Endpoints { get; }

    public string Role { get; }

    public string Metadata { get; }

    public string LastBeat { get; }

    public string LeftAt { get; }

    /// <summary>Gets the key columns for the generation row.</summary>
    public IReadOnlyList<SqlKeyColumn> NodeKey { get; }

    /// <summary>Gets the key columns for descriptor and liveness rows.</summary>
    public IReadOnlyList<SqlKeyColumn> IncarnationKey { get; }
}
