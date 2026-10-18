namespace ByteClub.Api;

/// <summary>What a customer can book. Fixed in code: Nick changes it rarely and it ships with a deploy.</summary>
public sealed record Service(string Id, string Name, string Blurb, string Device, string Status, int DeviceHp);

public static class Services
{
    // DeviceHp is the "how bad is it" bar the booking page shows for the customer's device.
    public static readonly IReadOnlyList<Service> All =
    [
        new("laptop", "Laptop / PC repair", "Slow, won't start, broken screen, virus", "Laptop", "Slow, Virus?", 12),
        new("phone", "Phone fix", "Cracked screen, battery, setup", "Phone", "Cracked", 40),
        new("wifi", "Wi-Fi and printer", "No internet, printer won't print", "Router", "Offline", 0),
        new("scam", "Scam cleanup", "Clicked a dodgy link, remote access, fake calls", "Computer", "Cursed", 5),
        new("setup", "New device setup", "Transfer your stuff, email, backups", "New device", "Unequipped", 100),
        new("other", "Something else", "Tell Nick what's going on", "Mystery device", "Unknown", 50),
    ];

    public static Service? Find(string id) => All.FirstOrDefault(s => s.Id == id);
}

public enum BookingStatus { Requested, Confirmed, Done, Cancelled }

public sealed class Booking
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ServiceId { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required string Name { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required string Suburb { get; init; }
    public required string Address { get; init; }
    public string? Problem { get; init; }
    public BookingStatus Status { get; set; } = BookingStatus.Requested;
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Unique among live bookings, so two people can't take the same slot.</summary>
    public DateTimeOffset? ActiveSlot { get; set; }
}

public sealed record BookingRequest(
    string ServiceId, DateTimeOffset Start, string Name, string Phone, string? Email,
    string Suburb, string Address, string? Problem);

public static class BookingValidation
{
    public static Dictionary<string, string> Validate(BookingRequest r)
    {
        var errors = new Dictionary<string, string>();
        if (Services.Find(r.ServiceId) is null) errors["serviceId"] = "Choose a quest.";
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80) errors["name"] = "Enter your name.";
        var digits = new string((r.Phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is < 8 or > 15) errors["phone"] = "Enter a phone number Nick can WhatsApp.";
        if (!string.IsNullOrWhiteSpace(r.Email) && !r.Email.Contains('@')) errors["email"] = "That email doesn't look right.";
        if (string.IsNullOrWhiteSpace(r.Suburb) || r.Suburb.Length > 60) errors["suburb"] = "Enter your suburb.";
        if (string.IsNullOrWhiteSpace(r.Address) || r.Address.Length > 160) errors["address"] = "Enter the address for the visit.";
        if (r.Problem is { Length: > 1000 }) errors["problem"] = "Keep it under 1000 characters.";
        return errors;
    }
}
