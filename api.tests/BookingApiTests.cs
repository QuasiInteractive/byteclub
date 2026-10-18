using System.Net;
using System.Net.Http.Json;
using ByteClub.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ByteClub.Api.Tests;

public class BookingApiTests : IClassFixture<BookingApiTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"byteclub-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("ConnectionStrings:ByteClub", $"Data Source={_db}");
    }

    private readonly HttpClient _http;
    public BookingApiTests(Factory f) => _http = f.CreateClient();

    private async Task<Slot> FirstOpenSlot() =>
        (await _http.GetFromJsonAsync<List<Slot>>("/api/slots"))!.First();

    private static object Request(DateTimeOffset start) => new
    {
        serviceId = "laptop", start, name = "Mary Jones", phone = "0412 345 678",
        suburb = "Southport", address = "1 Example St", problem = "Very slow",
    };

    [Fact]
    public async Task Services_are_listed()
    {
        var services = await _http.GetFromJsonAsync<List<Service>>("/api/services");
        Assert.Contains(services!, s => s.Id == "scam");
    }

    [Fact]
    public async Task A_slot_can_be_booked_once_and_then_disappears()
    {
        var slot = await FirstOpenSlot();
        var first = await _http.PostAsJsonAsync("/api/bookings", Request(slot.Start));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _http.PostAsJsonAsync("/api/bookings", Request(slot.Start));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var after = await _http.GetFromJsonAsync<List<Slot>>("/api/slots");
        Assert.DoesNotContain(after!, s => s.Start == slot.Start);
    }

    [Fact]
    public async Task Invalid_requests_get_field_errors()
    {
        var slot = await FirstOpenSlot();
        var res = await _http.PostAsJsonAsync("/api/bookings", new { serviceId = "laptop", start = slot.Start, name = "", phone = "1", suburb = "", address = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("phone", await res.Content.ReadAsStringAsync());
    }
}
