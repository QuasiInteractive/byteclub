using Microsoft.EntityFrameworkCore;

namespace ByteClub.Api;

public sealed class ByteClubDb(DbContextOptions<ByteClubDb> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var booking = b.Entity<Booking>();
        booking.Property(x => x.Status).HasConversion<string>();
        // SQLite can't order or compare DateTimeOffset, so store UTC ticks.
        booking.Property(x => x.Start).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        booking.Property(x => x.CreatedAt).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        booking.Property(x => x.ActiveSlot).HasConversion(
            v => v.HasValue ? v.Value.UtcTicks : (long?)null,
            v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        // the database itself refuses a double booking, even if two requests race
        booking.HasIndex(x => x.ActiveSlot).IsUnique();
    }
}
