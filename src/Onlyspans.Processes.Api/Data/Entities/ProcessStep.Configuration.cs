using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Processes.Api.Data.Entities;

public class ProcessStepConfiguration : IEntityTypeConfiguration<ProcessStep>
{
    public void Configure(EntityTypeBuilder<ProcessStep> builder)
    {
        builder.ToTable("steps");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.ProcessId)
            .HasColumnName("process_id")
            .IsRequired();

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(s => s.Order)
            .HasColumnName("order")
            .IsRequired();

        builder.Property(s => s.Description)
            .HasColumnName("description")
            .HasMaxLength(1024);

        builder.Property(s => s.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(s => s.Script)
            .HasColumnName("script")
            .HasColumnType("text");

        builder.Property(s => s.ScriptPath)
            .HasColumnName("script_path")
            .HasMaxLength(512);

        builder.Property(s => s.Optional)
            .HasColumnName("optional")
            .HasDefaultValue(false);

        builder.Property(s => s.Blocking)
            .HasColumnName("blocking")
            .HasDefaultValue(true);

        builder.Property(s => s.OnFailure)
            .HasColumnName("on_failure")
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(s => s.Timeout)
            .HasColumnName("timeout")
            .HasMaxLength(32);

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(s => new { s.ProcessId, s.Order })
            .IsUnique();
    }
}
