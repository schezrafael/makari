# CreateSale — M0.3

```http
POST /api/sales
Content-Type: application/json
```

Request:

```json
{
  "idempotencyKey": "8ecda310-3a89-44b4-8eaf-6014ed1f5a34",
  "customerType": "Visitor",
  "paymentMethod": "Cash",
  "items": [
    { "productVariantId": "<variant-guid>", "quantity": 2 }
  ]
}
```

The client cannot set the operator, price, subtotal or total. Enums are serialized
as names (`Local`, `Visitor`, `Cash`, `Transfer`); numeric enum values are rejected.
`Sales:RegisteredBy` supplies the operator identity and defaults to `Andi` for
the MVP. Replace this setting with the authenticated principal when authentication
is introduced.

- New sale: HTTP 201 and the persisted sale with item price snapshots.
- Same key and same effective content (customer, payment method, variant IDs and
  quantities; item order does not matter): HTTP 200 with the original sale.
- Same key and changed content: HTTP 409.
- Invalid request or unavailable/inactive/unpriced variant: HTTP 400.
- Repeated variant in one request: HTTP 400.

The handler prices every variant from PostgreSQL, computes subtotals and the total,
then writes the sale and all items in one transaction. A unique index on
`IdempotencyKey` is the authority across processes. A per-key semaphore avoids
unnecessary same-process races, while a provider-specific unique-violation handler
rereads and compares the winning sale if another process wins the race. SQLite
integration tests cover same-process concurrent requests; a multi-process unique
race still warrants an additional PostgreSQL integration test.

The fingerprint is SHA-256 over the normalized sale fields and sorted variant IDs
and quantities. Prices are intentionally excluded: an identical retry returns the
original stored sale even if catalog prices later change. The fingerprint is
internal persistence metadata and is not returned by the API.

`AddSales` adds `Sales` and `SaleItems`; it is generated but must be applied to an
authorized database explicitly. No migration runs during API startup.
