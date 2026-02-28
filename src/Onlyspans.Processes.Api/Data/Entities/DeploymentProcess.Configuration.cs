using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Onlyspans.Processes.Api.Domain.Enums;

namespace Onlyspans.Processes.Api.Data.Entities;

public class DeploymentProcessConfiguration : IEntityTypeConfiguration<DeploymentProcess>
{
    public void Configure(EntityTypeBuilder<DeploymentProcess> builder)
    {
        builder.ToTable("processes");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        builder.Property(p => p.EnvironmentId)
            .HasColumnName("environment_id")
            .IsRequired();

        builder.Property(p => p.ReleaseVersion)
            .HasColumnName("release_version")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.RawYaml)
            .HasColumnName("raw_yaml")
            .HasColumnType("text");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(p => p.CompletedAt)
            .HasColumnName("completed_at");

        builder.HasIndex(p => new { p.ProjectId, p.EnvironmentId, p.ReleaseVersion })
            .IsUnique();

        builder.HasMany(p => p.Steps)
            .WithOne(s => s.Process)
            .HasForeignKey(s => s.ProcessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Variables)
            .WithOne(v => v.Process)
            .HasForeignKey(v => v.ProcessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
