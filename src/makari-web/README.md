# Makari POS — M0.7 Reliable Checkout

Interfaz React + TypeScript para registrar ventas desde celular, tablet o escritorio.
Catálogo Visitor, cantidades, descuento local, resumen, efectivo/transferencia y confirmación modal.
El total estimado permanece fijo en móvil; el éxito muestra el total confirmado por la API.

## Ejecutar

Desde la raíz, con PostgreSQL configurado y las migraciones InitialCatalog, AddSales y AddRejectedSaleAttempts
aplicadas explícitamente (el arranque no migra ni siembra):

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
- `POST /api/sales/quote`: `{ customerType: "Local"|"Visitor", items: [{ productVariantId, quantity }] }`.
  Devuelve `{ customerType, subtotal, discount, total, items }` con desglose por línea;
  no crea ventas. Ver [contrato completo](../Makari.Api/Features/Sales/QuoteSale/README.md).
- `POST /api/sales`: `{ idempotencyKey, customerType: "Local"|"Visitor",
  paymentMethod: "Cash"|"Transfer", expectedTotal, items: [{ productVariantId, quantity }] }`.
- Respuesta `201` nueva / `200` repetida: `{ id, createdAtUtc, customerType,
  paymentMethod, total, registeredBy, idempotencyKey,
  items: [{ id, productVariantId, quantity, unitPrice, subtotal }] }`.
- `400`: validación ProblemDetails; `409`: conflicto de clave. Otros errores,
  timeout, JSON inválido o respuesta incompatible nunca se consideran éxito.

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

Las pruebas usan el runner de Node 24, sin dependencias nuevas. Cubren contratos, cálculo,
carrito vacío, cambios de cliente/pago/cantidad, claves de reintento, respuestas ambiguas,
precio confirmado y errores HTTP. No son pruebas de renderizado de componentes.

Comprobación histórica M0.6: catálogo sin parámetros, Visitor sin descuento, activar/desactivar
descuento, cambiar cantidades, confirmación Local y error de cotización con API detenida.
La creación exitosa Local/Visitor y reintento sin duplicación se prueban en integración
sobre SQLite aislado; no se crean nuevas ventas en PostgreSQL durante esta verificación.
Las pruebas Node incluyen respuestas tardías tras cancelación, errores de red/HTTP,
validación de cotizaciones y conservación de claves. No hay pruebas DOM automatizadas.

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
