using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Liuvis.Core.Entities;

namespace Liuvis.Infrastructure.Persistence;

/// <summary>EF Core database context for Liuvis with pgvector support.</summary>
public class LiuvisDbContext : DbContext
{
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();
    public DbSet<Model3D> Models => Set<Model3D>();
    public DbSet<ModelComponent> ModelComponents => Set<ModelComponent>();
    public DbSet<KnowledgeEntry> KnowledgeEntries => Set<KnowledgeEntry>();
    public DbSet<DesignSnapshot> DesignSnapshots => Set<DesignSnapshot>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public LiuvisDbContext(DbContextOptions<LiuvisDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new Configurations.SessionConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Model3DConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.KnowledgeEntryConfiguration());

        // SessionMessage
        modelBuilder.Entity<SessionMessage>(entity =>
        {
            entity.ToTable("session_messages");
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.MessageId).ValueGeneratedOnAdd();
            entity.Property(e => e.Content).IsRequired().HasMaxLength(16384);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new())
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object>>(
                    (a, b) => (a == null && b == null) || (a != null && b != null && a.Count == b.Count && !a.Except(b).Any()),
                    v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(), kv.Value == null ? 0 : kv.Value.GetHashCode())),
                    v => v == null ? null! : new Dictionary<string, object>(v)));
            entity.HasOne<Session>().WithMany(s => s.Messages).HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        // ModelComponent
        modelBuilder.Entity<ModelComponent>(entity =>
        {
            entity.ToTable("model_components");
            entity.HasKey(e => e.ComponentId);
            entity.Property(e => e.ComponentId).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.GeometryType).HasMaxLength(64);
            entity.Property(e => e.Transform)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<Liuvis.Core.ValueObjects.Transform3D>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());
            entity.Property(e => e.Material)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<Liuvis.Core.ValueObjects.MaterialSpec>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new());
            entity.HasOne<Model3D>().WithMany(m => m.Components).HasForeignKey(e => e.ModelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Children).WithOne().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.NoAction);
        });

        // DesignSnapshot
        modelBuilder.Entity<DesignSnapshot>(entity =>
        {
            entity.ToTable("design_snapshots");
            entity.HasKey(e => e.SnapshotId);
            entity.Property(e => e.SnapshotId).ValueGeneratedOnAdd();
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.Spec)
                .HasColumnType("jsonb")
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => System.Text.Json.JsonSerializer.Deserialize<Liuvis.Core.ValueObjects.DesignSpec>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Liuvis.Core.ValueObjects.DesignSpec());
            entity.HasOne<Session>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        // AppSetting
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("app_settings");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(128);
            entity.Property(e => e.Value).HasMaxLength(4096);
            entity.Property(e => e.Description).HasMaxLength(256);
        });
    }
}
