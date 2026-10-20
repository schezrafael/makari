# Catálogo — M0.2.1

`Domain` contiene entidades sin dependencias de EF Core. `Persistence` contiene
las configuraciones Fluent API; el contexto las registra desde el ensamblado.

- Crear productos con `new Product(name, category)`, variantes con `AddVariant`
  y precios con `AddPrice`. Los identificadores se generan en el dominio.
- Nombres y categoría obligatorios, volumen positivo e importes positivos
  representables en `decimal(12,2)` sin redondeo.
- `CustomerType` se almacena como entero estable: Local = 0, Visitor = 1.
- `AddPrice` rechaza tipos duplicados en la colección cargada. Al usarlo sobre una
  variante persistida, cargar previamente `Prices`. El índice único compuesto
  protege también ante concurrencia o colecciones incompletas; un futuro caso de
  uso deberá traducir el error de persistencia correspondiente.
- `Deactivate` cambia solo `IsActive` de la entidad indicada, sin propagarlo a
  sus hijos. No hay filtros globales: los registros inactivos siguen disponibles
  para consultas históricas. Un futuro catálogo de venta deberá comprobar la
  actividad tanto del producto como de su variante.
- Las claves foráneas usan `Restrict` para evitar borrados en cascada. Esto no
  constituye una prohibición global de borrado físico; los futuros casos de uso
  deben utilizar la desactivación para retirar productos o variantes.
- Los checks de PostgreSQL validan volumen, importe y valores del enum; el índice
  único permite como máximo un precio por variante y tipo de cliente.

Las pruebas cubren el dominio, el modelo EF, el DDL generado y el seguimiento de
cambios sin abrir conexiones. No se ejecuta ese DDL, no se crean migraciones ni
se modifica ninguna base de datos. La aplicación real de las restricciones queda
pendiente para el incremento de migraciones y pruebas contra PostgreSQL.
