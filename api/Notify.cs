using System.Net.Http.Headers;
using System.Text;

namespace ByteClub.Api;

/// <summary>Tells Nick about a new booking or enquiry. A failure here must never fail the save.</summary>
public interface IBookingNotifier
{
    Task SendAsync(NtfyMessage message, CancellationToken ct = default);
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
    public async Task SendAsync(NtfyMessage msg, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.NtfyTopic))
        {
            log.LogWarning("'{Title}' saved but no ntfy topic is configured, so nobody was notified", msg.Title);
            return;
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{options.NtfyServer.TrimEnd('/')}/{options.NtfyTopic}")
        {
            Content = new StringContent(msg.Body, Encoding.UTF8, new MediaTypeHeaderValue("text/plain")),
        };
        // Header values must be ASCII; ntfy accepts RFC 2047 encoded-words for anything else.
        req.Headers.Add("Title", Rfc2047(msg.Title));
        req.Headers.Add("Tags", msg.Tags);
        req.Headers.Add("Priority", "high");
        if (!string.IsNullOrEmpty(msg.WhatsAppLink)) req.Headers.Add("Click", msg.WhatsAppLink);
        try
        {
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                log.LogError("ntfy returned {Status} for '{Title}'", (int)res.StatusCode, msg.Title);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogError(e, "Couldn't reach ntfy for '{Title}'", msg.Title);
        }
    }

    private static string Rfc2047(string s) =>
        s.All(c => c < 128) ? s : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(s))}?=";
}

public sealed record NtfyMessage(string Title, string Body, string WhatsAppLink, string Tags)
{
    public static NtfyMessage For(Booking b, Service service, TimeZoneInfo tz)
    {
        var local = TimeZoneInfo.ConvertTime(b.Start, tz);
        var au = System.Globalization.CultureInfo.GetCultureInfo("en-AU");
        var when = $"{local.ToString("ddd d MMM", au)}, {local.ToString("h:mmtt", au).ToLowerInvariant()}";
        var lines = new List<string> { $"{b.Name} · {when}", $"{b.Address}, {b.Suburb}", $"Phone {b.Phone}" };
        if (!string.IsNullOrWhiteSpace(b.Problem)) lines.Add($"\"{b.Problem}\"");
        lines.Add("Tap to WhatsApp them.");
        return new NtfyMessage($"New quest: {service.Name}", string.Join('\n', lines),
            WhatsApp(b.Phone, $"Hi {FirstName(b.Name)}, it's Nick from Byte Club about your booking."), "crossed_swords");
    }

    public static NtfyMessage For(Enquiry e, Service service)
    {
        var lines = new List<string> { e.Name, $"Phone {e.Phone}", $"\"{e.Idea}\"", "Tap to WhatsApp them." };
        var noun = service.Id == "game" ? "game" : "app";
        return new NtfyMessage($"New PROJECT quest: {service.Name}", string.Join('\n', lines),
            WhatsApp(e.Phone, $"Hi {FirstName(e.Name)}, it's Nick from Byte Club about your {noun} idea."), "star2,video_game");
    }

    private static string FirstName(string name) => name.Trim().Split(' ')[0];

    /// <summary>wa.me needs the number in international form without '+': 0412... (AU) -> 61412...</summary>
    public static string WhatsApp(string phone, string greeting)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (!phone.TrimStart().StartsWith('+') && digits.StartsWith('0')) digits = "61" + digits[1..];
        return $"https://wa.me/{digits}?text={Uri.EscapeDataString(greeting)}";
    }
}
