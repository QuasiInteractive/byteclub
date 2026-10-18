namespace ByteClub.Api;

/// <summary>
/// What a customer can choose. Fixed in code: Nick changes it rarely and it ships with a deploy.
/// Kind "visit" is a home visit, booked into a time slot with an address. Kind "project" (make a
/// game, build an app) has no slot: the customer sends their idea as an enquiry and Nick picks
/// it up on WhatsApp, then quotes.
/// </summary>
public sealed record Service(string Id, string Name, string Blurb, string Device, string Status, int DeviceHp,
    string Kind = Service.Visit)
{
    public const string Visit = "visit";
    public const string Project = "project";
    public bool IsProject => Kind == Project;
}

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
        new("game", "Make a video game", "From idea to playable. Quoted after a chat", "Game idea", "Unbuilt", 100, Service.Project),
        new("app", "Build an app", "Apps, tools and AI. Quoted after a chat", "App idea", "Unbuilt", 100, Service.Project),
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

public enum EnquiryStatus { New, Talking, Quoted, Won, Lost }

/// <summary>A game or app idea sent to Nick. No time slot; he follows up on WhatsApp.</summary>
public sealed class Enquiry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string ServiceId { get; init; }
    public required string Name { get; init; }
    public required string Phone { get; init; }
    public string? Email { get; init; }
    public required string Idea { get; init; }
    public EnquiryStatus Status { get; set; } = EnquiryStatus.New;
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record BookingRequest(
    string ServiceId, DateTimeOffset Start, string Name, string Phone, string? Email,
    string Suburb, string Address, string? Problem);

public sealed record EnquiryRequest(string ServiceId, string Name, string Phone, string? Email, string Idea);

public static class BookingValidation
{
    public static Dictionary<string, string> Validate(BookingRequest r)
    {
        var errors = new Dictionary<string, string>();
        var service = Services.Find(r.ServiceId);
        if (service is null) errors["serviceId"] = "Choose a quest.";
        else if (service.IsProject) errors["serviceId"] = "Game and app quests are sent as an idea, not booked.";
        Contact(errors, r.Name, r.Phone, r.Email);
        if (string.IsNullOrWhiteSpace(r.Suburb) || r.Suburb.Length > 60) errors["suburb"] = "Enter your suburb.";
        if (string.IsNullOrWhiteSpace(r.Address) || r.Address.Length > 160) errors["address"] = "Enter the address for the visit.";
        if (r.Problem is { Length: > 1000 }) errors["problem"] = "Keep it under 1000 characters.";
        return errors;
    }

    public static Dictionary<string, string> Validate(EnquiryRequest r)
    {
        var errors = new Dictionary<string, string>();
        if (Services.Find(r.ServiceId) is not { IsProject: true }) errors["serviceId"] = "Choose a game or app quest.";
        Contact(errors, r.Name, r.Phone, r.Email);
        if (string.IsNullOrWhiteSpace(r.Idea) || r.Idea.Trim().Length < 10) errors["idea"] = "Tell Nick your idea in a sentence or two.";
        else if (r.Idea.Length > 2000) errors["idea"] = "Keep it under 2000 characters.";
        return errors;
    }

    private static void Contact(Dictionary<string, string> errors, string? name, string? phone, string? email)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) errors["name"] = "Enter your name.";
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length is < 8 or > 15) errors["phone"] = "Enter a phone number Nick can WhatsApp.";
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@')) errors["email"] = "That email doesn't look right.";
    }
}
