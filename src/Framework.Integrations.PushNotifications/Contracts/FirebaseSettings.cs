#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Integrations.PushNotifications;

public sealed class FirebaseSettings
{
    public required bool Enabled { get; init; }

    public required string Json { get; init; }
}
