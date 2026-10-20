# Makari POS — M0.1

Bootstrap del módulo de punto de venta de Makari Drinks. Sin entidades ni
funcionalidades de negocio, migraciones, Docker o cambios en bases de datos.
El `README.md` original de MakariOS se conserva íntegro.

## Requisitos

- .NET SDK 10.0.203 (o un parche posterior de la misma banda, según `global.json`).
- Node.js 24 LTS y npm 11. Verificado con Node 24.21.0 y npm 11.19.0.
- PostgreSQL no es necesario para compilar, ejecutar `/health` ni correr las pruebas.

## Preparación y verificación

Desde la raíz del repositorio:

```sh
dotnet restore MakariPOS.slnx --disable-parallel -m:1
dotnet build MakariPOS.slnx --no-restore -m:1
dotnet test MakariPOS.slnx --no-build -m:1
npm --prefix src/makari-web ci
npm --prefix src/makari-web run lint
npm --prefix src/makari-web run build
```

`-m:1` limita los procesos de MSBuild y facilita la ejecución en entornos restringidos.
`package-lock.json` fija la resolución de dependencias del frontend.

## Ejecución local

En una terminal:

```sh
dotnet run --project src/Makari.Api --launch-profile http
```

La API escucha en `http://localhost:5080`. Comprobación:

```sh
curl -i http://localhost:5080/health
```

Respuesta esperada: HTTP 200, `application/json`, `{"status":"ok"}`.
Es un endpoint de vida del proceso, sin comprobación de conexión a PostgreSQL.
El perfil HTTP está destinado al desarrollo local.

En otra terminal:

```sh
npm --prefix src/makari-web run dev
```

Frontend: `http://localhost:5173`. La pantalla es estática y todavía no consume
la API; no requiere CORS ni proxy. El build se genera en `src/makari-web/dist`.

## Persistencia

`MakariDbContext` está registrado con EF Core y el proveedor Npgsql, sin `DbSet`,
entidades ni migraciones. La cadena se lee de `ConnectionStrings:Makari`.
Antes de implementar acceso a datos, se puede configurar mediante una variable
de entorno (sustituir los marcadores por los valores del entorno local):

```sh
export ConnectionStrings__Makari='Host=localhost;Port=5432;Database=makari_pos;Username=<usuario>;Password=<secreto>'
```

No se incluye una contraseña en archivos versionados. El arranque no ejecuta
`EnsureCreated`, `Migrate` ni abre conexiones. La configuración de PostgreSQL
y su validación de conectividad quedan para un incremento que necesite persistencia.

## Organización

- `src/Makari.Api/Features/Health`: endpoint organizado como una slice vertical.
- `src/Makari.Api/Infrastructure/Persistence`: contexto y configuración técnica.
- `tests/Makari.Api.Tests/Features/Health`: prueba de integración mediante
  `WebApplicationFactory<Program>` y servidor en memoria.
- `src/makari-web/src`: entrada y pantalla inicial de React. Las futuras funcionalidades
  se ubicarán en `features/<feature>` al implementarse.

No se agregan capas de servicios, repositorios genéricos ni frameworks CQRS.

## Próximo incremento

Acordar el modelo del catálogo: producto, variante por sabor y presentación,
y precios para clientes locales y visitantes. Después implementar una primera
slice de consulta del catálogo con pruebas, y planificar explícitamente su
persistencia y primera migración.
