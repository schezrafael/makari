# GetDailySalesSummary

```http
GET /api/sales/summary?date=YYYY-MM-DD
```

`date` is optional; when omitted, the API selects today's date in `Business:TimeZoneId`.
The default is `America/Mexico_City`. The handler converts local midnight and the next
local midnight to UTC and filters sales with `[startUtc, endUtc)`, so day length follows
the configured zone's daylight-saving rules. Invalid calendar dates or non-ISO date
strings return HTTP 400.

The response contains `date`, `timeZoneId`, `salesCount`, `totalRevenue`, `cashRevenue`,
`transferRevenue`, `unitsSold`, and `variants`. Variant quantities and revenue aggregate
SaleItem quantities and stored historical subtotals, joined to current product and
variant names. Amounts use decimal. Empty days return zeros and an empty `variants` array.
Only persisted Sales and SaleItems participate; `RejectedSaleAttempts` are not queried.

## PostgreSQL integration test

`PostgreSqlDailySalesSummaryTests` issues the HTTP request through the API test host and
executes the Npgsql query against a real database. It requires `MAKARI_TEST_CONNECTION`;
the test verifies that its database is exactly `makari_test` and refuses to migrate it.
The general suite skips this test when the variable is absent. The test inserts rows with
the `M0.8.2-TEST` marker and removes only rows associated with its generated IDs in a
`finally` block. Configure the local-only connection string and run:

```sh
dotnet test MakariPOS.slnx --filter FullyQualifiedName~PostgreSqlDailySalesSummaryTests
```

The test database must already have all migrations applied. No connection string or
credential belongs in source control.

```json
{
  "date": "2026-10-25",
  "timeZoneId": "America/Mexico_City",
  "salesCount": 2,
  "totalRevenue": 185,
  "cashRevenue": 120,
  "transferRevenue": 65,
  "unitsSold": 3,
  "variants": [{
    "productVariantId": "<guid>", "productName": "Cold Brew", "variantName": "Cítrico",
    "quantity": 3, "revenue": 185
  }]
}
```
