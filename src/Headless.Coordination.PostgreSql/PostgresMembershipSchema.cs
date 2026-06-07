// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination.PostgreSql;

/// <summary>PostgreSQL physical identifiers for coordination membership.</summary>
internal static class PostgresMembershipSchema
{
    public const string ClusterName = "cluster_name";
    public const string NodeId = "node_id";
    public const string Incarnation = "incarnation";
    public const string DateCreated = "date_created";
    public const string DateUpdated = "date_updated";

    public static class Descriptor
    {
        public const string Table = "coordination_descriptor";
        public const string HostName = "host_name";
        public const string Endpoints = "endpoints";
        public const string Role = "role";
        public const string Metadata = "metadata";
    }

    public static class Liveness
    {
        public const string Table = "coordination_liveness";
        public const string LastBeat = "last_beat";
        public const string LeftAt = "left_at";
    }

    public static class Generation
    {
        public const string Table = "coordination_node_generation";
        public const string CurrentIncarnation = "current_incarnation";
    }
}
