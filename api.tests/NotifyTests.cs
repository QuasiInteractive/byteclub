using ByteClub.Api;

namespace ByteClub.Api.Tests;

public class NotifyTests
{
    private static readonly TimeZoneInfo Brisbane = TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");

    private static Booking Booking(string phone = "0412 345 678", string? problem = "Very slow") => new()
    {
        ServiceId = "laptop", Name = "Mary Jones", Phone = phone, Suburb = "Southport", Address = "1 Example St",
        Problem = problem, Start = new DateTimeOffset(2025, 10, 21, 1, 0, 0, TimeSpan.Zero), // 11 am Gold Coast
    };

    [Fact]
    public void Message_has_the_quest_customer_local_time_and_place()
    {
        var m = NtfyMessage.For(Booking(), Services.Find("laptop")!, Brisbane);
        Assert.Equal("New quest: Laptop / PC repair", m.Title);
        var lines = m.Body.Split('\n');
        Assert.Equal("Mary Jones · Tue 21 Oct, 11:00am", lines[0]);
        Assert.Equal("1 Example St, Southport", lines[1]);
        Assert.Contains("\"Very slow\"", m.Body);
    }

    [Theory]
    [InlineData("0412 345 678", "61412345678")]     // Australian mobile
    [InlineData("+64 21 190 2813", "64211902813")]  // already international
    [InlineData("+61 467 696 556", "61467696556")]
    public void WhatsApp_link_uses_international_numbers(string phone, string expected) =>
        Assert.StartsWith($"https://wa.me/{expected}?text=Hi%20Mary", NtfyMessage.WhatsApp(phone, "Mary Jones"));
}
