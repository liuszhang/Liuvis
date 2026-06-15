using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
                v => System.Text.Json.JsonSerializer.Deserialize<float[]>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? Array.Empty<float>());
        builder.Property(e => e.Tags)
            .HasColumnType("jsonb")
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());
        builder.HasIndex(e => e.Category);
    }
}
