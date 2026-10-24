# GetCatalog — M0.5: precios base Visitor

```http
GET /api/catalog
```

El endpoint no requiere parámetros y devuelve exclusivamente precios `CustomerType.Visitor`.
El formato de `GetCatalogResponse` no cambia: `{ "products": [{ "id", "name",
"category", "variants": [{ "id", "name", "sizeMl", "price" }] }] }` (esquema ilustrativo).

El parámetro obsoleto `customerType` se **rechaza con HTTP 400** y
`application/problem+json`, con un error en `errors.customerType`, antes de consultar
la base. Se rechaza cualquier valor, incluidos `Local`, `Visitor`, vacío, sin valor
expreso o repetido; el nombre del parámetro no distingue mayúsculas de minúsculas.
Así se evita que un cliente solicite precios Local y reciba Visitor silenciosamente.
`customerType` ya no se anuncia como parámetro en OpenAPI.

`Endpoint` valida y entrega el resultado; `Handler` recibe `MakariDbContext`
directamente y realiza una proyección SQL sin tracking ni carga de entidades.
Los DTOs de `Response` definen el contrato público.

- Solo productos activos y variantes activas con precio Visitor; se excluyen las variantes con solo precio Local o sin precios.
- Cada variante expone un único `price`, sin listas de precios ni relaciones EF.
- Productos y variantes ordenados por nombre usando la collation de la base;
  el ID desempata nombres iguales. El catálogo inicial queda Cítrico, Natural.
- Un producto activo sin variantes elegibles permanece con `variants: []`.
- Sin productos activos, la respuesta HTTP 200 es `{"products":[]}`.
- Se propaga la cancelación de la petición a EF Core.

Las pruebas HTTP utilizan una base SQLite independiente en memoria por caso;
no conectan ni escriben en `makari_dev`. SQLite no sustituye la validación de
traducción SQL y collation de PostgreSQL. El proveedor de producción sigue siendo
Npgsql. No se agregan migraciones.

Dependencias exclusivas del proyecto de pruebas: EF Core SQLite 10.0.7 y
SQLitePCLRaw.bundle_e_sqlite3 3.0.5. Se fija el bundle actualizado para evitar
la dependencia nativa 2.1.11 con advertencia de vulnerabilidad que resuelve
por defecto el proveedor SQLite 10.0.7.

La consulta de `CatalogSeeder` usa `AsSplitQuery` dentro de la transacción y del
bloqueo de tablas existentes. Conserva tracking para insertar los datos faltantes;
la consulta de GetCatalog utiliza `AsNoTracking`. No se ejecuta el seed para
probar este endpoint.

## Compatibilidad

Este cambio rompe el consumo del frontend M0.4, que todavía envía
`?customerType=Local|Visitor`: recibirá HTTP 400 y mostrará error de catálogo.
M0.6 adapta el frontend a `/api/catalog` sin parámetros y aplica el descuento local
mediante `POST /api/sales/quote`; los precios base del catálogo siguen siendo Visitor.
El frontend M0.4 anterior continúa siendo incompatible.

`CreateSale` permanece intacto y sigue aceptando `CustomerType.Local` y
`CustomerType.Visitor`, calculando sus respectivos precios en el servidor.
Este incremento no implementa descuentos en la interfaz, no modifica `ProductPrice`
ni `CustomerType`, no genera migraciones y no modifica registros de PostgreSQL.

## Verificación

Desde la raíz:

```sh
dotnet restore MakariPOS.slnx
dotnet build MakariPOS.slnx --no-restore
dotnet test MakariPOS.slnx --no-build
```

Las pruebas cubren consulta sin parámetros, precios Visitor, rechazo del parámetro
obsoleto, productos y variantes inactivos, variantes sin precio Visitor, catálogo
vacío, orden por nombre y ausencia de tracking. Se ejecuta también la suite existente
de CreateSale sin modificarla.
