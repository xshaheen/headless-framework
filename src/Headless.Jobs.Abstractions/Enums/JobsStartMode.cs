namespace Headless.Jobs.Enums;

public enum JobsStartMode
{
    /// <summary>
    /// Start job processing immediately when UseJobs is called.
    /// Background services are registered and start automatically.
    /// </summary>
    Immediate,

    /// <summary>
    /// Background services are registered but skip the first run.
    /// Job processing needs to be started manually via IJobsHostScheduler.
    /// </summary>
    Manual,
}
