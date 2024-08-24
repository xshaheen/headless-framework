using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Framework.Orm.EntityFramework.ChangeTrackers;

public sealed class HelperEntityEntry(string id, EntityEntry entityEntry)
{
    private bool _isModified;

    public string Id { get; } = id;

    public EntityEntry EntityEntry { get; } = entityEntry;

    public List<HelperNavigationEntry> NavigationEntries { get; } =
        entityEntry.Navigations.Select(x => new HelperNavigationEntry(x, x.Metadata.Name)).ToList();

    public bool IsModified
    {
        get
        {
            return _isModified || EntityEntry.State is EntityState.Modified || NavigationEntries.Any(n => n.IsModified);
        }
        set { _isModified = value; }
    }
}

public sealed class HelperNavigationEntry(NavigationEntry navigationEntry, string name)
{
    public NavigationEntry NavigationEntry { get; } = navigationEntry;

    public string Name { get; } = name;

    public bool IsModified { get; set; }
}
