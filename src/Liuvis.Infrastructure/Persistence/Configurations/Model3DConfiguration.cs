using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Liuvis.Core.Entities;

namespace Liuvis.Infrastructure.Persistence.Configurations;

public class Model3DConfiguration : IEntityTypeConfiguration<Model3D>
{
    public void Configure(EntityTypeBuilder<Model3D> builder)
    {
        builder.ToTable("models");
        builder.HasKey(e => e.ModelId);
        builder.Property(e => e.ModelId).ValueGeneratedOnAdd();
        builder.Property(e => e.Name).IsRequired().HasMaxLength(512);
        builder.Property(e => e.Description).HasMaxLength(4096);
        builder.Property(e => e.Format).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.FilePath).HasMaxLength(2048);
        builder.Property(e => e.ThumbnailPath).HasMaxLength(2048);
        builder.Property(e => e.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());
        builder.Property(e => e.Tags)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());
        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.CreatedAt);
    }
}
