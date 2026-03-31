using LooseNotes.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Indexes for frequently queried fields
        builder.Entity<Note>()
            .HasIndex(n => n.OwnerId);

        builder.Entity<Note>()
            .HasIndex(n => n.IsPublic);

        builder.Entity<Note>()
            .HasIndex(n => n.CreatedAt);

        builder.Entity<Rating>()
            .HasIndex(r => new { r.NoteId, r.RaterId })
            .IsUnique(); // One rating per user per note

        builder.Entity<ShareLink>()
            .HasIndex(s => s.Token)
            .IsUnique();

        builder.Entity<AuditLog>()
            .HasIndex(a => a.UserId);

        builder.Entity<AuditLog>()
            .HasIndex(a => a.OccurredAt);

        builder.Entity<PasswordResetToken>()
            .HasIndex(p => p.TokenHash);

        // Cascade delete: removing a note removes its children
        builder.Entity<Note>()
            .HasMany(n => n.Attachments)
            .WithOne(a => a.Note)
            .HasForeignKey(a => a.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Note>()
            .HasMany(n => n.Ratings)
            .WithOne(r => r.Note)
            .HasForeignKey(r => r.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Note>()
            .HasMany(n => n.ShareLinks)
            .WithOne(s => s.Note)
            .HasForeignKey(s => s.NoteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Preserve audit logs even if user is deleted (set null)
        builder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany(u => u.AuditLogs)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
