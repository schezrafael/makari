# Makari POS — M0.4 Sales UI

Interfaz React + TypeScript para registrar ventas desde celular, tablet o escritorio.
Catálogo por cliente, cantidades, resumen, efectivo/transferencia y confirmación modal.
El total estimado permanece fijo en móvil; el éxito muestra el total confirmado por la API.

## Ejecutar

Desde la raíz, con PostgreSQL configurado y las migraciones InitialCatalog y AddSales
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

- `GET /api/catalog?customerType=Local|Visitor`: `{ products: [{ id, name, category,
  variants: [{ id, name, sizeMl, price }] }] }`.
- `POST /api/sales`: `{ idempotencyKey, customerType: "Local"|"Visitor",
  paymentMethod: "Cash"|"Transfer", items: [{ productVariantId, quantity }] }`.
- Respuesta `201` nueva / `200` repetida: `{ id, createdAtUtc, customerType,
  paymentMethod, total, registeredBy, idempotencyKey,
  items: [{ id, productVariantId, quantity, unitPrice, subtotal }] }`.
- `400`: validación ProblemDetails; `409`: conflicto de clave. Otros errores,
  timeout, JSON inválido o respuesta incompatible nunca se consideran éxito.

La API es la autoridad de precios. El cliente calcula el estimado en centavos y valida
que la respuesta corresponda a la clave, cliente, pago, variantes y cantidades enviadas.
Un precio actualizado por la API se acepta y se muestra como total confirmado.

## Reintentos

Se genera una clave con `crypto.randomUUID()` al primer envío. Un fallo conserva
carrito y clave; reintentar envía el mismo pedido. Cambiar cantidades, tipo de cliente
o forma de pago invalida la clave anterior. Durante el envío se bloquean los controles
y se impiden envíos simultáneos. Solo un éxito validado vacía el carrito.

Ante un fallo de red, reintentar sin editar y sin volver a cobrar. La venta puede haberse
guardado aunque la respuesta no haya llegado. El estado se conserva en memoria: recargar,
cerrar la pestaña o navegar lo pierde. No hay recuperación entre sesiones ni modo offline.
Cambiar cliente conserva cantidades y carga sus precios; una variante sin precio bloquea
el cobro hasta volver al cliente anterior o quitar las variantes no disponibles.

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

Comprobación manual: cargar Local y Visitante, agregar/quitar variantes, revisar total,
abrir/cancelar confirmación, cambiar pago y confirmar. Probar API detenida para el error
de catálogo; cortar la conexión durante el registro y reintentar sin editar para comprobar
idempotencia. Para ventas de prueba iniciar la API con
`-- --Sales:RegisteredBy=M0.4-PRUEBA-UI` y conservar el folio del registro de prueba.

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
