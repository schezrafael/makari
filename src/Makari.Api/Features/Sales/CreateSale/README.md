# CreateSale — M0.7 Reliable Checkout

```http
POST /api/sales
Content-Type: application/json
```

```json
{
  "idempotencyKey": "8ecda310-3a89-44b4-8eaf-6014ed1f5a34",
  "customerType": "Visitor",
  "paymentMethod": "Cash",
  "expectedTotal": 120,
  "items": [{ "productVariantId": "<variant-guid>", "quantity": 2 }]
}
```

`expectedTotal` es obligatorio, positivo y con hasta dos decimales. Es una condición
para registrar, nunca un precio que el servidor acepte como autoridad. El backend
consulta precios y calcula el total; solo registra si coincide exactamente. Se mantiene
el JSON de respuesta de CreateSale, incluido folio, total y clave. `Sales:RegisteredBy`
sigue proporcionando el operador; por defecto es Andi.

## Respuestas

- 201: venta nueva. 200: repetición de una venta ya registrada, con su folio y total originales.
- 409 `code: "price_changed"`, `idempotencyKey`, `currentTotal`: la clave quedó rechazada
  definitivamente, sin crear venta. El cliente conserva el borrador, cotiza otra vez,
  solicita nueva confirmación y solo entonces usa una clave nueva.
- 409 `code: "idempotency_conflict"`, `idempotencyKey`: la clave se resolvió con otro
  contenido o importe esperado. No se debe cambiar automáticamente de clave.
- 409 `code: "daily_cash_closed"`, `businessDate`: la solicitud quedó rechazada para la
  fecha operativa cerrada. El rechazo se persiste, por lo que la misma clave repite el
  conflicto. Una venta ya confirmada se busca antes de esta regla y conserva su replay.
- 400 `code: "sale_validation_failed"`, `idempotencyKey`, `errors`: rechazo de negocio
  definitivo (por ejemplo, variante inactiva). Se conserva el borrador para corregirlo.
- 400 de binding o `code: "request_validation_failed"`: solicitud mal formada. No prueba
  que una operación anterior con esa clave no exista; el checkout no desbloquea un intento
  incierto por esta respuesta. Otros 4xx/5xx, timeout o JSON ambiguo tampoco lo resuelven.

Los códigos se incluyen como extensiones de ProblemDetails. El cliente verifica también
que la clave de la respuesta sea la del intento pendiente.

## Orden y concurrencia

1. Validación sintáctica del request.
2. Semáforo local por clave, transacción y `pg_advisory_xact_lock` por clave en PostgreSQL.
3. Buscar venta existente antes de consultar precios o disponibilidad. Comparar fingerprint
   y `expectedTotal` con el total persistido: un reintento idéntico funciona aunque el
   catálogo haya cambiado o el producto se haya desactivado.
4. Buscar rechazo definitivo en `RejectedSaleAttempts`. Repetir el rechazo original para
   contenido idéntico; otro contenido/expectedTotal produce conflicto de idempotencia.
5. Tomar el advisory lock exclusivo por fecha operativa compartido con DailyCashClose.
   Si ya existe un cierre, persistir el rechazo `daily_cash_closed`; si no, consultar
   precios y guardar rechazo o venta e items antes de responder.

El cierre y toda venta nueva serializan usando el mismo advisory lock transaccional de
fecha. Si la venta obtiene el lock primero, el cierre incluye su registro ya confirmado;
si el cierre gana, la venta se rechaza. Las búsquedas de venta existente y rechazo por
clave preceden el lock de fecha, preservando la recuperación de comprobantes.

El fingerprint conserva el formato histórico (cliente, pago, variantes y cantidades
ordenadas). No depende de precios actuales. Para ventas aceptadas, el total persistido
es también el expectedTotal aceptado; se compara aparte para no invalidar fingerprints
anteriores ni necesitar un backfill.

La tabla de rechazos es necesaria: sin ella, una respuesta 409 podría desbloquear al
operador mientras otro reintento tardío de la misma clave registra una venta después de
un cambio de precios. Los rechazos no son ventas, pero sus claves son terminales.
No borrar esas filas mientras puedan existir reintentos. El bloqueo PostgreSQL coordina
las dos tablas entre instancias M0.7; el índice único de Sales se mantiene como defensa.
No mezclar instancias antiguas con M0.7 durante el despliegue.

## Migración y ejecución

`AddRejectedSaleAttempts` añade exclusivamente la tabla de rechazos. No modifica precios
ni ventas existentes. Se generó la migración y su snapshot, **no se aplicó a PostgreSQL**.
Antes de iniciar M0.7, el operador debe revisar y aplicar explícitamente en el entorno
correcto (con su conexión local configurada):

```sh
dotnet ef migrations script 20261020220102_AddSales --project src/Makari.Api
dotnet ef database update --project src/Makari.Api
dotnet run --project src/Makari.Api --launch-profile http
```

El arranque nunca migra automáticamente. Un backend anterior no cumple este contrato:
frontend y backend deben actualizarse juntos.

## Pruebas y límites

Las pruebas HTTP usan SQLite aislado. Verifican expectedTotal, ausencia de ventas ante
rechazo, nueva confirmación, repetición histórica, conflictos, rechazos terminales aun
si después cambian precios/disponibilidad, y la suite previa de CreateSale.
Se verifica el SQL generado y que el snapshot coincida con el modelo sin conectarse a PG.

Pendiente contra PostgreSQL: aplicar la migración en una base de pruebas, verificar el
bloqueo advisory con dos procesos API, solicitudes concurrentes de igual/distinto importe,
respuesta perdida después del commit (venta o rechazo), reinicio de procesos y conservación
de la clave terminal. No se modificaron precios ni datos de PostgreSQL en M0.7.
