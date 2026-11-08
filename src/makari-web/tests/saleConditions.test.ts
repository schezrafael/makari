import assert from 'node:assert/strict'
import { afterEach, test } from 'node:test'
import { JSDOM } from 'jsdom'
import { act, createElement } from 'react'
import { emptyCheckout, prepareAttempt, restoreCheckout, updateDraft, checkoutKey } from '../src/features/sales/checkout.ts'
import { makeOrder, operation } from '../src/features/sales/model.ts'

const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost' })
Object.assign(globalThis, { window: dom.window, document: dom.window.document, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })
Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { userAgent: 'jsdom', locks: { request: async (_name: string, _options: unknown, callback: () => Promise<void>) => callback() } } })
dom.window.HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
dom.window.HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
const { createRoot } = await import('react-dom/client')
const { default: SalesPage } = await import('../src/features/sales/SalesPage.tsx')
const { default: DailySalesPage } = await import('../src/features/sales/DailySalesPage.tsx')
let root: ReturnType<typeof createRoot> | undefined
async function mount(page = SalesPage) { root = createRoot(document.getElementById('root')!); await act(async () => { root!.render(createElement(page)) }) }
async function unmount() { if (root) { await act(async () => root!.unmount()); root = undefined } }
afterEach(async () => { await unmount(); localStorage.clear() })
const button = (text: string) => [...document.querySelectorAll('button')].find(item => item.textContent?.trim() === text)!
async function click(text: string) { await act(async () => button(text).click()) }
async function add() { await act(async () => document.querySelector<HTMLButtonElement>('[aria-label="Agregar Bebida, Natural"]')!.click()) }
async function confirm() { await act(async () => document.querySelector<HTMLButtonElement>('.checkout button')!.click()); await click('Confirmar y registrar') }
const id = '11111111-1111-4111-8111-111111111111'
const buyerId = '22222222-2222-4222-8222-222222222222'
function fixture(url: string, options?: RequestInit): Response | undefined {
  if (url === '/api/catalog') return Response.json({ products: [{ id, name: 'Bebida', category: 'Bebidas', variants: [{ id, name: 'Natural', sizeMl: 350, price: 60 }] }] })
  if (url === '/api/customers/frequent?limit=5') return Response.json([{ id: buyerId, username: 'Ana', customerType: 'Local' }])
  if (url.startsWith('/api/customers?username=')) return Response.json({ id: buyerId, username: url.endsWith('local') ? 'local' : 'visitante', customerType: 'Visitor' })
  if (url === '/api/sales/quote') {
    const request = JSON.parse(options!.body as string)
    assert.equal(request.customerType, undefined)
    const price = request.saleCondition === 'LocalDiscount' ? 50 : ['Courtesy', 'Staff', 'Birthday'].includes(request.saleCondition) ? 0 : 60
    const quantity = request.items[0].quantity
    return Response.json({ ...request, customerType: request.saleCondition === 'LocalDiscount' ? 'Local' : 'Visitor', subtotal: 60 * quantity, discount: (60 - price) * quantity, total: price * quantity, unitsConsumed: quantity, items: [{ ...request.items[0], visitorUnitPrice: 60, unitPrice: price, subtotal: 60 * quantity, discount: (60 - price) * quantity, total: price * quantity }] })
  }
}

test('Local conocido conserva Normal; V/L no cambian condición; actualización explícita de clasificación', async t => {
  let classified = false
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    if (url.endsWith('/classification')) {
      assert.deepEqual(JSON.parse(options!.body as string), { customerType: 'Visitor', expectedCustomerType: 'Local' })
      classified = true; return new Response(null, { status: 204 })
    }
    return fixture(url, options)!
  })
  await mount(); await click('Ana'); await add()
  assert.match(document.body.textContent!, /Clasificación: Local/)
  assert.equal(button('Normal').getAttribute('aria-pressed'), 'true')
  assert.match(document.querySelector('.checkout strong')!.textContent!, /60/)
  await click('Clasificar como Visitor'); assert.equal(classified, true)
  assert.match(document.body.textContent!, /Clasificación: Visitor/)
  assert.equal(button('Normal').getAttribute('aria-pressed'), 'true')
  await click('Descuento local'); assert.match(document.querySelector('.checkout strong')!.textContent!, /50/)
  await click('L'); await click('V'); assert.equal(button('Descuento local').getAttribute('aria-pressed'), 'true')
  assert.equal(restoreCheckout(localStorage).draft.saleCondition, 'LocalDiscount')
})

