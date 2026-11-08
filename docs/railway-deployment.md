# Railway — un servicio web con Docker

Estado: archivos preparados y publicación local verificada. **No listo para operación
pública**: los endpoints de ventas, catálogo, clientes y cierre no tienen autenticación
ni autorización en este repositorio. No se ha verificado una capa externa de acceso.
No se realizó un despliegue ni se habilitaron dominios públicos.

## Imagen

El Dockerfile de la raíz compila Vite con Node 24 (el lockfile exige al menos 24.15.0;
`npm ci --engine-strict` valida compatibilidad), publica Makari.Api con SDK 10.0.203
alineado con global.json y copia `dist` a `wwwroot` en la imagen ASP.NET 10.
Solo el runtime y los artefactos publicados llegan a la imagen final. Se ejecuta como
el usuario no root incluido en la imagen Microsoft. No necesita start.sh ni Node en
runtime. Actualizar el SDK y global.json juntos cuando se adopte otro parche/banda.

.dockerignore permite solo las fuentes de ambos proyectos y global.json; excluye
bin/obj, node_modules/dist, pruebas, configuraciones por entorno, .env, claves y
credenciales locales. appsettings.json contiene solo configuración común. No colocar
secretos allí ni en archivos fuente. Las variables VITE_* serían públicas en el bundle;
no se necesitan para la API y nunca deben contener secretos.

Program.cs sirve estáticos de wwwroot y fallback SPA solo para GET/HEAD sin extensión,
fuera de /api y /health. Excluir esos prefijos del matching conserva 404/405 del servidor.
Los assets inexistentes no devuelven index.html. Se usa UseStaticFiles porque wwwroot
se copia después de publicar, sin depender de un manifiesto generado durante el build.

El frontend ya usa rutas absolutas al mismo origen `/api/...`. El proxy de Vite solo
se usa en desarrollo; en Docker el mismo proceso sirve frontend y API. La navegación
actual de React usa estado, no un router de URLs: `/catalogo` o `/ventas/hoy` cargan
la SPA, pero no seleccionan automáticamente esa pantalla. Esto no cambia la navegación
existente ni agrega contratos de deep links.

## Configuración precisa en Railway

1. Usar el servicio conectado al repositorio y la rama que contenga estos cambios.
   No crear un servicio independiente para React. PostgreSQL permanece como servicio
   de datos separado en el mismo proyecto/entorno.
2. En Settings → Source, usar la **raíz del repositorio** como Root Directory (`/` o
   campo vacío según la UI), no `src/Makari.Api` ni `src/makari-web`.
