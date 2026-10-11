using EvidenceChain.Domain.Custody;
using EvidenceChain.Domain.Notifications;
using EvidenceChain.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EvidenceChain.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Notification"/>: a reader's list newest first by id, read from one index; the unread count from a
/// filtered index that holds only unread rows. The unique key backs up exactly-once delivery, which the write path's
/// replay returns provide. No trigger, so EF reads identities back in the same batch.
/// </summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        var kinds = string.Join(", ", Enum.GetNames<NotificationKind>().Select(k => $"'{k}'"));
        builder.ToTable("Notifications", table => table.HasCheckConstraint("CK_Notifications_Kind", $"[Kind] IN ({kinds})"));

        builder.HasKey(n => n.NotificationId);
        builder.Property(n => n.NotificationId).UseIdentityColumn();
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(32).IsUnicode(false);

        builder.HasOne<User>().WithMany().HasForeignKey(n => n.RecipientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustodyTransfer>().WithMany().HasForeignKey(n => n.TransferId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => new { n.RecipientId, n.TransferId, n.Kind }).IsUnique().HasDatabaseName("UX_Notifications_RecipientTransferKind");
        builder.HasIndex(n => new { n.RecipientId, n.NotificationId }).IsDescending(false, true).HasDatabaseName("IX_Notifications_Recipient")
            .IncludeProperties(n => new { n.TransferId, n.Kind, n.CreatedAtUtc, n.ReadAtUtc });
        builder.HasIndex(n => n.RecipientId).HasFilter("[ReadAtUtc] IS NULL").HasDatabaseName("IX_Notifications_Unread");
    }
}
