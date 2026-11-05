# Makari POS — M0.9 Daily Cash Close

La navegación permite abrir Nueva venta, Ventas del día y Cierre de caja. El cierre muestra
los totales del servidor, calcula efectivo esperado y diferencia, y conserva un comprobante
inmutable después de confirmarlo.

La navegación principal permite alternar entre Nueva venta y Ventas del día. El resumen
consulta `GET /api/sales/summary` sin fecha al abrir (fecha comercial del backend) y usa
`?date=YYYY-MM-DD` al elegir otra fecha. Muestra ingresos, número de ventas, efectivo,
transferencias, unidades y ventas agrupadas por variante. El detalle del contrato está en
[GetDailySalesSummary](../Makari.Api/Features/Sales/GetDailySalesSummary/README.md).

Interfaz React + TypeScript para registrar ventas desde celular, tablet o escritorio.
Catálogo Visitor, cantidades, descuento local, resumen, efectivo/transferencia y confirmación modal.
El total estimado permanece fijo en móvil; el éxito muestra el total confirmado por la API.

## Ejecutar

Desde la raíz, con PostgreSQL configurado y las migraciones InitialCatalog, AddSales,
AddRejectedSaleAttempts y AddDailyCashCloses aplicadas explícitamente (el arranque no
migra ni siembra). M0.9 requiere la última migración antes de desplegar los nuevos nodos API.

```sh
dotnet run --project src/Makari.Api --launch-profile http
```

En otra terminal:

```sh
npm --prefix src/makari-web ci
npm --prefix src/makari-web run dev
```

Abrir http://localhost:5173. Vite reenvía `/api` a http://localhost:5080.
Para otro puerto usar `API_PROXY_TARGET=http://localhost:5081 npm --prefix src/makari-web run dev`.
Esta variable configura el servidor de Vite; no se incluyen credenciales en el cliente.
En producción, el servidor web debe servir el build y reenviar `/api` al backend.
`vite preview` solo previsualiza el build; no es un despliegue de producción.

Para un celular físico se necesita servir el frontend con HTTPS confiable y acceso de red
(por ejemplo, un proxy HTTPS de desarrollo delante de Vite); `crypto.randomUUID()` requiere
contexto seguro. HTTP con la IP de la computadora no basta. Localhost funciona en escritorio.
No se desactiva la validación de certificados ni se configura CORS.

## Contratos verificados

- `GET /api/catalog` (sin parámetros; precios Visitor): `{ products: [{ id, name, category,
  variants: [{ id, name, sizeMl, price }] }] }`.
- `GET /api/sales/summary?date=YYYY-MM-DD` (fecha opcional; si se omite, usa la fecha local
  del negocio). Devuelve ventas, importes por método de pago, unidades y agrupación histórica
  por variante; ver [contrato](../Makari.Api/Features/Sales/GetDailySalesSummary/README.md).
- `POST /api/sales/quote`: `{ customerType: "Local"|"Visitor", items: [{ productVariantId, quantity }] }`.
  Devuelve `{ customerType, subtotal, discount, total, items }` con desglose por línea;
  no crea ventas. Ver [contrato completo](../Makari.Api/Features/Sales/QuoteSale/README.md).
- `POST /api/sales`: `{ idempotencyKey, customerType: "Local"|"Visitor",
  paymentMethod: "Cash"|"Transfer"|"Card", expectedTotal, items: [{ productVariantId, quantity }] }`.
- Respuesta `201` nueva / `200` repetida: `{ id, createdAtUtc, customerType,
  paymentMethod, total, registeredBy, idempotencyKey,
  items: [{ id, productVariantId, quantity, unitPrice, subtotal }] }`.
- `400`: validación ProblemDetails; `409`: conflicto de clave. Otros errores,
  timeout, JSON inválido o respuesta incompatible nunca se consideran éxito.
- `POST /api/cash-closes`: `{ idempotencyKey, businessDate, openingCash, countedCash,
  responsible, observations }`. Totales del cierre se calculan desde ventas persistidas;
  ver [contrato](../Makari.Api/Features/CashCloses/README.md).
- `GET /api/cash-closes?date=YYYY-MM-DD`: obtiene un cierre por fecha (404 si aún está abierta).
- `GET /api/cash-closes/{id}`: obtiene el comprobante por folio.

La API es la autoridad de precios. El cliente calcula el estimado en centavos y valida
que la respuesta corresponda a la clave, cliente, pago, variantes y cantidades enviadas.
Una diferencia de importe produce 409 sin venta y requiere cotizar y confirmar de nuevo;
no se acepta como éxito un total distinto del esperado.

