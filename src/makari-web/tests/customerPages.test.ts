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
let root: ReturnType<typeof createRoot>
async function mount(page = SalesPage) {
  root = createRoot(document.getElementById('root')!)
  await act(async () => { root.render(createElement(page)) })
}
const button = (label: string) => [...document.querySelectorAll('button')].find(item => item.textContent?.trim() === label)!
const id = '11111111-1111-4111-8111-111111111111'
afterEach(async () => { if (root) await act(async () => root.unmount()); localStorage.clear() })

const visitor = { id: 'a1200000-0000-4000-8000-000000000001', username: 'visitante' }
const local = { id: 'a1200000-0000-4000-8000-000000000002', username: 'local' }
const named = { id: 'a1200000-0000-4000-8000-000000000003', username: 'Ana Luna' }
async function editUsername(value: string) {
  const input = document.querySelector<HTMLInputElement>('#buyer-username')!
  await act(async () => {
    Object.getOwnPropertyDescriptor(dom.window.HTMLInputElement.prototype, 'value')!.set!.call(input, value)
    input.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
  })
}
function commonResponse(url: string, options?: RequestInit): Response | undefined {
  if (url === '/api/customers/frequent?limit=5') return Response.json([named])
  if (url === '/api/catalog') return Response.json({ products: [{ id, name: 'Cold Brew', category: 'Bebidas', variants: [{ id, name: 'Natural', sizeMl: 300, price: 60 }] }] })
  if (url === '/api/sales/quote') {
    const request = JSON.parse(options!.body as string)
    assert.equal(request.customerId, undefined)
    const price = request.customerType === 'Local' ? 50 : 60
    return Response.json({ ...request, subtotal: 60, discount: 60 - price, total: price, items: [{ ...request.items[0], visitorUnitPrice: 60, unitPrice: price, subtotal: 60, discount: 60 - price, total: price }] })
  }
}

test('input vacío y V por defecto; L/V, frecuentes y escritura sin consultas por tecla', async t => {
  const fetchMock = t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    const response = commonResponse(url, options)
    assert.ok(response, `Unexpected request: ${url}`)
    return response
  })
  await mount()
  const input = () => document.querySelector<HTMLInputElement>('#buyer-username')!
  assert.equal(input().value, '')
  assert.equal(button('V').getAttribute('aria-pressed'), 'true')
  assert.equal(button('Buscar'), undefined)
  await act(async () => button('L').click())
  assert.equal(input().value, '')
  assert.equal(restoreCheckout(localStorage).draft.localDiscount, true)
  const calls = fetchMock.mock.callCount()
  await editUsername('Nuevo')
  await editUsername('Nuevo username')
  assert.equal(fetchMock.mock.callCount(), calls)
  assert.equal(button('V').getAttribute('aria-pressed'), 'false')
  assert.equal(button('L').getAttribute('aria-pressed'), 'false')
  assert.equal(restoreCheckout(localStorage).draft.localDiscount, true)
  await act(async () => button('Ana Luna').click())
  assert.equal(input().value, 'Ana Luna')
  await act(async () => root.unmount())
  await mount()
  assert.equal(input().value, 'Ana Luna')
  await act(async () => document.querySelector<HTMLInputElement>('.discount-control input')!.click())
  assert.equal(input().value, 'Ana Luna')
  assert.equal(restoreCheckout(localStorage).draft.localDiscount, false)
  await act(async () => button('L').click())
  await editUsername('Temporal')
  await editUsername('')
  assert.equal(button('V').getAttribute('aria-pressed'), 'true')
  assert.equal(restoreCheckout(localStorage).draft.localDiscount, false)
  await act(async () => button('V').click())
  assert.equal(input().value, '')
})

test('persiste username antes del POST, recupera la misma solicitud y restablece Visitante', async t => {
  let offline = true
  const posts: Record<string, unknown>[] = []
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    const common = commonResponse(url, options)
    if (common) return common
    assert.equal(url, '/api/sales')
    const request = JSON.parse(options!.body as string)
    posts.push(request)
    assert.deepEqual(restoreCheckout(localStorage).attempt, request)
    if (offline) throw new TypeError('offline')
    const buyer = request.customerUsername === 'visitante' ? visitor : request.customerUsername === 'local' ? local : named
    return Response.json({ ...request, id, createdAtUtc: '2026-11-05T12:00:00Z', registeredBy: 'Andi', customerId: buyer.id, customerUsernameSnapshot: buyer.username, total: 60, items: [{ ...request.items[0], id, unitPrice: 60, subtotal: 60 }] })
  })
  await mount()
  await editUsername('Comprador inicial')
  await editUsername('  Ana   Luna  ')
  await act(async () => document.querySelector<HTMLButtonElement>('[aria-label="Agregar Cold Brew, Natural"]')!.click())
  await act(async () => document.querySelector<HTMLButtonElement>('.checkout button')!.click())
  await act(async () => button('Confirmar y registrar').click())
  assert.equal(restoreCheckout(localStorage).attempt?.customerUsername, 'Ana Luna')
  assert.equal(restoreCheckout(localStorage).attempt?.customerId, undefined)
  assert.ok(document.querySelector('#buyer-username')!.matches(':disabled'))
  assert.ok(button('L').matches(':disabled'))
  await act(async () => root.unmount())
  offline = false
  await mount()
  assert.equal(document.querySelector<HTMLInputElement>('#buyer-username')!.value, '  Ana   Luna  ')
  await act(async () => document.querySelector<HTMLButtonElement>('.checkout button')!.click())
  assert.deepEqual(posts[0], posts[1])
  assert.equal(restoreCheckout(localStorage).receipt?.sale.customerId, named.id)
  assert.equal(document.querySelector<HTMLInputElement>('#buyer-username')!.value, '')
  assert.equal(button('V').getAttribute('aria-pressed'), 'true')
  assert.equal(restoreCheckout(localStorage).draft.localDiscount, false)
  await act(async () => document.querySelector<HTMLButtonElement>('[aria-label="Agregar Cold Brew, Natural"]')!.click())
  await act(async () => document.querySelector<HTMLButtonElement>('.checkout button')!.click())
  await act(async () => button('Confirmar y registrar').click())
  assert.equal(posts[2].customerUsername, 'visitante')
  assert.notEqual(posts[2].idempotencyKey, posts[1].idempotencyKey)
})

test('frecuentes no disponibles no impiden escribir ni vender', async t => {
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    if (url.includes('/frequent')) throw new Error('offline')
    return commonResponse(url, options)!
  })
  await mount()
  await editUsername('Ana')
  assert.equal(document.querySelector<HTMLInputElement>('#buyer-username')!.value, 'Ana')
  assert.match(document.body.textContent!, /Frecuentes no disponibles/)
})
