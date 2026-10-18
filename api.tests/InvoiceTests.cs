using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ByteClub.Api;

namespace ByteClub.Api.Tests;

public class InvoiceRuleTests
{
    private static InvoiceRequest Req(params InvoiceLineRequest[] lines) =>
        new(null, "Mary Jones", null, "mary@example.com", "0412 345 678", [.. lines]);

    [Fact]
    public void Totals_add_up_and_round_to_the_cent()
    {
        var inv = InvoiceRules.Create(Req(new InvoiceLineRequest("Laptop cleanup", 1.5m, 80m), new("Travel", 0.25m, 33.33m)),
            7, new DateOnly(2026, 10, 19), 7);
        Assert.Equal("INV-0007", inv.NumberText);
        Assert.Equal([120.00m, 8.33m], inv.Lines.Select(l => l.Amount));
        Assert.Equal(128.33m, inv.Total);
        Assert.Equal(new DateOnly(2026, 10, 26), inv.DueDate);
    }

    [Fact]
    public void Empty_or_silly_lines_are_refused()
    {
        Assert.Contains("lines", InvoiceRules.Validate(Req()).Keys);
        Assert.Contains("lines", InvoiceRules.Validate(Req(new InvoiceLineRequest("", 1, 80))).Keys);
        Assert.Contains("lines", InvoiceRules.Validate(Req(new InvoiceLineRequest("Thing", 0, 80))).Keys);
        Assert.Empty(InvoiceRules.Validate(Req(new InvoiceLineRequest("Thing", 1, 80))));
    }

    [Fact]
    public void Pdf_renders_with_and_without_bank_details()
    {
        var inv = InvoiceRules.Create(Req(new InvoiceLineRequest("Scam cleanup - Mon 19 Oct", 2m, 80m)), 1, new DateOnly(2026, 10, 19), 7);
        var biz = new BusinessOptions { Abn = "18 843 622 574", Locality = "Biggera Waters QLD 4216", WhatsApp = "0467 696 556" };
        var pdf = InvoicePdf.Render(inv, biz);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.False(biz.HasPaymentDetails);
        biz.Bsb = "none"; biz.AccountNumber = "none"; biz.PayId = "none";
        Assert.False(biz.HasPaymentDetails); // placeholders from Azure count as blank
        biz.Bsb = "000-000"; biz.AccountNumber = "00000000";
        Assert.True(biz.HasPaymentDetails);
        Assert.True(InvoicePdf.Render(inv, biz).Length > 1000);
    }
}

public class InvoiceApiTests : IClassFixture<BookingApiTests.Factory>
{
    private readonly BookingApiTests.Factory _f;
    public InvoiceApiTests(BookingApiTests.Factory f) => _f = f;

    private async Task<HttpClient> SignedIn()
    {
        var http = _f.CreateClient();
        await http.PostAsync("/api/admin/code", null);
        var code = Regex.Match(_f.Notifier.Sent.Last(m => m.Title.StartsWith("Byte Club sign-in code")).Title, "[0-9]{6}").Value;
        await http.PostAsJsonAsync("/api/admin/session", new { code });
        return http;
    }

    private static object Body(string desc) => new
    {
        billToName = "Mary Jones", billToPhone = "0412 345 678",
        lines = new[] { new { description = desc, hours = 2, rate = 80 } },
    };

    [Fact]
    public async Task Invoices_are_locked_without_signing_in()
    {
        var http = _f.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/admin/invoices", Body("x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/admin/invoices")).StatusCode);
    }

    [Fact]
    public async Task Invoice_numbers_go_up_and_the_pdf_downloads()
    {
        var http = await SignedIn();
        var a = await (await http.PostAsJsonAsync("/api/admin/invoices", Body("Wi-Fi fix"))).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var b = await (await http.PostAsJsonAsync("/api/admin/invoices", Body("Printer setup"))).Content.ReadFromJsonAsync<Dictionary<string, object>>();
        var na = int.Parse(a!["number"].ToString()![4..]);
        var nb = int.Parse(b!["number"].ToString()![4..]);
        Assert.Equal(na + 1, nb);

        var pdf = await http.GetAsync($"/api/admin/invoices/{b["id"]}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NoContent,
            (await http.PatchAsJsonAsync($"/api/admin/invoices/{b["id"]}", new { status = "Paid" })).StatusCode);
    }
}

public class InvoiceSample
{
    // Writes a sample PDF to look at by eye: set BYTECLUB_SAMPLE_PDF to a file path.
    [Fact]
    public void Write_sample_when_asked()
    {
        var path = Environment.GetEnvironmentVariable("BYTECLUB_SAMPLE_PDF");
        if (string.IsNullOrEmpty(path)) return;
        var inv = InvoiceRules.Create(new InvoiceRequest(null, "Mary Jones", null, "mary@example.com", "0412 345 678",
            [new("Scam cleanup and antivirus setup - Mon 19 Oct 2026", 1.5m, 80m), new("New printer setup", 0.5m, 80m)]),
            1, new DateOnly(2026, 10, 19), 7);
        var biz = new BusinessOptions { Abn = "18 843 622 574", Locality = "Biggera Waters QLD 4216",
            WhatsApp = "0467 696 556", Email = "ryderlance9@gmail.com" };
        File.WriteAllBytes(path, InvoicePdf.Render(inv, biz));
    }
}
