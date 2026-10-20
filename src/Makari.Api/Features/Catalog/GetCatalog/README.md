# GetCatalog — M0.2.3

```http
GET /api/catalog?customerType=Visitor
GET /api/catalog?customerType=Local
```

`customerType` es obligatorio y admite exactamente `Local` o `Visitor`, con
esas mayúsculas. Valores ausentes, vacíos, numéricos, combinados o desconocidos
devuelven HTTP 400 con un problema de validación para `customerType`.

`Endpoint` valida y entrega el resultado; `Handler` recibe `MakariDbContext`
directamente y realiza una proyección SQL sin tracking ni carga de entidades.
Los DTOs de `Response` definen el contrato público.

- Solo productos activos y variantes activas con precio para el tipo solicitado.
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
