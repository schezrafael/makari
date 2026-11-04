# Makari POS · M1.0

Sistema visual aplicado a las tres pantallas existentes. No cambia contratos,
precios, persistencia, cotización ni lógica de recuperación.

## Archivos

- `src/components/AppShell.tsx`: AppShell, AppHeader y Navigation. Navegación
  superior táctil en móvil para no competir con la barra inferior de cobro.
- `src/components/ui.tsx`: Button, PageHeader, MetricCard, MoneyDisplay y
  StatusMessage. Se mantienen los labels nativos del cierre y sus estados.
- `src/App.tsx`: integración del shell.
- `src/features/sales/SalesPage.tsx`: encabezado operativo y catálogo compacto.
- `src/features/sales/DailySalesPage.tsx`: encabezado y tarjetas compartidas.
- `src/index.css`: tokens de color, espaciado 4/8/12/16/24/32, radios 8/12/16,
  tamaños táctiles, foco, importes tabulares y adaptación responsive.
- `index.html`: favicon, color del navegador y carga de DM Sans.
- `public/brand/palm.png` y `public/brand/wordmark.png`: copias binarias de los
  originales de `src/assets`. SHA-1 verificado; sin edición ni efectos.

El encuadre CSS del encabezado oculta únicamente márgenes vacíos de las imágenes;
conserva la proporción. El favicon usa el archivo original de la palmera.
DM Sans se carga desde Google Fonts con `display=swap` y fallback `system-ui`.
Sin conexión externa se utiliza el fallback.

## Verificación

Desde la raíz:

```sh
npm --prefix src/makari-web run build
npm --prefix src/makari-web run lint
npm --prefix src/makari-web test
```

Resultado: build y lint correctos; 36 pruebas aprobadas. Incluyen cotización,
price_changed, persistencia e idempotencia, recuperación de POST pendiente,
404 cash_close_not_found y edición de cierre sin GET ni pérdida de foco.

Revisión en Chrome: 1440 × 1000 y 390 × 844, sin desbordamiento horizontal.
Capturas completas en `../../artifacts/m1-ui/`:

| Pantalla | Escritorio | Móvil |
| --- | --- | --- |
| Nueva venta | sale-desktop.png | sale-mobile.png |
| Ventas del día | daily-desktop.png | daily-mobile.png |
| Cierre de caja | close-desktop.png | close-mobile.png |

`error-mobile.png` muestra un error de catálogo. Las capturas móviles completas
muestran la barra fija de cobro en la posición correspondiente al viewport inicial;
el contenido inferior se accede desplazando la página.

El navegador emplea fixtures HTTP aislados, identificados como datos de prueba:
no se agregan precios ni catálogo estático a la aplicación. Se verifican navegación,
descuento local, confirmación explícita, respuesta HTTP 409, fallo de red y
recuperación tras recarga con request idéntico, ausencia de GET al editar el cierre,
y estados de catálogo vacío/error. La suite de componentes cubre el POST de cierre.

El guion de revisión se entrega en `../../artifacts/m1-ui/visual-check.cjs`.
Para repetirlo con Vite iniciado en 127.0.0.1:5173, Chrome instalado y Playwright
proporcionado externamente (sin nueva dependencia del frontend):

```sh
PLAYWRIGHT_MODULE=/ruta/a/playwright node artifacts/m1-ui/visual-check.cjs
```

## Pendientes

El backend no estaba disponible en localhost:5080. Falta una comprobación integral
con API y base de datos reales. No se registraron ventas ni cierres reales.
