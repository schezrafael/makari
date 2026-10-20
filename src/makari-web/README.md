# Makari POS — frontend

React + TypeScript + Vite. Pantalla inicial estática, sin funcionalidades de negocio.

Desde la raíz del repositorio:

```sh
npm --prefix src/makari-web ci
npm --prefix src/makari-web run dev
npm --prefix src/makari-web run lint
npm --prefix src/makari-web run build
```

El servidor de desarrollo escucha en http://localhost:5173.
Las futuras funcionalidades se organizarán en `src/features/<feature>` cuando se implementen.
