using System.Threading.RateLimiting;
using ByteClub.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var availability = builder.Configuration.GetSection("Availability").Get<AvailabilityOptions>() ?? new();
builder.Services.AddSingleton(availability);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Availability>();
// SQLite on a dev machine; Azure SQL in production (passwordless: the app signs in with its
// managed identity, see infra/main.bicep). The free serverless database pauses when idle, so
// connections retry while it wakes.
var dbProvider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString("ByteClub") ?? "Data Source=byteclub.db";
builder.Services.AddDbContext<ByteClubDb>(o =>
{
    if (dbProvider == "SqlServer")
        o.UseSqlServer(connection, sql => sql.EnableRetryOnFailure(10, TimeSpan.FromSeconds(10), null));
    else
        o.UseSqlite(connection);
});
// Sign-in cookies are encrypted with keys kept in the database, so a restart or a second
// replica doesn't sign Nick out.
builder.Services.AddDataProtection().SetApplicationName("byteclub").PersistKeysToDbContext<ByteClubDb>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // behind Azure's ingress: the visitor's real IP is in X-Forwarded-For (the rate limit needs it)
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddSingleton(builder.Configuration.GetSection("Notify").Get<NotifyOptions>() ?? new());
builder.Services.AddHttpClient<IBookingNotifier, NtfyNotifier>(c => c.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton(builder.Configuration.GetSection("Business").Get<BusinessOptions>() ?? new());
builder.Services.AddSingleton<AdminLogin>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "byteclub_admin";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict; // plus JSON-only endpoints: no cross-site requests get in
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
    // an API answers 401/403, it doesn't redirect to a login page
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
// bookings + enquiries per visitor (IP) per 10 minutes; tests raise it
var perTenMinutes = builder.Configuration.GetValue<int?>("RateLimit:PerTenMinutes") ?? 5;
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // the site is served by Netlify, which proxies /api here: the address Azure sees is Netlify's,
    // so the visitor's own IP comes from the header Netlify adds
    o.AddPolicy("bookings", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Request.Headers["X-Nf-Client-Connection-Ip"].FirstOrDefault()
            ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perTenMinutes, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

// The page must never wait for the database: a paused free-tier database takes up to a minute
// to wake. Serve the site at once and wake the database in the background; only requests that
// need it wait for it.
var dbReady = EnsureDatabase(app);
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !dbReady.IsCompleted) await dbReady;
    await next();
});

app.UseForwardedHeaders();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/services", () => Services.All);

api.MapGet("/slots", async (ByteClubDb db, Availability av) => av.OpenSlots(await TakenSlots(db)));

api.MapPost("/bookings", async (BookingRequest req, ByteClubDb db, Availability av, TimeProvider clock,
    IBookingNotifier notify, NotifyOptions notifyOptions) =>
{
    var errors = BookingValidation.Validate(req);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
    if (!av.IsOpen(req.Start, await TakenSlots(db)))
        return Results.Conflict(new { error = "That time was just taken. Pick another." });

    var booking = new Booking
    {
        ServiceId = req.ServiceId, Start = req.Start.ToUniversalTime(), Name = req.Name.Trim(),
        Phone = req.Phone.Trim(), Email = req.Email?.Trim(), Suburb = req.Suburb.Trim(),
        Address = req.Address.Trim(), Problem = req.Problem?.Trim(), CreatedAt = clock.GetUtcNow(),
        ActiveSlot = req.Start.ToUniversalTime(),
    };
    db.Bookings.Add(booking);
    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "That time was just taken. Pick another." }); }

    var service = Services.Find(req.ServiceId)!;
    var tz = TimeZoneInfo.FindSystemTimeZoneById(notifyOptions.TimeZone);
    await notify.SendAsync(NtfyMessage.For(booking, service, tz)); // logs and carries on if ntfy is down
    return Results.Created($"/api/bookings/{booking.Id}",
        new { booking.Id, service = service.Name, booking.Start, firstName = booking.Name.Split(' ')[0] });
}).RequireRateLimiting("bookings");

// Game and app ideas: no time slot, Nick follows up on WhatsApp.
api.MapPost("/enquiries", async (EnquiryRequest req, ByteClubDb db, TimeProvider clock, IBookingNotifier notify) =>
{
    var errors = BookingValidation.Validate(req);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));

    var enquiry = new Enquiry
    {
        ServiceId = req.ServiceId, Name = req.Name.Trim(), Phone = req.Phone.Trim(), Email = req.Email?.Trim(),
        Idea = req.Idea.Trim(), CreatedAt = clock.GetUtcNow(),
    };
    db.Enquiries.Add(enquiry);
    await db.SaveChangesAsync();

    var service = Services.Find(req.ServiceId)!;
    await notify.SendAsync(NtfyMessage.For(enquiry, service));
    return Results.Created($"/api/enquiries/{enquiry.Id}",
        new { enquiry.Id, service = service.Name, firstName = enquiry.Name.Split(' ')[0] });
}).RequireRateLimiting("bookings");

app.MapAdmin();
app.MapInvoices();
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));
app.MapFallbackToFile("index.html");

app.Run();

// A paused serverless database can take up to a minute to wake: keep trying before giving up.
static async Task EnsureDatabase(WebApplication app)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ByteClubDb>().Database.EnsureCreatedAsync();
            return;
        }
        catch (Exception e) when (attempt < 24)
        {
            app.Logger.LogWarning("Database not ready ({Message}); retrying", e.Message);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
}

static async Task<List<DateTimeOffset>> TakenSlots(ByteClubDb db) =>
    await db.Bookings.Where(b => b.ActiveSlot != null).Select(b => b.ActiveSlot!.Value).ToListAsync();

public partial class Program;
