namespace PADLOck.Models;

public enum PackageState { Installed, Disabled, Uninstalled }

public enum PackageCategory { Remove, Optional, Unknown, Keep, Critical }

public enum PackageAction { Remove, Disable, Restore }

public sealed record CatalogEntry(string Title, string Description, PackageCategory Category, bool RequiresKioskHome)
{
    public static CatalogEntry Unknown { get; } = new("", "Нет описания", PackageCategory.Unknown, false);
}

public sealed class PackageRow
{
    public PackageRow(string name, CatalogEntry info, PackageState state)
    {
        Name = name;
        Info = info;
        State = state;
    }

    public string Name { get; }
    public CatalogEntry Info { get; }
    public PackageState State { get; }
    public bool IsChecked { get; set; }
    public bool IsLocked => Info.Category == PackageCategory.Critical;
}
