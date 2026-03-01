namespace PatchNotes.Data;

public class WatchlistTemplateOptions
{
    public const string SectionName = "WatchlistTemplates";
    public WatchlistTemplate[] Templates { get; set; } = [];
}

public class WatchlistTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Packages { get; set; } = [];
}
