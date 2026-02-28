using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Onlyspans.Processes.Api.Data.Entities;

public class ProcessVariableConfiguration : IEntityTypeConfiguration<ProcessVariable>
{
    public void Configure(EntityTypeBuilder<ProcessVariable> builder)
    {
        builder.ToTable("variables");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(v => v.ProcessId)
            .HasColumnName("process_id")
            .IsRequired();

        builder.Property(v => v.Name)
            .HasColumnName("name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(v => v.Value)
            .HasColumnName("value")
            .HasMaxLength(4096);

        builder.Property(v => v.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(v => new { v.ProcessId, v.Name })
            .IsUnique();
    }
}
