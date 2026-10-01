// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;

namespace Headless.Coordination;

/// <summary>
/// The membership tables and columns, named by the dialect: snake_case on PostgreSQL, PascalCase on SQL Server. They
/// are the names each provider's schema contribution creates and the names the one relational store queries.
/// </summary>
internal sealed class CoordinationTables
{
    public const int ClusterNameMaxLength = 200;
    public const int NodeIdMaxLength = 400;
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

    /// <summary>The quoted, qualified generation table: one row per node id, never purged.</summary>
    public string Generation { get; }

    /// <summary>The quoted, qualified descriptor table: one write-once row per incarnation.</summary>
    public string Descriptor { get; }

    /// <summary>The quoted, qualified liveness table: one row per incarnation, pruned after retention.</summary>
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

    /// <summary>The generation row's key.</summary>
    public IReadOnlyList<SqlKeyColumn> NodeKey { get; }

    /// <summary>The descriptor and liveness rows' key.</summary>
    public IReadOnlyList<SqlKeyColumn> IncarnationKey { get; }
}
