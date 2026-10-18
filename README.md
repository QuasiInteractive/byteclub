# Byte Club

Booking app for Byte Club, Gold Coast home-visit tech help, dressed as a late-90s JRPG pause menu.

Customers pick a quest (laptop, phone, Wi-Fi, scam cleanup...), a time through the "time gate", enter
their party details and save. A glove cursor, keyboard control, and original synthesised menu sounds
(off until the visitor turns them on). The look is an homage; every asset and sound is original.

## Stack

- **API:** .NET 10 minimal API, EF Core (SQLite locally), rate-limited bookings, double bookings blocked
  by a unique index, Gold Coast time (no daylight saving). xUnit tests with a fake clock and
  `WebApplicationFactory`.
- **Web:** React 19 + TypeScript + Vite. Built into `api/wwwroot`, so one service serves both.

## Run

```bash
cd web && npm install && npm run build
cd .. && dotnet run --project api --urls http://localhost:5165
dotnet test
```

For front-end work with hot reload, run the API as above and `npm run dev` in `web` (proxies `/api`).

## Next

- Notify Nick when a booking comes in.
- Nick's dashboard: quest log (jobs), bestiary (customer history), items (invoices with ABN as PDF).
- Deploy to Azure.

Elyra Software, a Quasi Interactive company.
