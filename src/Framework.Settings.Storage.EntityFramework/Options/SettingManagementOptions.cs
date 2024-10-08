using Framework.Kernel.Primitives;
using Framework.Settings.Repositories;

namespace Framework.Settings.Options;

public class SettingManagementOptions
{
    public TypeList<ISettingManagementProvider> Providers { get; } = [];

    /// <summary>
    /// Default: true.
    /// </summary>
    public bool SaveStaticSettingsToDatabase { get; set; } = true;
}
