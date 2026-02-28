using Microsoft.EntityFrameworkCore;

namespace PatchNotes.Data;

public class PatchNotesDbContext : DbContext
{
    public PatchNotesDbContext(DbContextOptions<PatchNotesDbContext> options)
        : base(options)
    {
    }

    protected PatchNotesDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IHasCreatedAt>())
        {
            if (entry.State == EntityState.Added)
                entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<IHasUpdatedAt>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = now;
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Watchlist> Watchlists => Set<Watchlist>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<ReleaseSummary> ReleaseSummaries => Set<ReleaseSummary>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<SentDigestEmail> SentDigestEmails => Set<SentDigestEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Package>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.NpmName).HasMaxLength(256);
            entity.Property(e => e.GithubOwner).HasMaxLength(128);
            entity.Property(e => e.GithubRepo).HasMaxLength(128);
            entity.Property(e => e.TagPrefix).HasMaxLength(64);
            entity.HasIndex(e => e.NpmName).IsUnique().HasFilter("[NpmName] IS NOT NULL");
            entity.Property(e => e.ConsecutiveFailures).HasDefaultValue(0);
            entity.Property(e => e.LastFailureMessage).HasMaxLength(1024);
            entity.Property(e => e.IsSyncDisabled).HasDefaultValue(false);
        });

        modelBuilder.Entity<Release>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.PackageId).HasMaxLength(21);
            entity.Property(e => e.Tag).HasMaxLength(128);
            entity.Property(e => e.SummaryStale).HasDefaultValue(true);
            entity.HasIndex(e => e.PublishedAt);
            entity.HasIndex(e => new { e.PackageId, e.PublishedAt });
            entity.HasIndex(e => new { e.PackageId, e.Tag }).IsUnique();
            entity.HasIndex(e => new { e.PackageId, e.MajorVersion, e.IsPrerelease });
            entity.HasOne(e => e.Package)
                .WithMany(p => p.Releases)
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.StytchUserId).HasMaxLength(128);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.Name).HasMaxLength(256);
            entity.Property(e => e.StripeCustomerId).HasMaxLength(64);
            entity.Property(e => e.StripeSubscriptionId).HasMaxLength(64);
            entity.Property(e => e.SubscriptionStatus).HasMaxLength(32);
            entity.Property(e => e.EmailDigestEnabled).HasDefaultValue(false);
            entity.Property(e => e.EmailWelcomeSent).HasDefaultValue(false);
            entity.HasIndex(e => e.StytchUserId).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.StripeCustomerId);
        });

        modelBuilder.Entity<ProcessedWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
            entity.Property(e => e.EventId).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<ReleaseSummary>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.PackageId).HasMaxLength(21);
            entity.HasIndex(e => new { e.PackageId, e.MajorVersion, e.IsPrerelease }).IsUnique();
            entity.HasOne(e => e.Package)
                .WithMany(p => p.ReleaseSummaries)
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(128);
            entity.Property(e => e.Name).HasMaxLength(128);
            entity.Property(e => e.Subject).HasMaxLength(512);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<SentDigestEmail>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.UserId).HasMaxLength(21);
            entity.Property(e => e.Subject).HasMaxLength(512);
            entity.Property(e => e.RecipientEmail).HasMaxLength(256);
            entity.Property(e => e.Status).HasMaxLength(16);
            entity.Property(e => e.ResendEmailId).HasMaxLength(128);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1024);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.SentAt);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Watchlist>(entity =>
        {
            entity.Property(e => e.Id).HasMaxLength(21);
            entity.Property(e => e.UserId).HasMaxLength(21);
            entity.Property(e => e.PackageId).HasMaxLength(21);
            entity.HasIndex(e => new { e.UserId, e.PackageId }).IsUnique();
            entity.HasOne(e => e.User)
                .WithMany(u => u.Watchlists)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Package)
                .WithMany(p => p.Watchlists)
                .HasForeignKey(e => e.PackageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    }
}
