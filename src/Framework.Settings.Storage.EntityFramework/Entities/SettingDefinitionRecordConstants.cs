namespace Framework.Settings.Entities;

public static class SettingDefinitionRecordConstants
{
    public static int MaxNameLength { get; set; } = 128;

    public static int MaxDisplayNameLength { get; set; } = 256;

    public static int MaxDescriptionLength { get; set; } = 512;

    public static int MaxDefaultValueLength { get; set; } = SettingRecordConstants.MaxValueLengthValue;

    public static int MaxProvidersLength { get; set; } = 1024;
}
