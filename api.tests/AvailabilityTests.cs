using ByteClub.Api;
using Microsoft.Extensions.Time.Testing;

namespace ByteClub.Api.Tests;

public class AvailabilityTests
{
    // Monday 20 Oct 2025, 8:00 am on the Gold Coast (UTC+10, no daylight saving)
    private static FakeTimeProvider MondayMorning() =>
        new(new DateTimeOffset(2025, 10, 19, 22, 0, 0, TimeSpan.Zero));

    private static Availability Make(FakeTimeProvider clock, AvailabilityOptions? o = null) =>
        new(o ?? new AvailabilityOptions(), clock);

    [Fact]
    public void Slots_are_in_gold_coast_time_and_respect_minimum_notice()
    {
        var slots = Make(MondayMorning()).OpenSlots([]);
        // 8 am now + 3 h notice: 9 am is too soon, 11 am is the first slot
        Assert.Equal(new DateTimeOffset(2025, 10, 20, 11, 0, 0, TimeSpan.FromHours(10)), slots[0].Start);
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromHours(10), s.Start.Offset));
    }

    [Fact]
    public void Sundays_are_closed_and_slots_end_by_close_of_day()
    {
        var slots = Make(MondayMorning()).OpenSlots([]);
        Assert.DoesNotContain(slots, s => s.Start.DayOfWeek == DayOfWeek.Sunday);
        Assert.All(slots, s => Assert.True(s.End.TimeOfDay <= new TimeSpan(17, 0, 0)));
        // 9-11, 11-1, 1-3, 3-5: four slots on a full day
        Assert.Equal(4, slots.Count(s => s.Start.Date == new DateTime(2025, 10, 21)));
    }

    [Fact]
    public void Taken_slots_are_not_offered()
    {
        var clock = MondayMorning();
        var taken = new DateTimeOffset(2025, 10, 21, 11, 0, 0, TimeSpan.FromHours(10));
        var av = Make(clock);
        Assert.DoesNotContain(av.OpenSlots([taken.ToUniversalTime()]), s => s.Start == taken);
        Assert.False(av.IsOpen(taken, [taken]));
        Assert.True(av.IsOpen(taken, []));
    }

    [Fact]
    public void Times_outside_the_offered_slots_are_not_open()
    {
        var av = Make(MondayMorning());
        Assert.False(av.IsOpen(new DateTimeOffset(2025, 10, 21, 10, 0, 0, TimeSpan.FromHours(10)), []));  // between slots
        Assert.False(av.IsOpen(new DateTimeOffset(2025, 10, 26, 11, 0, 0, TimeSpan.FromHours(10)), []));  // Sunday
        Assert.False(av.IsOpen(new DateTimeOffset(2025, 12, 1, 11, 0, 0, TimeSpan.FromHours(10)), []));   // beyond 14 days
    }
}

public class ValidationTests
{
    private static BookingRequest Good() => new("laptop", DateTimeOffset.UtcNow, "Mary Jones", "0412 345 678",
        null, "Southport", "1 Example St", "It's slow");

    [Fact]
    public void A_complete_request_is_valid() => Assert.Empty(BookingValidation.Validate(Good()));

    [Fact]
    public void Missing_or_bad_fields_are_reported_in_plain_words()
    {
        var e = BookingValidation.Validate(Good() with { ServiceId = "nope", Phone = "12", Address = " ", Email = "x" });
        Assert.Equal(["address", "email", "phone", "serviceId"], e.Keys.Order());
        Assert.Equal("Enter a phone number Nick can WhatsApp.", e["phone"]);
    }
}
