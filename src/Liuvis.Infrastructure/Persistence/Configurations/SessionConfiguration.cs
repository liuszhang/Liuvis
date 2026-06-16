using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Liuvis.Core.Entities;

namespace Liuvis.Infrastructure.Persistence.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(e => e.SessionId);
        builder.Property(e => e.SessionId).ValueGeneratedOnAdd();
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => e.UserId);
        builder.HasIndex(e => e.Status);
    }
}
