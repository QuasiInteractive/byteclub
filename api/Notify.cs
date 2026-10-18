using System.Net.Http.Headers;
using System.Text;

namespace ByteClub.Api;

/// <summary>Tells Nick about a new booking. A failure here must never fail the booking itself.</summary>
public interface IBookingNotifier
{
    Task NotifyAsync(Booking booking, Service service, CancellationToken ct = default);
}

public sealed class NotifyOptions
{
    public string NtfyServer { get; set; } = "https://ntfy.sh";
    /// <summary>
    /// Long random topic name. Anyone who knows it can read the notifications (they carry the
    /// customer's name and suburb), so it lives in user-secrets / app settings, never in git.
    /// </summary>
    public string? NtfyTopic { get; set; }
    public string TimeZone { get; set; } = "Australia/Brisbane";
}

/// <summary>Push to Nick's phone through ntfy (free app). Tapping it opens WhatsApp to the customer.</summary>
public sealed class NtfyNotifier(HttpClient http, NotifyOptions options, ILogger<NtfyNotifier> log) : IBookingNotifier
{
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);

    public async Task NotifyAsync(Booking b, Service service, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.NtfyTopic))
        {
            log.LogWarning("Booking {Id} saved but no ntfy topic is configured, so nobody was notified", b.Id);
            return;
        }
        var msg = NtfyMessage.For(b, service, _tz);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{options.NtfyServer.TrimEnd('/')}/{options.NtfyTopic}")
        {
            Content = new StringContent(msg.Body, Encoding.UTF8, new MediaTypeHeaderValue("text/plain")),
        };
        // Header values must be ASCII; ntfy accepts RFC 2047 encoded-words for anything else.
        req.Headers.Add("Title", Rfc2047(msg.Title));
        req.Headers.Add("Tags", "crossed_swords");
        req.Headers.Add("Priority", "high");
        req.Headers.Add("Click", msg.WhatsAppLink);
        try
        {
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                log.LogError("ntfy returned {Status} for booking {Id}", (int)res.StatusCode, b.Id);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogError(e, "Couldn't reach ntfy for booking {Id}", b.Id);
        }
    }

    private static string Rfc2047(string s) =>
        s.All(c => c < 128) ? s : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(s))}?=";
}

public sealed record NtfyMessage(string Title, string Body, string WhatsAppLink)
{
    public static NtfyMessage For(Booking b, Service service, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(b.Start, tz);
        var au = System.Globalization.CultureInfo.GetCultureInfo("en-AU");
        var when = $"{local.ToString("ddd d MMM", au)}, {local.ToString("h:mmtt", au).ToLowerInvariant()}";
        var lines = new List<string> { $"{b.Name} · {when}", $"{b.Address}, {b.Suburb}", $"Phone {b.Phone}" };
        if (!string.IsNullOrWhiteSpace(b.Problem)) lines.Add($"\"{b.Problem}\"");
        lines.Add("Tap to WhatsApp them.");
        return new NtfyMessage($"New quest: {service.Name}", string.Join('\n', lines), WhatsApp(b.Phone, b.Name));
    }

    /// <summary>wa.me needs the number in international form without '+': 0412... (AU) -> 61412...</summary>
    public static string WhatsApp(string phone, string name)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (phone.TrimStart().StartsWith('+')) { /* already international */ }
        else if (digits.StartsWith("0")) digits = "61" + digits[1..];
        var text = Uri.EscapeDataString($"Hi {name.Split(' ')[0]}, it's Nick from Byte Club about your booking.");
        return $"https://wa.me/{digits}?text={text}";
    }
}
