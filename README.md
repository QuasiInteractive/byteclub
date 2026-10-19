# Byte Club

**Live:** https://byteclub.quasi-interactive.com

Booking app for Byte Club, Gold Coast home-visit tech help, dressed as a late-90s JRPG pause menu,
plus Nick's dashboard for running the business.

**For customers:** pick a quest (laptop, phone, Wi-Fi, scam cleanup... or *make a video game* /
*build an app*), pick a time through the "time gate", enter party details, save. Glove cursor,
full keyboard control, painted portrait and device art, menu sounds.

**For Nick (`/admin`):** passwordless sign-in (a one-time code pushed to his phone), quest log
(confirm / done / cancel, which frees the slot), game and app ideas pipeline, customer
"bestiary", and invoices as PDFs with his ABN.

## Stack

- **.NET 10** minimal API, EF Core (SQLite locally, **Azure SQL** in production), cookie auth,
  rate limiting, QuestPDF invoices, push notifications through ntfy. 29 xUnit tests (fake clock,
  `WebApplicationFactory`).
- **React 19 + TypeScript + Vite**, built into the API's `wwwroot`: one container serves both.
- **Azure Container Apps** (scale to zero) + **Azure SQL Database free offer**, both in Bicep.
  **No database password exists**: the SQL server only accepts Microsoft Entra sign-in and the
  app connects with its managed identity. Data Protection keys live in the database so sign-ins
  survive restarts.
- GitHub Actions: tests on every push; the image is built and pushed to GHCR on master.

## Run locally

```bash
cd web && npm install && npm run build
cd .. && dotnet run --project api --urls http://localhost:5165
dotnet test
```

Secrets (never in git): `dotnet user-secrets set "Notify:NtfyTopic" ...` and, for invoices,
`Business:Bsb`, `Business:AccountNumber` (or `Business:PayId`).

The menu sounds used on the live site aren't in this repo; without them the app plays its own
synthesised sounds (`web/src/sound.ts`).

Elyra Software, a Quasi Interactive company.