## Persistencia y recuperación

`localStorage["makari.checkout.v1"]` contiene `{ version: 1, draft, attempt?, receipt? }`.
El borrador guarda solo IDs de variantes, cantidades, descuento local y método de pago.
No guarda ni restaura precios del catálogo. La restauración valida versión, UUIDs,
cantidades, enums, importes del intento y coherencia entre borrador e intento.
Los datos inválidos se conservan sin sobrescribir y se bloquea el checkout para revisión.

Se reconcilia el borrador con el catálogo actual: las variantes ausentes no se eliminan
silenciosamente; bloquean un nuevo registro y el operador puede quitarlas explícitamente.
Un intento ya enviado nunca se modifica por cambios del catálogo. Se puede recuperar
incluso si el catálogo falla o ya no contiene esas variantes.

La solicitud exacta, `expectedTotal` e IdempotencyKey se guardan y verifican antes del
fetch. Timeout, desconexión, 5xx o respuesta ambigua conservan el intento y bloquean la
edición. «Recuperar venta» reenvía exactamente ese request sin generar una clave nueva.
Recargar o cerrar la pestaña no lo pierde. No existe un botón para descartar un intento
incierto. Un conflicto de idempotencia requiere revisar la clave con el administrador.

Un rechazo definitivo reconocido (`price_changed` o `sale_validation_failed`, misma clave)
libera el intento pero conserva el borrador. Un precio actualizado se muestra como aviso,
se vuelve a cotizar y se exige otra confirmación. La nueva clave se genera al confirmar.
Solo una respuesta de venta validada sustituye atómicamente borrador/intento por un
comprobante con folio y total. Si ese guardado falla, se mantiene la recuperación.
El comprobante permanece tras recargar hasta editar el siguiente pedido.

Web Locks mantiene un único editor por origen entre pestañas. Las demás esperan y leen
el estado actualizado al obtener el bloqueo. Se requiere HTTPS confiable o localhost,
Web Locks, localStorage habilitado y espacio disponible; no se envía una venta sin
persistir antes su solicitud. Cerrar la pestaña libera el bloqueo del navegador.

El cierre usa `localStorage["makari.cash-close.v1"]` con esquema versionado. Guarda el
request exacto y su IdempotencyKey antes del POST; un resultado incierto conserva ambos y
bloquea la edición. Solo un comprobante de API validado elimina el intento. Al recuperar la
página se consulta el servidor por fecha y se muestra el cierre ya confirmado si existe.

La fecha operativa usa `Business:TimeZoneId`; la UI y la API impiden cerrar fechas futuras.
El efectivo esperado es fondo inicial más ventas Cash. Transferencias quedan fuera del
efectivo físico. La diferencia puede ser positiva o negativa y el cierre no se puede editar.

## Importes y descuento local

Visitor es el tipo por defecto; el control de descuento cambia a Local. Ambos modos
se cotizan en el servidor antes de habilitar el registro. Cambiar productos, cantidades,
descuento o pago invalida la cotización visible. Se cancelan solicitudes anteriores y
se descartan respuestas que no correspondan al pedido actual.

El modal muestra el total a confirmar. CreateSale recibe ese importe como `expectedTotal`:
si cambió, no registra la venta y devuelve un rechazo definitivo con el importe actualizado.
El éxito muestra el folio y el total confirmado por CreateSale. Nunca se envían precios
por línea ni porcentajes de descuento.

## Límites operativos

La recuperación pertenece al mismo navegador y origen (protocolo, host y puerto).
Borrar datos del navegador, cambiar de dispositivo/origen, manipular localStorage o perder
el perfil puede eliminar la clave; localStorage no es un respaldo externo ni un registro
contable. No hay sincronización entre dispositivos ni recuperación administrativa automática.
No se deben editar manualmente datos de un intento para desbloquearlo.

La migración del backend debe aplicarse explícitamente antes de usar este frontend;
ver [CreateSale M0.7](../Makari.Api/Features/Sales/CreateSale/README.md). No se aplicó en esta tarea.

## Verificación

```sh
npm --prefix src/makari-web run build
npm --prefix src/makari-web run lint
npm --prefix src/makari-web test
dotnet build MakariPOS.slnx -m:1
dotnet test MakariPOS.slnx --no-build -m:1
```

Las pruebas usan el runner de Node 24; `tsx` carga TypeScript/TSX y `jsdom` permite
probar componentes React con eventos DOM y foco. Cubren contratos, cálculo,
carrito vacío, cambios de cliente/pago/cantidad, claves de reintento, respuestas ambiguas,
precio confirmado y errores HTTP. La suite de cierre incluye pruebas de renderizado de componentes.

