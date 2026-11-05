import assert from 'node:assert/strict'
import { afterEach, test } from 'node:test'
import { JSDOM } from 'jsdom'
import { act, createElement } from 'react'
import { restoreCheckout } from '../src/features/sales/checkout.ts'

const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost' })
Object.assign(globalThis, { window: dom.window, document: dom.window.document, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { userAgent: 'jsdom', locks: { request: async (_name: string, _options: unknown, callback: () => Promise<void>) => callback() } } })
dom.window.HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
dom.window.HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
const { createRoot } = await import('react-dom/client')
const { default: SalesPage } = await import('../src/features/sales/SalesPage.tsx')
const { default: DailySalesPage } = await import('../src/features/sales/DailySalesPage.tsx')
let root: ReturnType<typeof createRoot>
async function mount(page = SalesPage) {
  root = createRoot(document.getElementById('root')!)
  await act(async () => { root.render(createElement(page)) })
}
const button = (label: string) => [...document.querySelectorAll('button')].find(item => item.textContent?.trim() === label)!
const id = '11111111-1111-4111-8111-111111111111'
afterEach(async () => { if (root) await act(async () => root.unmount()); localStorage.clear() })

test('selecciona Tarjeta, restaura selección y confirma Card desde la pantalla', async t => {
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    if (url === '/api/catalog') return Response.json({ products: [{ id, name: 'Cold Brew', category: 'Bebidas', variants: [{ id, name: 'Natural', sizeMl: 300, price: 60 }] }] })
    const request = JSON.parse(options!.body as string)
    if (url === '/api/sales/quote') return Response.json({ ...request, subtotal: 60, discount: 0, total: 60, items: [{ ...request.items[0], visitorUnitPrice: 60, unitPrice: 60, subtotal: 60, discount: 0, total: 60 }] })
    assert.equal(request.paymentMethod, 'Card')
    assert.deepEqual(restoreCheckout(localStorage).attempt, request)
    return Response.json({ ...request, id, createdAtUtc: '2026-11-04T12:00:00Z', registeredBy: 'Andi', total: 60, items: [{ ...request.items[0], id, unitPrice: 60, subtotal: 60 }] }, { status: 201 })
  })
  await mount()
  assert.equal(button('Efectivo').getAttribute('aria-pressed'), 'true')
  await act(async () => button('Tarjeta').click())
  assert.equal(button('Tarjeta').getAttribute('aria-pressed'), 'true')
  assert.equal(restoreCheckout(localStorage).draft.payment, 'Card')
  await act(async () => root.unmount())
  await mount()
  assert.equal(button('Tarjeta').getAttribute('aria-pressed'), 'true')
  await act(async () => document.querySelector<HTMLButtonElement>('[aria-label="Agregar Cold Brew, Natural"]')!.click())
  await act(async () => document.querySelector<HTMLButtonElement>('.checkout button')!.click())
  assert.match(document.querySelector('dialog')!.textContent!, /Tarjeta/)
  await act(async () => button('Confirmar y registrar').click())
  assert.match(document.body.textContent!, /Venta registrada/)
  assert.equal(restoreCheckout(localStorage).receipt?.sale.paymentMethod, 'Card')
})

test('resumen presenta Tarjeta con su importe independiente', async t => {
  t.mock.method(globalThis, 'fetch', async () => Response.json({ date: '2026-11-04', timeZoneId: 'America/Mexico_City', salesCount: 3, totalRevenue: 180, cashRevenue: 50, transferRevenue: 60, cardRevenue: 70, unitsSold: 3, variants: [] }))
  await mount(DailySalesPage)
  const card = [...document.querySelectorAll('.summary-card')].find(item => item.textContent?.includes('Tarjeta'))!
  assert.match(card.textContent!, /70/)
  assert.match(document.body.textContent!, /Efectivo/)
  assert.match(document.body.textContent!, /Transferencias/)
})