3. Railway detecta el archivo raíz `Dockerfile`. Si hay una selección explícita de
   builder, usar Dockerfile; quitar cualquier Build Command de Railpack/Nixpacks.
   El log debe mostrar las etapas Docker, no la detección automática de start.sh.
   Si existe RAILWAY_DOCKERFILE_PATH apuntando a otro archivo, quitarla o usar
   `Dockerfile`. [Referencia oficial](https://docs.railway.com/builds/dockerfiles).
4. En Settings → Deploy, **borrar el Start Command personalizado** que invoque
   `start.sh`, npm o dotnet run. Dejarlo vacío para usar
   `ENTRYPOINT ["dotnet", "Makari.Api.dll"]`. No configurar pre-deploy commands que
   migren o siembren datos automáticamente.
5. Agregar las variables siguientes en el servicio web, nunca en Git ni como ARG del
   Dockerfile. Aplicar los cambios de variables junto con el siguiente despliegue.
6. Configurar Healthcheck Path `/health`; usar el PORT asignado al servicio. Railway
   proporciona PORT y lo usa para el healthcheck; la aplicación escucha HTTP en ese
   puerto sobre todas las interfaces. Sin PORT, la imagen usa 8080. Un PORT inválido
   falla al iniciar con un error explícito.
   [Healthchecks de Railway](https://docs.railway.com/deployments/healthchecks).
7. **Mantener solo red privada**: no generar dominio Railway, dominio personalizado
   ni proxy TCP para el servicio web hasta resolver el acceso. Revisar y desactivar
   cualquier entrada pública previa antes de desplegar datos reales sin protección.
   La red privada es servicio-a-servicio dentro del entorno; no da acceso directo
   desde un teléfono. [Red privada de Railway](https://docs.railway.com/networking/private-networking).
8. Revisar el log: entorno Production, escucha en PORT y respuesta del healthcheck.
   `/health` solo certifica que el proceso responde, no conectividad ni esquema de
   PostgreSQL. No usar ese resultado como autorización para abrir el POS al público.

## Variables

| Variable del servicio web | Valor / propósito |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` (también definido en la imagen) |
| `DOTNET_ENVIRONMENT` | `Production`; evitar un override Development |
| `PORT` | Inyectado por Railway. Si se fija manualmente, usar un puerto no privilegiado, por ejemplo 8080 |
| `ConnectionStrings__Makari` | Cadena Npgsql del PostgreSQL autorizado, obligatoria para operar |
| `Business__TimeZoneId` | `America/Mexico_City`, o la zona comercial acordada |
| `Sales__RegisteredBy` | Nombre del operador configurado, 1–100 caracteres; el valor actual por defecto es Andi |

ConnectionStrings__DefaultConnection es una alternativa legado, pero Makari tiene
precedencia. Preferir una sola variable para evitar destinos ambiguos. La API no
convierte DATABASE_URL automáticamente ni construye una conexión a partir de PGHOST.
Usar el formato de Npgsql `Host=...;Port=...;Database=...;Username=...;Password=...;`.
[Parámetros Npgsql](https://www.npgsql.org/doc/connection-string-parameters.html).

Si el servicio de base se llama `Postgres`, crear ConnectionStrings__Makari con estas
**referencias**, sin copiar valores reales a archivos:

```text
Host=${{Postgres.RAILWAY_PRIVATE_DOMAIN}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}};
```

Cambiar `Postgres` por el nombre real del servicio y comprobar que las variables
referenciadas existan. Si los valores contienen punto y coma u otros caracteres
especiales, entrecomillarlos según el formato Npgsql. Mantener la política TLS/CA
apropiada al proveedor; no agregar Trust Server Certificate=true como solución
universal. Para conexiones externas, configurar validación del certificado y TLS
según el proveedor. No usar el host/puerto público del proxy si se dispone del privado.
[Referencias entre variables](https://docs.railway.com/variables).

El runtime estándar conserva soporte de globalización y zonas horarias. No activar
invariant globalization: el cierre y resumen usan la zona comercial configurada.
No se agregaron redirecciones HTTPS dentro del contenedor: Railway termina TLS en
su ingreso; la aplicación escucha HTTP internamente.

## Acceso antes de producción

No hay RequireAuthorization, UseAuthentication ni UseAuthorization en la API actual.
El login al dashboard de Railway y HTTPS no autentican las peticiones al POS.

Antes de habilitar usuarios reales, implementar autenticación/autorización en la
aplicación o proporcionar un acceso privado/gateway autenticado que proteja tanto
el HTML como **todo /api**, en todos sus métodos. Si se usa gateway, impedir el bypass
por un dominio Railway, dominio de origen o puerto público directo. No basta con
proteger solo el frontend o esconder los botones.

Verificar desde fuera de la sesión autorizada que ventas, catálogo, clientes y cierres
no sean accesibles: rechazo 401/403, red privada inaccesible o redirección obligatoria
a un flujo autenticado. Verificar también los nombres alternativos del origen.
Conservar `/health` mínimo para la plataforma. La presente tarea no incorpora un
sistema de identidad ni afirma haber verificado una capa privada del despliegue real.

## Esquema y datos

El contenedor no migra ni siembra al iniciar. Antes de operar, preparar explícitamente
el esquema de una base autorizada con las migraciones que correspondan a la versión.
No usar makari_dev ni makari_test para estas verificaciones sin autorización.
No se modificaron reglas comerciales, contratos HTTP, precios ni ventas históricas.
No almacenar PostgreSQL ni datos persistentes dentro de este contenedor web.

## Verificación reproducible

Desde la raíz:

```sh
dotnet build MakariPOS.slnx
env -u MAKARI_TEST_CONNECTION dotnet test MakariPOS.slnx --no-build
npm --prefix src/makari-web ci
npm --prefix src/makari-web run build
npm --prefix src/makari-web run lint
npm --prefix src/makari-web test
docker build -t makari-pos:railway .
```

Smoke test de contenedor, con puerto local y conexión ficticia a un puerto cerrado:

```sh
docker run --rm -d --name makari-railway-smoke \
  -p 127.0.0.1:18080:8080 \
  -e PORT=8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e 'ConnectionStrings__Makari=Host=127.0.0.1;Port=1;Database=makari_smoke;Username=unused;Timeout=1' \
  makari-pos:railway

docker logs makari-railway-smoke
curl -i http://127.0.0.1:18080/health
curl -i http://127.0.0.1:18080/
curl -i http://127.0.0.1:18080/catalogo
curl -i http://127.0.0.1:18080/ventas/hoy
curl -I http://127.0.0.1:18080/catalogo
curl -i http://127.0.0.1:18080/api/no-existe
curl -i -X DELETE http://127.0.0.1:18080/api/sales
curl -i -H 'Content-Type: application/json' -d '{}' http://127.0.0.1:18080/api/sales
curl -i -H 'Content-Type: application/json' -d '{}' http://127.0.0.1:18080/api/sales/quote
curl -i http://127.0.0.1:18080/assets/missing.js

docker stop makari-railway-smoke
```

Esperado: health 200 JSON; raíz/rutas SPA 200 HTML; HEAD sin cuerpo; ruta API inexistente
404; DELETE de una ruta POST 405; requests vacíos 400; asset inexistente 404. Copiar las
URLs `/assets/...` del HTML y verificar JS/CSS 200. Estas llamadas no necesitan una
base funcional ni realizan escrituras. Las pantallas de negocio mostrarán errores de
conexión hasta configurar una base autorizada; ese smoke test no verifica PostgreSQL.
Repetir con otro PORT y el mapeo de contenedor correspondiente para comprobarlo.

## Resultado de esta sesión

- Build .NET: correcto, 0 advertencias y 0 errores.
- Backend: 172 aprobadas, 8 PostgreSQL opt-in omitidas.
- Frontend: build y lint correctos; 59 pruebas aprobadas.
- Publish Release: correcto. Se copiaron los assets reales a wwwroot en una carpeta
  temporal y se inició el artefacto en Production con PORT=18081.
- HTTP directo al artefacto: health/raíz/SPA/HEAD/JS/CSS correctos; 404/405/400 de API
  preservados, sin fallback HTML. Pruebas adicionales comprueban /API y evitan servir
  archivos incluso si existiera wwwroot/api/hidden.json.
- `docker build` y `docker run`: intentados, no ejecutables (`docker: command not found`).
  **La imagen y sus capas Linux no están verificadas todavía**; validar en un equipo
  con Docker o en el build de Railway antes de considerar resuelto el despliegue.
- No se accedió a una base real, no se aplicaron migraciones, no se hicieron commits.

Pendientes para producción: verificar la imagen, desplegar de forma privada, confirmar
conexión/esquema autorizados y establecer/verificar protección efectiva del POS.
