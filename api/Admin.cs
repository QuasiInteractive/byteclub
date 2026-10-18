using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace ByteClub.Api;

/// <summary>
/// Passwordless sign-in for Nick: a 6-digit code is pushed to his phone through the same private
/// ntfy topic as bookings. Whoever can read that topic is Nick. One code at a time, 10 minutes,
/// single use, 5 wrong guesses and it's burnt, and at most 5 codes an hour.
/// </summary>
public sealed class AdminLogin(TimeProvider clock)
{
    private readonly object _gate = new();
    private string? _code;
    private DateTimeOffset _expires;
    private int _wrong;
    private readonly Queue<DateTimeOffset> _issued = new();

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxWrongGuesses = 5;
    public const int MaxCodesPerHour = 5;

    /// <summary>A new code, or null when too many were asked for in the last hour.</summary>
    public string? Issue()
    {
        lock (_gate)
        {
            var now = clock.GetUtcNow();
            while (_issued.Count > 0 && now - _issued.Peek() > TimeSpan.FromHours(1)) _issued.Dequeue();
            if (_issued.Count >= MaxCodesPerHour) return null;
            _issued.Enqueue(now);
            _code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _expires = now + Lifetime;
            _wrong = 0;
            return _code;
        }
    }

    public bool Redeem(string? attempt)
    {
        lock (_gate)
        {
            if (_code is null || clock.GetUtcNow() > _expires) { _code = null; return false; }
            var ok = attempt is { Length: 6 } &&
                     CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(attempt),
                                                             System.Text.Encoding.ASCII.GetBytes(_code));
            if (ok) { _code = null; return true; }
            if (++_wrong >= MaxWrongGuesses) _code = null; // burnt: ask for a new one
            return false;
        }
    }
}

public sealed record CodeAttempt(string Code);
public sealed record StatusChange(string Status);

public static class AdminEndpoints
{
    public static void MapAdmin(this WebApplication app)
    {
        var auth = app.MapGroup("/api/admin");

        auth.MapPost("/code", async (AdminLogin login, IBookingNotifier notify) =>
        {
            var code = login.Issue();
            if (code is null) return Results.Json(new { error = "Too many codes this hour. Try again later." }, statusCode: 429);
            await notify.SendAsync(new NtfyMessage($"Byte Club sign-in code: {code}",
                "Expires in 10 minutes. If you didn't ask for this, ignore it.", WhatsAppLink: "", Tags: "key"));
            return Results.NoContent();
        });

        auth.MapPost("/session", async (CodeAttempt body, AdminLogin login, HttpContext http) =>
        {
            if (!login.Redeem(body.Code?.Trim()))
                return Results.Json(new { error = "That code didn't work. Check it, or send a new one." }, statusCode: 401);
            var id = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Nick"), new Claim(ClaimTypes.Role, "admin")],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id),
                new AuthenticationProperties { IsPersistent = true });
            return Results.NoContent();
        });

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        var admin = app.MapGroup("/api/admin").RequireAuthorization();

        admin.MapGet("/me", () => Results.Ok(new { name = "Nick" }));

        admin.MapGet("/bookings", async (ByteClubDb db) =>
        {
            var all = await db.Bookings.AsNoTracking().ToListAsync();
            return all.OrderBy(b => b.Status is BookingStatus.Done or BookingStatus.Cancelled) // live ones first
                      .ThenBy(b => b.Start)
                      .Select(b => new
                      {
                          b.Id, b.ServiceId, service = Services.Find(b.ServiceId)?.Name ?? b.ServiceId, b.Start, b.Name,
                          b.Phone, b.Email, b.Suburb, b.Address, b.Problem, status = b.Status.ToString(), b.CreatedAt,
                          whatsApp = NtfyMessage.WhatsApp(b.Phone, $"Hi {b.Name.Split(' ')[0]}, it's Nick from Byte Club about your booking."),
                      });
        });

        admin.MapPatch("/bookings/{id:guid}", async (Guid id, StatusChange body, ByteClubDb db) =>
        {
            if (!Enum.TryParse<BookingStatus>(body.Status, out var status)) return Results.BadRequest(new { error = "Unknown status." });
            var b = await db.Bookings.FindAsync(id);
            if (b is null) return Results.NotFound();
            b.Status = status;
            // a cancelled booking gives its time back
            b.ActiveSlot = status == BookingStatus.Cancelled ? null : b.Start;
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Someone else has booked that time since. It can't be restored." }); }
            return Results.NoContent();
        });

        admin.MapGet("/enquiries", async (ByteClubDb db) =>
        {
            var all = await db.Enquiries.AsNoTracking().ToListAsync();
            return all.OrderBy(e => e.Status is EnquiryStatus.Won or EnquiryStatus.Lost)
                      .ThenByDescending(e => e.CreatedAt)
                      .Select(e => new
                      {
                          e.Id, e.ServiceId, service = Services.Find(e.ServiceId)?.Name ?? e.ServiceId, e.Name, e.Phone, e.Email,
                          e.Idea, status = e.Status.ToString(), e.CreatedAt,
                          whatsApp = NtfyMessage.WhatsApp(e.Phone, $"Hi {e.Name.Split(' ')[0]}, it's Nick from Byte Club about your idea."),
                      });
        });

        admin.MapPatch("/enquiries/{id:guid}", async (Guid id, StatusChange body, ByteClubDb db) =>
        {
            if (!Enum.TryParse<EnquiryStatus>(body.Status, out var status)) return Results.BadRequest(new { error = "Unknown status." });
            var e = await db.Enquiries.FindAsync(id);
            if (e is null) return Results.NotFound();
            e.Status = status;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // The bestiary: every customer, matched by phone number, with everything they've booked or asked for.
        admin.MapGet("/customers", async (ByteClubDb db) =>
        {
            var bookings = await db.Bookings.AsNoTracking().ToListAsync();
            var enquiries = await db.Enquiries.AsNoTracking().ToListAsync();
            var entries = bookings.Select(b => (Key: PhoneKey(b.Phone), b.Name, b.Phone, Suburb: (string?)b.Suburb, When: b.Start,
                            What: Services.Find(b.ServiceId)?.Name ?? b.ServiceId, Status: b.Status.ToString(), Kind: "visit"))
                .Concat(enquiries.Select(e => (Key: PhoneKey(e.Phone), e.Name, e.Phone, Suburb: (string?)null, When: e.CreatedAt,
                            What: Services.Find(e.ServiceId)?.Name ?? e.ServiceId, Status: e.Status.ToString(), Kind: "idea")));
            return entries.GroupBy(x => x.Key)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.When).First();
                    return new
                    {
                        name = latest.Name, phone = latest.Phone,
                        suburb = g.Select(x => x.Suburb).LastOrDefault(s => !string.IsNullOrEmpty(s)),
                        visits = g.Count(x => x.Kind == "visit" && x.Status == nameof(BookingStatus.Done)),
                        lastSeen = latest.When,
                        history = g.OrderByDescending(x => x.When).Select(x => new { x.When, x.What, x.Status, x.Kind }),
                        whatsApp = NtfyMessage.WhatsApp(latest.Phone, $"Hi {latest.Name.Split(' ')[0]}, it's Nick from Byte Club."),
                    };
                })
                .OrderByDescending(c => c.lastSeen);
        });
    }

    /// <summary>Same person whether they typed 0412 345 678 or +61 412 345 678.</summary>
    public static string PhoneKey(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.StartsWith("61") ? "0" + digits[2..] : digits;
    }
}
