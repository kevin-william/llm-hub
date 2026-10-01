using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LlmHub.Infrastructure.Persistence;

public sealed class HubDbContextFactory : IDesignTimeDbContextFactory<HubDbContext>
{
    public HubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Hub")
            ?? "Host=localhost;Port=5432;Database=llmhub;Username=llmhub;Password=local-development-only";
        var options = new DbContextOptionsBuilder<HubDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new HubDbContext(options);
    }
}
