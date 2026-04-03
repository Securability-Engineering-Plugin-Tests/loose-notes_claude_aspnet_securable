using LooseNotes.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LooseNotes.Data;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Rating> Ratings => Set<Rating>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Note>(entity =>
        {
            entity.HasOne(n => n.Owner)
                  .WithMany(u => u.Notes)
                  .HasForeignKey(n => n.OwnerId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(n => n.OwnerId);
            entity.HasIndex(n => n.IsPublic);
            // Partial index so share lookups are fast
            entity.HasIndex(n => n.ShareToken)
                  .IsUnique()
                  .HasFilter("[ShareToken] IS NOT NULL");
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.HasOne(a => a.Note)
                  .WithMany(n => n.Attachments)
                  .HasForeignKey(a => a.NoteId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Rating>(entity =>
        {
            entity.HasOne(r => r.Note)
                  .WithMany(n => n.Ratings)
                  .HasForeignKey(r => r.NoteId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(r => r.Rater)
                  .WithMany(u => u.Ratings)
                  .HasForeignKey(r => r.RaterId)
                  .OnDelete(DeleteBehavior.Restrict);

            // One rating per user per note
            entity.HasIndex(r => new { r.NoteId, r.RaterId }).IsUnique();
        });
    }
}
