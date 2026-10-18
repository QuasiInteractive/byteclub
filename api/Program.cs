using System.Threading.RateLimiting;
using ByteClub.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var availability = builder.Configuration.GetSection("Availability").Get<AvailabilityOptions>() ?? new();
builder.Services.AddSingleton(availability);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Availability>();
builder.Services.AddDbContext<ByteClubDb>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("ByteClub") ?? "Data Source=byteclub.db"));
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("bookings", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(10) }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<ByteClubDb>().Database.EnsureCreated();

app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

api.MapGet("/services", () => Services.All);

api.MapGet("/slots", async (ByteClubDb db, Availability av) => av.OpenSlots(await TakenSlots(db)));

api.MapPost("/bookings", async (BookingRequest req, ByteClubDb db, Availability av, TimeProvider clock) =>
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
    return Results.Created($"/api/bookings/{booking.Id}",
        new { booking.Id, service = service.Name, booking.Start, firstName = booking.Name.Split(' ')[0] });
}).RequireRateLimiting("bookings");

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));
app.MapFallbackToFile("index.html");

app.Run();

static async Task<List<DateTimeOffset>> TakenSlots(ByteClubDb db) =>
    await db.Bookings.Where(b => b.ActiveSlot != null).Select(b => b.ActiveSlot!.Value).ToListAsync();

public partial class Program;
