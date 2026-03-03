namespace PatchNotes.Data;

public class WatchlistTemplate : IHasCreatedAt, IHasUpdatedAt
{
    public string Id { get; set; } = IdGenerator.NewId();
    public required string Name { get; set; }
    public required string Description { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<WatchlistTemplatePackage> TemplatePackages { get; set; } = [];
}

public class WatchlistTemplatePackage
{
    public string Id { get; set; } = IdGenerator.NewId();
    public required string WatchlistTemplateId { get; set; }
    public required string PackageId { get; set; }
    public WatchlistTemplate WatchlistTemplate { get; set; } = null!;
    public Package Package { get; set; } = null!;
}
