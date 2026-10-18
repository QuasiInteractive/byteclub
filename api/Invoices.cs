using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ByteClub.Api;

/// <summary>
/// The business details printed on every invoice. Public details live in appsettings.json;
/// bank details are added by Nick himself in user-secrets / app settings, never in git.
/// </summary>
public sealed class BusinessOptions
{
    public string TradingName { get; set; } = "Byte Club Tech Help";
    public string Owner { get; set; } = "Nicolas Balkhausen";
    public string Abn { get; set; } = "";
    public string Locality { get; set; } = "";
    public string WhatsApp { get; set; } = "";
    public string Email { get; set; } = "";
    public string GstNote { get; set; } = "Not registered for GST - no GST has been charged.";
    public decimal DefaultRate { get; set; } = 80m;
    public int DaysToPay { get; set; } = 7;
    public string? AccountName { get; set; }
    public string? Bsb { get; set; }
    public string? AccountNumber { get; set; }
    public string? PayId { get; set; }

    public bool HasPaymentDetails => !string.IsNullOrWhiteSpace(PayId) ||
        (!string.IsNullOrWhiteSpace(Bsb) && !string.IsNullOrWhiteSpace(AccountNumber));
}

public enum InvoiceStatus { Unpaid, Paid, Void }

public sealed record InvoiceLine(string Description, decimal Hours, decimal Rate)
{
    public decimal Amount => Math.Round(Hours * Rate, 2, MidpointRounding.AwayFromZero);
}

public sealed class Invoice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Number { get; init; }
    public string NumberText => $"INV-{Number:D4}";
    public Guid? BookingId { get; init; }
    public required string BillToName { get; init; }
    public string? BillToAttn { get; init; }
    public string? BillToEmail { get; init; }
    public required string BillToPhone { get; init; }
    public DateOnly IssueDate { get; init; }
    public DateOnly DueDate { get; init; }
    public string LinesJson { get; init; } = "[]";
    public decimal Total { get; init; }
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;
    public DateTimeOffset? PaidAt { get; set; }

    public IReadOnlyList<InvoiceLine> Lines => JsonSerializer.Deserialize<List<InvoiceLine>>(LinesJson) ?? [];
}

public sealed record InvoiceRequest(Guid? BookingId, string BillToName, string? BillToAttn, string? BillToEmail,
    string BillToPhone, List<InvoiceLineRequest> Lines);
public sealed record InvoiceLineRequest(string Description, decimal Hours, decimal Rate);

public static class InvoiceRules
{
    public static Dictionary<string, string> Validate(InvoiceRequest r)
    {
        var e = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(r.BillToName)) e["billToName"] = "Who is the invoice for?";
        if (string.IsNullOrWhiteSpace(r.BillToPhone)) e["billToPhone"] = "Add their phone number.";
        if (r.Lines is not { Count: > 0 }) e["lines"] = "Add at least one line.";
        else if (r.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description) || l.Hours <= 0 || l.Hours > 100 || l.Rate < 0 || l.Rate > 10_000))
            e["lines"] = "Each line needs a description, hours above 0 and a rate.";
        return e;
    }

    public static Invoice Create(InvoiceRequest r, int number, DateOnly today, int daysToPay)
    {
        var lines = r.Lines.Select(l => new InvoiceLine(l.Description.Trim(), l.Hours, l.Rate)).ToList();
        return new Invoice
        {
            Number = number, BookingId = r.BookingId, BillToName = r.BillToName.Trim(), BillToAttn = r.BillToAttn?.Trim(),
            BillToEmail = r.BillToEmail?.Trim(), BillToPhone = r.BillToPhone.Trim(), IssueDate = today,
            DueDate = today.AddDays(daysToPay), LinesJson = JsonSerializer.Serialize(lines), Total = lines.Sum(l => l.Amount),
        };
    }
}

/// <summary>A4 invoice laid out like Nick's Word template: header, bill to, lines, total, payment.</summary>
public static class InvoicePdf
{
    private static readonly CultureInfo Au = CultureInfo.GetCultureInfo("en-AU");
    private const string Navy = "#1A2A85";

    static InvoicePdf() => QuestPDF.Settings.License = LicenseType.Community; // free under US$1M revenue

    public static byte[] Render(Invoice inv, BusinessOptions biz) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(42);
        page.DefaultTextStyle(t => t.FontSize(10.5f).FontColor("#1D2320"));

