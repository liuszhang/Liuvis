using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Liuvis.Core.Entities;

namespace Liuvis.Infrastructure.Persistence.Configurations;

public class KnowledgeEntryConfiguration : IEntityTypeConfiguration<KnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<KnowledgeEntry> builder)
    {
        builder.ToTable("knowledge_entries");
        builder.HasKey(e => e.EntryId);
        builder.Property(e => e.EntryId).ValueGeneratedOnAdd();
        builder.Property(e => e.Category).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Description).HasMaxLength(4096);
        builder.Property(e => e.Embedding)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<float[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<float>())
            .Metadata.SetValueComparer(new ValueComparer<float[]>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                v => v == null ? null! : v.ToArray()));
        builder.Property(e => e.Tags)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                v => v == null ? null! : v.ToList()));
        builder.HasIndex(e => e.Category);
    }
}
