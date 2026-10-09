using EvidenceChain.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvidenceChain.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/>; ids come from the seed, roles are stored by name.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table =>
            table.HasCheckConstraint("CK_Users_Role", "[Role] IN ('Investigador', 'Custodio', 'Supervisor')"));

        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId).ValueGeneratedNever();
        builder.Property(u => u.UserName).HasMaxLength(64).IsUnicode(false);
        builder.Property(u => u.DisplayName).HasMaxLength(100);
        builder.Property(u => u.Email).HasMaxLength(254).IsUnicode(false);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(16).IsUnicode(false);

        builder.HasIndex(u => u.UserName).IsUnique().HasDatabaseName("UX_Users_UserName");
        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UX_Users_Email");
    }
}
