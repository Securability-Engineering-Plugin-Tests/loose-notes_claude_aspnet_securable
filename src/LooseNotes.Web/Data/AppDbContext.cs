using LooseNotes.Web.Data.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LooseNotes.Web.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<ShareToken> ShareTokens => Set<ShareToken>();
    public DbSet<SecurityQuestion> SecurityQuestions => Set<SecurityQuestion>();
    public DbSet<PasswordResetTicket> PasswordResetTickets => Set<PasswordResetTicket>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // SQLite cannot order by DateTimeOffset natively. Persist as ticks
        // (UTC) so chronological ordering remains correct without changing the
        // domain types. (FIASSE Modifiability — keep boundary quirks at the
        // boundary; domain types stay typed.)
        var dtoConverter = new ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));
        var dtoNullableConverter = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.UtcTicks : null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
            {
                if (prop.ClrType == typeof(DateTimeOffset)) prop.SetValueConverter(dtoConverter);
                else if (prop.ClrType == typeof(DateTimeOffset?)) prop.SetValueConverter(dtoNullableConverter);
            }
        }

        builder.Entity<Note>()
            .HasIndex(n => n.OwnerId);
        builder.Entity<Note>()
            .HasIndex(n => n.IsPublic);

        builder.Entity<Attachment>()
            .HasIndex(a => a.NoteId);
        builder.Entity<Attachment>()
            .HasIndex(a => a.StoredFileName)
            .IsUnique();

        builder.Entity<Rating>()
            .HasIndex(r => new { r.NoteId, r.SubmitterId })
            .IsUnique();

        builder.Entity<ShareToken>()
            .HasIndex(s => s.TokenHash)
            .IsUnique();

        builder.Entity<PasswordResetTicket>()
            .HasIndex(p => p.TicketIdHash)
            .IsUnique();

        builder.Entity<SecurityQuestion>().HasData(
            new SecurityQuestion { Id = "first-pet", Text = "What was the name of your first pet?" },
            new SecurityQuestion { Id = "first-school", Text = "What was the name of your first school?" },
            new SecurityQuestion { Id = "birth-city", Text = "In what city were you born?" },
            new SecurityQuestion { Id = "favorite-book", Text = "What is the title of your favorite book?" },
            new SecurityQuestion { Id = "mothers-maiden", Text = "What is your mother's maiden name?" }
        );
    }
}