        page.Header().Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().Text(biz.TradingName).FontSize(18).Bold().FontColor(Navy);
                c.Item().Text(biz.Owner);
                if (!string.IsNullOrWhiteSpace(biz.Abn)) c.Item().Text($"ABN: {biz.Abn}");
                if (!string.IsNullOrWhiteSpace(biz.Locality)) c.Item().Text(biz.Locality);
                c.Item().Text(string.Join("  |  ", new[] { biz.WhatsApp is { Length: > 0 } w ? $"WhatsApp {w}" : null, biz.Email }
                    .Where(s => !string.IsNullOrWhiteSpace(s))));
            });
            row.ConstantItem(200).AlignRight().Column(c =>
            {
                c.Item().AlignRight().Text("INVOICE").FontSize(22).Bold().FontColor(Navy);
                c.Item().AlignRight().Text($"Invoice no: {inv.NumberText}");
                c.Item().AlignRight().Text($"Date: {inv.IssueDate.ToString("d MMMM yyyy", Au)}");
                c.Item().AlignRight().Text($"Due: {inv.DueDate.ToString("d MMMM yyyy", Au)}");
                if (inv.Status == InvoiceStatus.Paid) c.Item().AlignRight().Text("PAID").Bold().FontColor("#1F6B45");
            });
        });

        page.Content().PaddingTop(18).Column(col =>
        {
            col.Spacing(14);
            col.Item().Text(biz.GstNote).Italic().FontColor("#55605A");

            col.Item().Column(c =>
            {
                c.Item().Text("Bill to").Bold().FontColor(Navy);
                c.Item().Text(inv.BillToName);
                if (!string.IsNullOrWhiteSpace(inv.BillToAttn)) c.Item().Text($"Attn: {inv.BillToAttn}");
                if (!string.IsNullOrWhiteSpace(inv.BillToEmail)) c.Item().Text(inv.BillToEmail);
                c.Item().Text(inv.BillToPhone);
            });

            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(6); c.RelativeColumn(1.2f); c.RelativeColumn(1.6f); c.RelativeColumn(1.8f); });
                static IContainer Head(IContainer x) => x.Background(Navy).PaddingVertical(5).PaddingHorizontal(6);
                t.Header(h =>
                {
                    h.Cell().Element(Head).Text("Description").FontColor(Colors.White).Bold();
                    h.Cell().Element(Head).AlignRight().Text("Hours").FontColor(Colors.White).Bold();
                    h.Cell().Element(Head).AlignRight().Text("Rate").FontColor(Colors.White).Bold();
                    h.Cell().Element(Head).AlignRight().Text("Amount").FontColor(Colors.White).Bold();
                });
                static IContainer Cell(IContainer x) => x.BorderBottom(0.5f).BorderColor("#D5D9E4").PaddingVertical(6).PaddingHorizontal(6);
                foreach (var l in inv.Lines)
                {
                    t.Cell().Element(Cell).Text(l.Description);
                    t.Cell().Element(Cell).AlignRight().Text(l.Hours.ToString("0.##", Au));
                    t.Cell().Element(Cell).AlignRight().Text(l.Rate.ToString("C", Au));
                    t.Cell().Element(Cell).AlignRight().Text(l.Amount.ToString("C", Au));
                }
                t.Cell().ColumnSpan(3).PaddingTop(8).PaddingHorizontal(6).AlignRight().Text("Total").Bold();
                t.Cell().PaddingTop(8).PaddingHorizontal(6).AlignRight().Text(inv.Total.ToString("C", Au)).Bold().FontSize(13);
            });

            col.Item().Column(c =>
            {
                c.Item().Text("Payment details").Bold().FontColor(Navy);
                if (!biz.HasPaymentDetails)
                {
                    c.Item().Text("Payment details to be advised.").Italic();
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(biz.PayId)) c.Item().Text($"PayID: {biz.PayId}");
                    if (!string.IsNullOrWhiteSpace(biz.Bsb) && !string.IsNullOrWhiteSpace(biz.AccountNumber))
                    {
                        c.Item().Text($"Account name: {biz.AccountName ?? biz.Owner}");
                        c.Item().Text($"BSB: {biz.Bsb}");
                        c.Item().Text($"Account number: {biz.AccountNumber}");
                    }
                }
                c.Item().Text($"Reference: {inv.NumberText}");
            });
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Thanks for your business! For any future tech help, WhatsApp ").FontColor("#55605A");
            t.Span(biz.WhatsApp).FontColor("#55605A");
            t.Span(".").FontColor("#55605A");
        });
    })).GeneratePdf();
}

public static class InvoiceEndpoints
{
    public static void MapInvoices(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin/invoices").RequireAuthorization();

        admin.MapGet("/", async (ByteClubDb db) =>
        {
            var all = await db.Invoices.AsNoTracking().ToListAsync();
            return all.OrderByDescending(i => i.Number).Select(i => new
            {
                i.Id, number = i.NumberText, i.BillToName, i.BillToPhone, i.IssueDate, i.DueDate, i.Total,
                status = i.Status.ToString(), i.BookingId,
            });
        });

        admin.MapPost("/", async (InvoiceRequest req, ByteClubDb db, BusinessOptions biz, TimeProvider clock) =>
        {
            var errors = InvoiceRules.Validate(req);
            if (errors.Count > 0) return Results.ValidationProblem(errors.ToDictionary(e => e.Key, e => new[] { e.Value }));
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Brisbane");
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
            var next = (await db.Invoices.MaxAsync(i => (int?)i.Number) ?? 0) + 1;
            var inv = InvoiceRules.Create(req, next, today, biz.DaysToPay);
            db.Invoices.Add(inv);
            await db.SaveChangesAsync(); // unique index on Number guards a double submit
            return Results.Created($"/api/admin/invoices/{inv.Id}", new { inv.Id, number = inv.NumberText, inv.Total });
        });

        admin.MapGet("/{id:guid}/pdf", async (Guid id, ByteClubDb db, BusinessOptions biz) =>
        {
            var inv = await db.Invoices.FindAsync(id);
            if (inv is null) return Results.NotFound();
            return Results.File(InvoicePdf.Render(inv, biz), "application/pdf", $"{inv.NumberText} - {biz.TradingName}.pdf",
                enableRangeProcessing: false);
        });

        admin.MapPatch("/{id:guid}", async (Guid id, StatusChange body, ByteClubDb db, TimeProvider clock) =>
        {
            if (!Enum.TryParse<InvoiceStatus>(body.Status, out var status)) return Results.BadRequest(new { error = "Unknown status." });
            var inv = await db.Invoices.FindAsync(id);
            if (inv is null) return Results.NotFound();
            inv.Status = status;
            inv.PaidAt = status == InvoiceStatus.Paid ? clock.GetUtcNow() : null;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        admin.MapGet("/defaults", (BusinessOptions biz) =>
            new { rate = biz.DefaultRate, daysToPay = biz.DaysToPay, paymentDetailsSet = biz.HasPaymentDetails });
    }
}
