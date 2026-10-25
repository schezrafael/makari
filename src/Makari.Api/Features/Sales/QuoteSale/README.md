# QuoteSale — M0.6

`POST /api/sales/quote` consulta precios vigentes sin crear ni modificar ventas.

Solicitud (solo estos campos; no admite importes, pago ni clave de idempotencia):

```json
{
  "customerType": "Local",
  "items": [{ "productVariantId": "11111111-1111-4111-8111-111111111111", "quantity": 2 }]
}
```

Respuesta HTTP 200 ilustrativa:

```json
{
  "customerType": "Local",
  "subtotal": 150,
  "discount": 30,
  "total": 120,
  "items": [{
    "productVariantId": "11111111-1111-4111-8111-111111111111",
    "quantity": 2,
    "visitorUnitPrice": 75,
    "unitPrice": 60,
    "subtotal": 150,
    "discount": 30,
    "total": 120
  }]
}
```

`subtotal` siempre usa Visitor. `total` usa el tipo solicitado; `discount` es
subtotal menos total. Para Visitor es cero y no se necesita precio Local.
Para Local se requieren ambos precios. Un precio Local superior a Visitor devuelve
400: no se inventa un descuento ni se altera el precio que cobraría CreateSale.

Se rechazan cantidades no positivas, GUID vacíos, líneas duplicadas o nulas,
carrito vacío, tipo inválido, productos/variantes inactivos o inexistentes y precios
faltantes. La validación de negocio devuelve 400 ValidationProblem. JSON mal formado,
campos desconocidos (incluidos importes por línea) o campos requeridos ausentes
se rechazan por el binding de ASP.NET Core con 400.

`SaleOrderValidator`, `SalePricing` y `SaleAmounts` comparten con CreateSale las reglas
de pedido, lectura de precios activos y multiplicación decimal comprobada.
La consulta hace una única proyección SQL con `AsNoTracking`; no usa SaveChanges,
transacciones de escritura, migraciones ni seeds. Se propaga CancellationToken.

La cotización no reserva ni congela precios. CreateSale requiere el subtotal confirmado
como `expectedTotal`, vuelve a consultar precios y no registra si cambió. Devuelve el
nuevo total en un 409 terminal para esa clave; revisa la [documentación de CreateSale](../CreateSale/README.md).

Las pruebas de integración usan SQLite aislado en memoria: cotización Local/Visitor,
validaciones, ausencia de escrituras, lectura de precios actualizados, creación y
reintento idempotente. El uso manual contra PostgreSQL solo consulta catálogo y cotiza.
