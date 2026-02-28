using Microsoft.EntityFrameworkCore;
using Onlyspans.Processes.Api.Data.Entities;

namespace Onlyspans.Processes.Api.Data.Contexts;

public class ProcessesDbContext(DbContextOptions<ProcessesDbContext> options)
    : DbContext(options)
{
    public DbSet<DeploymentProcess> Processes => Set<DeploymentProcess>();
    public DbSet<ProcessStep> Steps => Set<ProcessStep>();
    public DbSet<ProcessVariable> Variables => Set<ProcessVariable>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProcessesDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
