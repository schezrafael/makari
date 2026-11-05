import assert from 'node:assert/strict'
import { afterEach, test } from 'node:test'
import { JSDOM } from 'jsdom'
import { act, createElement } from 'react'

const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost' })
Object.assign(globalThis, { window: dom.window, document: dom.window.document, IS_REACT_ACT_ENVIRONMENT: true, FormData: dom.window.FormData })
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: dom.window.navigator })
const { createRoot } = await import('react-dom/client')
const { default: CatalogPage } = await import('../src/features/catalog/CatalogPage.tsx')
let root: ReturnType<typeof createRoot>
afterEach(async () => { if (root) await act(async () => root.unmount()) })
const button = (text: string) => [...document.querySelectorAll('button')].find(x => x.textContent === text)!
async function click(text: string) { await act(async () => button(text).click()) }
async function mount() { root = createRoot(document.getElementById('root')!); await act(async () => { root.render(createElement(CatalogPage)) }) }
function input(name: string, value: string) { document.querySelector<HTMLInputElement>(`[name="${name}"]`)!.value = value }
async function submit() { await act(async () => { document.querySelector('form')!.dispatchEvent(new dom.window.Event('submit', { bubbles: true, cancelable: true })) }) }
const product = { id: 'p1', name: 'Cold Brew', category: 'Café', version: 'v1', isActive: true, variants: [{ id: 'v1', name: 'Natural', sizeMl: 350, visitorPrice: 60, localPrice: 50, isActive: true }] }

test('lista activos/inactivos y crea bebida, refrescando desde la API', async t => {
  let products = [{ ...product, isActive: false }]
  let gets = 0
  t.mock.method(globalThis, 'fetch', async (_url: string, options: RequestInit) => {
    if (options.method === 'GET') { gets++; return Response.json(products) }
    assert.equal(options.method, 'POST')
    const body = JSON.parse(options.body as string)
    assert.equal(body.name, 'Té'); assert.equal(body.category, 'Infusiones')
    products = [{ ...product, name: body.name }]
    return Response.json(products[0], { status: 201 })
  })
  await mount(); assert.match(document.body.textContent!, /Cold Brew/); assert.match(document.body.textContent!, /Inactiva/)
  await click('Nueva bebida'); input('name', 'Té'); input('category', 'Infusiones'); await submit()
  assert.equal(gets, 2); assert.match(document.body.textContent!, /Cambios guardados/); assert.match(document.body.textContent!, /Té/)
})

test('edita bebida, precios y desactiva variante usando la versión actual', async t => {
  let current = structuredClone(product)
  const paths: string[] = []
  t.mock.method(globalThis, 'fetch', async (url: string, options: RequestInit) => {
    if (options.method === 'GET') return Response.json([current])
    const body = JSON.parse(options.body as string)
    assert.equal(body.version, current.version)
    assert.equal(options.method, 'PUT'); paths.push(url)
    if (url.endsWith('/prices')) { current.variants[0].visitorPrice = body.visitorPrice; current.variants[0].localPrice = body.localPrice }
    else if (url.includes('/variants/')) current.variants[0] = { ...current.variants[0], ...body }
    else current = { ...current, ...body }
    current.version += 'x'
    return Response.json(current)
  })
  await mount(); await click('Editar bebida'); input('name', 'Renombrada'); await submit()
  assert.match(document.body.textContent!, /Renombrada/)
  await click('Editar precios'); input('visitorPrice', '75.50'); input('localPrice', '0'); await submit()
  assert.equal(current.variants[0].visitorPrice, 75.5); assert.equal(current.variants[0].localPrice, 0)
  await click('Editar variante'); input('name', 'Cítrica'); input('sizeMl', '500'); document.querySelector<HTMLInputElement>('[name="isActive"]')!.checked = false; await submit()
  assert.equal(current.variants[0].isActive, false); assert.match(document.body.textContent!, /Inactiva/)
  assert.deepEqual(paths, ['/api/products/p1', '/api/products/p1/variants/v1/prices', '/api/products/p1/variants/v1'])
})

test('crea variante con ambos precios', async t => {
  t.mock.method(globalThis, 'fetch', async (url: string, options: RequestInit) => {
    if (options.method === 'GET') return Response.json([product])
    assert.equal(url, '/api/products/p1/variants'); assert.equal(options.method, 'POST')
    assert.deepEqual(JSON.parse(options.body as string), { version: 'v1', name: 'Grande', isActive: false, sizeMl: 500, visitorPrice: 90, localPrice: 70 })
    return Response.json(product, { status: 201 })
  })
  await mount(); await click('Agregar variante'); input('name', 'Grande'); input('sizeMl', '500'); input('visitorPrice', '90'); input('localPrice', '70'); await submit()
  assert.match(document.body.textContent!, /Cambios guardados/)
})

test('conserva formulario ante conflicto o error y permite recargar', async t => {
  t.mock.method(globalThis, 'fetch', async (_url: string, options: RequestInit) => options.method === 'GET' ? Response.json([product]) : Response.json({ detail: 'El producto cambió. Recarga el catálogo.' }, { status: 409 }))
  await mount(); await click('Editar bebida'); input('name', 'Pendiente'); await submit()
  assert.match(document.querySelector('[role="alert"]')!.textContent!, /El producto cambió/)
  assert.equal(document.querySelector<HTMLInputElement>('[name="name"]')!.value, 'Pendiente')
  await click('Recargar catálogo'); assert.equal(document.querySelector('form'), null)
})

test('error inicial permite reintentar la carga', async t => {
  let offline = true
  t.mock.method(globalThis, 'fetch', async () => { if (offline) throw new Error('offline'); return Response.json([product]) })
  await mount(); assert.match(document.querySelector('[role="alert"]')!.textContent!, /No se pudo cargar/)
  offline = false; await click('Recargar catálogo'); assert.match(document.body.textContent!, /Cold Brew/)
})
