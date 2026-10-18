using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ByteClub.Api;
using Microsoft.Extensions.Time.Testing;

namespace ByteClub.Api.Tests;

public class AdminLoginTests
{
    [Fact]
    public void A_code_works_once()
    {
        var login = new AdminLogin(new FakeTimeProvider());
        var code = login.Issue()!;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.True(login.Redeem(code));
        Assert.False(login.Redeem(code));
    }

    [Fact]
    public void A_code_expires_after_ten_minutes()
    {
        var clock = new FakeTimeProvider();
        var login = new AdminLogin(clock);
        var code = login.Issue()!;
        clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        Assert.False(login.Redeem(code));
    }

    [Fact]
    public void Five_wrong_guesses_burn_the_code()
    {
        var login = new AdminLogin(new FakeTimeProvider());
        var code = login.Issue()!;
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < AdminLogin.MaxWrongGuesses; i++) Assert.False(login.Redeem(wrong));
        Assert.False(login.Redeem(code));
    }

    [Fact]
    public void At_most_five_codes_an_hour()
    {
        var clock = new FakeTimeProvider();
        var login = new AdminLogin(clock);
        for (var i = 0; i < AdminLogin.MaxCodesPerHour; i++) Assert.NotNull(login.Issue());
        Assert.Null(login.Issue());
        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.NotNull(login.Issue());
    }
}

public class AdminApiTests : IClassFixture<BookingApiTests.Factory>
{
    private readonly BookingApiTests.Factory _f;
    public AdminApiTests(BookingApiTests.Factory f) => _f = f;

    private async Task<HttpClient> SignedIn()
    {
        var http = _f.CreateClient(); // keeps cookies between calls
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/api/admin/code", null)).StatusCode);
        var title = _f.Notifier.Sent.Last(m => m.Title.StartsWith("Byte Club sign-in code")).Title;
        var code = Regex.Match(title, "[0-9]{6}").Value;
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync("/api/admin/session", new { code })).StatusCode);
        return http;
    }

    [Fact]
    public async Task The_dashboard_is_locked_without_signing_in()
    {
        var http = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/admin/bookings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/admin/customers")).StatusCode);
        var wrong = await http.PostAsJsonAsync("/api/admin/session", new { code = "123456" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
    }

    [Fact]
    public async Task Signed_in_nick_can_cancel_a_booking_and_the_slot_comes_back()
    {
        var http = await SignedIn();
        var slot = (await http.GetFromJsonAsync<List<Slot>>("/api/slots"))!.Last();
        var booked = await http.PostAsJsonAsync("/api/bookings", new
        {
            serviceId = "wifi", start = slot.Start, name = "Pat Example", phone = "0499 999 999",
            suburb = "Nerang", address = "2 Example Rd",
        });
        var id = (await booked.Content.ReadFromJsonAsync<Dictionary<string, object>>())!["id"].ToString();
        Assert.DoesNotContain((await http.GetFromJsonAsync<List<Slot>>("/api/slots"))!, s => s.Start == slot.Start);

        var cancel = await http.PatchAsJsonAsync($"/api/admin/bookings/{id}", new { status = "Cancelled" });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Contains((await http.GetFromJsonAsync<List<Slot>>("/api/slots"))!, s => s.Start == slot.Start);

        var customers = await http.GetStringAsync("/api/admin/customers");
        Assert.Contains("Pat Example", customers);
    }
}
