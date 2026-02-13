namespace Demo;

public sealed record PingRequest(string? Text);

public sealed record WorkItemRequest(string? WorkId, bool ShouldFail = false, int FailuresBeforeSuccess = 1);

public sealed record ScheduleOnceRequest(string? Name, int DelaySeconds, ScheduleOncePayload? Payload);

public sealed record ScheduleOncePayload(string? Note, bool ShouldFail, int FailuresBeforeSuccess);
