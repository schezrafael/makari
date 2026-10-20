# Makari POS

## Alcance y arquitectura

- Stack: .NET 10, Minimal APIs, EF Core + PostgreSQL; React, TypeScript y Vite.
- Organizar funcionalidades por slice en `src/Makari.Api/Features/<Feature>`.
- Mantener endpoint, validación y lógica de cada caso de uso juntos cuando existan.
- Reservar `Infrastructure` para detalles técnicos compartidos, como persistencia.
- Organizar pruebas por feature en `tests/Makari.Api.Tests/Features`.
- Mantener una solución simple; evitar capas y abstracciones sin una necesidad concreta.
- No introducir MediatR, frameworks CQRS ni repositorios genéricos.
- M0.1 solo incluye infraestructura y health; no agregar entidades de negocio ni migraciones en este incremento.

## Seguridad y colaboración

- Conservar archivos y cambios del usuario; pedir autorización antes de sobrescribirlos o eliminarlos.
- No guardar credenciales en Git. Usar variables de entorno o secretos locales.
- No modificar bases de datos ni ejecutar migraciones automáticamente al iniciar.
- No agregar Docker ni realizar commits salvo petición explícita.
- `/health` comprueba que el proceso responde; no certifica disponibilidad de PostgreSQL.

## Verificación

Desde la raíz:

```sh
dotnet restore MakariPOS.slnx
dotnet build MakariPOS.slnx --no-restore
dotnet test MakariPOS.slnx --no-build
npm --prefix src/makari-web ci
npm --prefix src/makari-web run lint
npm --prefix src/makari-web run build
```

Agregar pruebas de integración para nuevos endpoints. Mantener la documentación y
los comandos de ejecución actualizados al cambiar la configuración.
