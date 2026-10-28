# CashCloses — M0.9

One shared cash drawer is closed at most once per business-local date. `Business:TimeZoneId`
controls the business date; the default is `America/Mexico_City`. A future business date
cannot be closed.

## HTTP contracts

```http
POST /api/cash-closes
Content-Type: application/json
```

```json
{
  "idempotencyKey": "8ecda310-3a89-44b4-8eaf-6014ed1f5a34",
  "businessDate": "2026-10-26",
  "openingCash": 0,
  "countedCash": 320.50,
  "responsible": "Andi",
  "observations": "Conteo final"
}
```

The request contains no sales totals. The API aggregates persisted `Sales` over the
business-date UTC interval and stores these immutable snapshots: `CashSales`,
`TransferSales`, `TotalRevenue`, `SalesCount`, `OpeningCash`, `ExpectedCash`,
`CountedCash`, and `Difference`. `ExpectedCash = OpeningCash + CashSales`;
`Difference = CountedCash - ExpectedCash`. Transfer revenue is excluded from the
physical cash calculation. Amounts use decimal values and permit zero for both cash inputs.

- 201: new confirmed close with its receipt.
- 200: identical retry; returns the original receipt, including after API restart.
- 400 `cash_close_validation_failed`: invalid date, amount, responsible person, or observations.
- 409 `cash_close_exists`: another idempotency key already closed the business date;
  `existingCloseId` identifies the confirmed receipt.
- 409 `idempotency_conflict`: the key was used with different close contents.

Read a close by date (omitting date uses today's business-local date):

```http
GET /api/cash-closes?date=2026-10-26
```

Returns 200 with the confirmed receipt, or 404 `cash_close_not_found` if the date remains
open. Read a specific receipt with `GET /api/cash-closes/{id}`.

## Sales and close consistency

CreateSale and CreateDailyCashClose acquire the same PostgreSQL transaction advisory lock
for the business date. A sale that acquires it first commits before the close totals are
read and is included in the snapshot. A close that acquires it first commits before the
sale checks for a close; that new sale is rejected with HTTP 409 `daily_cash_closed`.
The rejected sale key and date are persisted so retries keep returning the same conflict.
CreateSale checks for an already-confirmed sale before taking this date lock, so retrying
the same confirmed sale key still returns its original receipt after close.

The confirmed close is immutable through the API and `BusinessDate` has a unique database
index. Do not run old API instances alongside M0.9: every instance accepting sales must
take the business-date lock and enforce the closed-date check before cash closes are
enabled. The rejection-code columns on `RejectedSaleAttempts` make `daily_cash_closed` a
terminal idempotent outcome.

## Migration and verification

`AddDailyCashCloses` (`20261027035313_AddDailyCashCloses`) creates the immutable snapshot
table, unique indexes on `BusinessDate` and `IdempotencyKey`, and adds nullable conflict
code/date columns to `RejectedSaleAttempts`. It does not change commercial prices or
existing sales. Application startup never applies migrations automatically.

To apply and verify it in the test environment, first configure a **local-only**
`MAKARI_TEST_CONNECTION` and confirm it identifies only `makari_test`:

```sh
psql "$MAKARI_TEST_CONNECTION" -X -At -c 'SELECT current_database()'
```

Continue only when the output is `makari_test`. Generate and review the SQL, then apply
the migration explicitly with that same connection:

```sh
dotnet ef migrations script 20261024021902_AddRejectedSaleAttempts 20261027035313_AddDailyCashCloses --project src/Makari.Api --output /tmp/makari-add-daily-cash-closes.sql
dotnet ef database update --project src/Makari.Api --connection "$MAKARI_TEST_CONNECTION"
dotnet ef migrations list --project src/Makari.Api --connection "$MAKARI_TEST_CONNECTION"
```

The final command should show `AddDailyCashCloses` as applied. The PostgreSQL concurrency
and restart tests are opt-in and use the same guarded variable:

```sh
dotnet test MakariPOS.slnx --filter FullyQualifiedName~PostgreSqlCashCloseTests
```

The test fixture refuses any database other than `makari_test`, checks for pending
migrations, labels its records `M0.9-TEST`, and cleans rows by generated IDs/keys. Do not
apply this migration or run these tests against `makari_dev`.
