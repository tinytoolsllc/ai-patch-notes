using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PatchNotes.Data;

/// <summary>
/// SQLite context for local development.
/// Migrations: dotnet ef migrations add MigrationName --context SqliteContext --output-dir Migrations/Sqlite --startup-project ../PatchNotes.Api
/// </summary>
public class SqliteContext : PatchNotesDbContext
{
    public SqliteContext(DbContextOptions<SqliteContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQLite doesn't have a native DateTimeOffset type, so convert to/from UTC DateTime.
        // Only UTC offsets are supported — non-UTC values throw InvalidOperationException
        // on write rather than silently losing offset information.
        var dtoConverter = new ValueConverter<DateTimeOffset, DateTime>(
            v => ToUtcDateTime(v),
            v => new DateTimeOffset(v, TimeSpan.Zero));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dtoConverter);
                }
            }
        }
    }

    private static DateTime ToUtcDateTime(DateTimeOffset v)
    {
        if (v.Offset != TimeSpan.Zero)
            throw new InvalidOperationException(
                $"Non-UTC DateTimeOffset values are not supported in SQLite context " +
                $"(offset: {v.Offset}). Ensure all DateTimeOffset values use UTC before persisting.");
        return v.UtcDateTime;
    }
}

/// <summary>
/// SQL Server context for production.
/// Migrations: dotnet ef migrations add MigrationName --context SqlServerContext --output-dir Migrations/SqlServer --startup-project ../PatchNotes.Api
/// </summary>
public class SqlServerContext : PatchNotesDbContext
{
    public SqlServerContext(DbContextOptions<SqlServerContext> options) : base(options) { }
}

/// <summary>
/// Design-time factory for SQLite migrations.
/// </summary>
public class SqliteContextFactory : IDesignTimeDbContextFactory<SqliteContext>
{
    public SqliteContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SqliteContext>();
        optionsBuilder.UseSqlite("Data Source=patchnotes.db");
        return new SqliteContext(optionsBuilder.Options);
    }
}

/// <summary>
/// Design-time factory for SQL Server migrations.
/// Set ConnectionStrings__PatchNotes environment variable.
/// </summary>
public class SqlServerContextFactory : IDesignTimeDbContextFactory<SqlServerContext>
{
    public SqlServerContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PatchNotes") ??
            throw new InvalidOperationException(
                "Connection string not found. " +
                "Set ConnectionStrings__PatchNotes environment variable.");

        var optionsBuilder = new DbContextOptionsBuilder<SqlServerContext>();
        optionsBuilder.UseSqlServer(connectionString, options =>
            options.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null));

        return new SqlServerContext(optionsBuilder.Options);
    }
}