for (const [label, condition] of [['Cortesía', 'Courtesy'], ['Staff', 'Staff'], ['Cumpleaños', 'Birthday']] as const) {
  test(`${label}: oculta pago, envía condición y cero, sin inferir clasificación`, async t => {
    let posted = false
    t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
      const common = fixture(url, options); if (common) return common
      const request = JSON.parse(options!.body as string)
      assert.equal(request.saleCondition, condition); assert.equal(request.expectedTotal, 0); assert.equal(request.paymentMethod, null); assert.equal(request.customerType, undefined)
      posted = true
      return Response.json({ ...request, id, createdAtUtc: '2026-11-05T12:00:00Z', registeredBy: 'Test', customerId: buyerId, customerUsernameSnapshot: 'Ana', total: 0, items: [{ ...request.items[0], id, unitPrice: 0, subtotal: 0 }] }, { status: 201 })
    })
    await mount(); await click('Ana'); await add(); await click(label)
    assert.equal(button('Efectivo'), undefined); assert.equal(button('Tarjeta'), undefined)
    assert.match(document.body.textContent!, /Sin método de pago|sin método de pago/)
    assert.equal(restoreCheckout(localStorage).draft.saleCondition, condition)
    await confirm(); assert.equal(posted, true); assert.match(document.body.textContent!, /Venta registrada/)
    assert.equal(restoreCheckout(localStorage).receipt?.sale.total, 0)
    assert.equal(button('Normal').getAttribute('aria-pressed'), 'true')
    assert.doesNotMatch(document.body.textContent!, /Clasificación: Local/)
  })
}

test('timeout mantiene condición y request exactos tras remontar; bloquea cambios', async t => {
  const requests: unknown[] = []
  let offline = true
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    const common = fixture(url, options); if (common) return common
    const request = JSON.parse(options!.body as string); requests.push(request)
    if (offline) throw new DOMException('Timeout', 'TimeoutError')
    return Response.json({ ...request, id, createdAtUtc: '2026-11-05T12:00:00Z', registeredBy: 'Test', customerId: buyerId, customerUsernameSnapshot: 'visitante', total: 0, items: [{ ...request.items[0], id, unitPrice: 0, subtotal: 0 }] })
  })
  await mount(); await add(); await click('Cumpleaños'); await confirm()
  assert.equal(restoreCheckout(localStorage).attempt?.saleCondition, 'Birthday')
  assert.ok(button('Normal').matches(':disabled'))
  await click('Normal'); assert.equal(restoreCheckout(localStorage).attempt?.saleCondition, 'Birthday')
  await unmount(); offline = false; await mount()
  assert.ok(button('Staff').matches(':disabled'))
  await act(async () => document.querySelector<HTMLButtonElement>('.checkout button')!.click())
  assert.deepEqual(requests[0], requests[1]); assert.equal(restoreCheckout(localStorage).receipt?.sale.saleCondition, 'Birthday')
})

test('borrador puede cambiar condición; intento persistido no acepta una condición distinta', () => {
  const draft = { ...emptyCheckout().draft, cart: { [id]: 1 }, saleCondition: 'Courtesy' as const }
  const request = operation(makeOrder('Visitor', null, draft.cart, undefined, 'visitante', 'Courtesy'), 0)
  const pending = prepareAttempt(localStorage, { version: 1, draft }, request)
  assert.deepEqual(restoreCheckout(localStorage).attempt, request)
  assert.throws(() => updateDraft(localStorage, pending, { ...draft, saleCondition: 'Staff' }), /pendiente/)
  localStorage.setItem(checkoutKey, JSON.stringify({ ...pending, draft: { ...draft, saleCondition: 'Staff' } }))
  assert.throws(() => restoreCheckout(localStorage), /no coinciden/)
  localStorage.setItem(checkoutKey, JSON.stringify({ ...pending, attempt: { ...request, expectedTotal: 60, paymentMethod: 'Cash' } }))
  assert.throws(() => restoreCheckout(localStorage), /intento guardado es inválido/)
})

test('resumen muestra unidades cobradas/gratuitas y cada condición', async t => {
  t.mock.method(globalThis, 'fetch', async () => Response.json({ date: '2026-11-05', timeZoneId: 'America/Mexico_City', salesCount: 4, totalRevenue: 60, cashRevenue: 60, transferRevenue: 0, cardRevenue: 0, unitsSold: 4, unitsRegistered: 4, paidUnits: 1, freeUnits: 3, variants: [], conditions: ['Normal', 'Courtesy', 'Staff', 'Birthday'].map(saleCondition => ({ saleCondition, operations: 1, units: 1, revenue: saleCondition === 'Normal' ? 60 : 0 })) }))
  await mount(DailySalesPage)
  for (const text of ['Ingresos reales', 'Unidades registradas', 'Unidades cobradas', 'Unidades gratuitas', 'Cortesía', 'Staff', 'Cumpleaños']) assert.match(document.body.textContent!, new RegExp(text))
})