Comprobación histórica M0.6: catálogo sin parámetros, Visitor sin descuento, activar/desactivar
descuento, cambiar cantidades, confirmación Local y error de cotización con API detenida.
La creación exitosa Local/Visitor y reintento sin duplicación se prueban en integración
sobre SQLite aislado; no se crean nuevas ventas en PostgreSQL durante esta verificación.
Las pruebas Node incluyen respuestas tardías tras cancelación, errores de red/HTTP,
validación de cotizaciones y conservación de claves. El cierre cuenta con pruebas DOM automatizadas desde M0.9.1.

## Resultado de verificación M0.4

- AddSales (`20261020220102_AddSales`) verificada en `__EFMigrationsHistory` de `makari_dev`.
- Build y lint frontend correctos; 7 pruebas Node y 78 pruebas .NET correctas.
- Chrome: catálogo real Local/Visitante, agregar/quitar, cálculo, transferencia,
  confirmación, bloqueo durante envío, éxito y vaciado de carrito verificados.
- Revisado en escritorio y viewport móvil de 390 × 844; error 502 del catálogo observado
  con registro deshabilitado antes de iniciar la API.
- Venta real de prueba: `bc6cf427-5da4-4182-9b8d-07268bff6a10`, $75 MXN,
  Visitante/Transferencia, un Cold Brew Cítrico, operador `M0.4-PRUEBA-UI`.
  Es un registro de prueba, sin cobro real; se conserva en la base local.
- La pérdida de red durante POST y el reintento no se simularon manualmente;
  la clave estable y el rechazo de respuestas ambiguas tienen pruebas automatizadas.

## Resultado M0.7

- API: build correcto, 112 pruebas .NET aprobadas, incluidas las suites previas.
- Frontend: build y lint correctos; 22 pruebas Node aprobadas.
- Chrome aislado en origen/servidor simulados: recarga de borrador Local/Transfer,
  corte de respuesta, bloqueo, recarga de intento con la misma clave, reintento exitoso
  con folio y total, y HTTP 409 por precio actualizado seguido de nueva cotización.
- El simulador no modifica PostgreSQL. La migración fue generada y validada con el
  snapshot; sigue sin aplicar. Ver [pruebas PostgreSQL pendientes](../Makari.Api/Features/Sales/CreateSale/README.md).

## M0.9.1 — Estado del formulario de cierre

El resumen y el cierre remoto se publican juntos; el borrador editable y los estados de
carga/envío se mantienen separados. Editar fondo, conteo, responsable u observaciones
persiste el borrador y recalcula la diferencia sin consultar la API. La carga ocurre al
entrar, seleccionar fecha o consultar de nuevo explícitamente. Las respuestas canceladas
se descartan incluso si llegan tarde. Solo `404` con `code: cash_close_not_found`
representa una fecha sin cierre; otros errores bloquean la creación.

Un importe vacío o con punto decimal final se conserva como texto de borrador; confirmar
sigue exigiendo importes válidos. Los intentos pendientes mantienen la validación estricta
y la misma solicitud/IdempotencyKey. Un comprobante existente se muestra solo lectura,
y permite consultar otra fecha cuando no hay un envío o intento pendiente.

Verificación: build y lint correctos; 36 pruebas frontend aprobadas, incluidas 7 pruebas
DOM del cierre (edición, foco, GET, errores 404, fechas, respuestas tardías, confirmación
y recuperación). En Chrome, con API simulada porque el proxy del entorno devolvía 502,
se escribió por tecla fondo 300 y conteo 400 con ventas Cash 50: esperado 350 y diferencia
50, sin loading ni GET adicionales. No se confirmaron cierres contra datos comerciales.

M1.1 incorpora Tarjeta en nueva venta, resumen y cierre. Los borradores y POST pendientes
conservan Card y su clave al recargar. `cardRevenue` y `cardSales` muestran el ingreso
electrónico separado; solo Cash se suma al fondo inicial para el efectivo esperado.

M1.2 simplificado añade Comprador con input vacío, chips V/L y hasta cinco frecuentes.
Escribir no consulta la API: CreateSale resuelve/crea al confirmar. El username y la clave
persisten antes del POST, y el formulario se bloquea durante un resultado incierto.
Tras confirmar se restablece Visitante. No hay búsqueda ni creación previa.
[Contratos y verificación](../../docs/M1.2-customers.md).
