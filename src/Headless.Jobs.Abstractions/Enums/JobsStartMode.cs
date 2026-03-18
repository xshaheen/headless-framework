namespace Headless.Jobs.Enums;

public enum JobsStartMode
{
    /// <summary>
    /// Start job processing immediately on application startup.
    /// </summary>
    Immediate,

    /// <summary>
    /// Background services are registered but skip the first run.
    /// Job processing needs to be started manually via IJobsHostScheduler.
    /// </summary>
    Manual,
}
