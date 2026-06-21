using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.Count == b.Count && !a.Except(b).Any()),
                v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(), kv.Value == null ? 0 : kv.Value.GetHashCode())),
                v => v == null ? null! : new Dictionary<string, string>(v)));
        builder.Property(e => e.Tags)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                v => v == null ? null! : v.ToList()));
        builder.HasIndex(e => e.Name);
        builder.HasIndex(e => e.CreatedAt);
    }
}
