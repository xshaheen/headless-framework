// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination.SqlServer;

/// <summary>SQL Server physical identifiers for coordination membership.</summary>
internal static class SqlServerMembershipSchema
{
    public const string ClusterName = "ClusterName";
    public const string NodeId = "NodeId";
    public const string Incarnation = "Incarnation";
    public const string CreatedAt = "CreatedAt";
    public const string UpdatedAt = "UpdatedAt";

    public static class Descriptor
    {
        public const string Table = "CoordinationDescriptor";
        public const string HostName = "HostName";
        public const string Endpoints = "Endpoints";
        public const string Role = "Role";
        public const string Metadata = "Metadata";
    }

    public static class Liveness
    {
        public const string Table = "CoordinationLiveness";
        public const string LastBeat = "LastBeat";
        public const string LeftAt = "LeftAt";
    }

    public static class Generation
    {
        public const string Table = "CoordinationNodeGeneration";
        public const string CurrentIncarnation = "CurrentIncarnation";
    }
}
