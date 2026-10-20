# PostgreSQL y catálogo inicial — M0.2.2

## Destino y autorización

No ejecutar los comandos de escritura sin confirmar servidor, base de datos y
autorización. La inspección inicial encontró PostgreSQL 18.6 en
`localhost:5432`, base `makari_dev`, usuario `rxfael`, sin tablas de usuario.
Esta observación no constituye autorización para modificarla.

La API busca `ConnectionStrings:Makari` y, si no existe, `DefaultConnection`.
`Makari` tiene prioridad. El archivo de desarrollo existente usa
`DefaultConnection` y no se modificó. Para que EF y el comando de seed utilicen
el mismo destino, configurar explícitamente `ConnectionStrings__Makari` en la
terminal. No guardar contraseñas en Git.

## Migración

`InitialCatalog` crea `Products`, `ProductVariants` y `ProductPrices`, sus claves
primarias UUID, relaciones obligatorias con `ON DELETE RESTRICT`, índice por
`ProductId` e índice único por `(ProductVariantId, CustomerType)`.

`Amount` usa `numeric(12,2)` en PostgreSQL. Los checks exigen importe y volumen
positivos y restringen `CustomerType` a Local = 0 y Visitor = 1.
La migración no contiene datos ni los gestiona con `HasData`.

Desde la raíz, los siguientes comandos solo preparan/verifican artefactos:

```sh
dotnet restore MakariPOS.slnx --disable-parallel -m:1
dotnet build MakariPOS.slnx --no-restore -m:1
dotnet ef migrations has-pending-model-changes --project src/Makari.Api --no-build
dotnet ef migrations script 0 InitialCatalog --project src/Makari.Api --no-build
```

Se requiere `dotnet-ef` 10.x; se verificó la versión global 10.0.8.
`Microsoft.EntityFrameworkCore.Design` 10.0.7 coincide con el runtime EF Core.
`Microsoft.EntityFrameworkCore.Relational` se fija también en 10.0.7 para que
la API y las pruebas resuelvan la misma versión al usar Design como dependencia
privada de herramientas.

**Solo después de autorizar el destino**, configurar la conexión y ejecutar:

```sh
# Ejemplo local sin contraseña; usar las credenciales del destino autorizado.
export ConnectionStrings__Makari='Host=localhost;Port=5432;Database=makari_dev;Username=rxfael'
dotnet ef database update InitialCatalog --project src/Makari.Api --no-build
dotnet run --project src/Makari.Api --no-build --no-launch-profile -- --seed-catalog
```

El seed no aplica migraciones ni crea la base. Se detiene si faltan migraciones.
El arranque normal de la API no ejecuta ninguna de estas operaciones.

## Datos y repetición

Producto: `Cold Brew`, categoría `Café`, inicialmente activo.
La moneda de los importes iniciales es MXN; el modelo aún no tiene una columna
de moneda y no se amplía en este incremento.

| Variante | Volumen | Visitor | Local |
|---|---:|---:|---:|
| Natural | 500 ml | 60 | 50 |
| Cítrico | 500 ml | 75 | 60 |

Los siete UUID nuevos tienen el prefijo `1ea4ad9a-f40c-4c65-9fab-`:

| Sufijo | Registro |
|---|---|
| `000000000001` | Cold Brew |
| `000000000002` | Natural |
| `000000000003` | Cítrico |
| `000000000004` | Natural / Visitor |
| `000000000005` | Natural / Local |
| `000000000006` | Cítrico / Visitor |
| `000000000007` | Cítrico / Local |

El seed busca primero las coincidencias por ID o nombre del producto, y por ID
o nombre y volumen de la variante. Compara nombres ignorando mayúsculas y
espacios externos. Si existen registros equivalentes con otros UUID, los
reutiliza sin reemplazar sus identificadores. Los precios se reconocen por
variante y tipo de cliente. No actualiza importes, nombres, categorías,
volúmenes o estados existentes.

Las coincidencias múltiples y los IDs asignados a otra relación provocan un
error y rollback. No intenta resolver catálogos ambiguos automáticamente.
No puede inferir equivalencias si un registro previo con ID distinto fue también
renombrado; esos casos requieren revisión manual antes del seed.

Cada ejecución usa una transacción y un bloqueo de las tres tablas que serializa
escrituras mientras resuelve coincidencias e inserta. Es un comando de
inicialización para un catálogo pequeño; carga el catálogo completo y puede
bloquear escrituras brevemente. No debe ejecutarse como parte de cada petición.

## Verificación posterior a una ejecución autorizada

Consultar `__EFMigrationsHistory` y comprobar los tres checks y las dos relaciones.
Un destino inicialmente vacío debe terminar con 1 producto, 2 variantes y 4
precios. Ejecutar el seed de nuevo debe conservar exactamente sus IDs y valores.

Las pruebas unitarias verifican el plan de inserciones, repetición, preservación
de precios/estados, reutilización de registros y detección de ambigüedades sin
abrir conexiones. La verificación contra PostgreSQL requiere autorización
explícita del destino y no se sustituye por esas pruebas.
