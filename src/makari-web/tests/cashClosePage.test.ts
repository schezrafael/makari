import assert from 'node:assert/strict'
import { afterEach, test } from 'node:test'
import { JSDOM } from 'jsdom'
import { act, createElement } from 'react'
import { cashCloseStorageKey, emptyCashClose, prepareCashCloseAttempt, restoreCashClose } from '../src/features/cash-closes/cashClose.ts'

const dom = new JSDOM('<!doctype html><div id="root"></div>', { url: 'http://localhost' })
Object.assign(globalThis, { window: dom.window, document: dom.window.document, localStorage: dom.window.localStorage, IS_REACT_ACT_ENVIRONMENT: true })
// jsdom does not implement the native dialog methods.
dom.window.HTMLDialogElement.prototype.close = function () { this.removeAttribute('open') }
dom.window.HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', '') }
const { createRoot } = await import('react-dom/client')
const { default: CashClosePage } = await import('../src/features/cash-closes/CashClosePage.tsx')
let root: ReturnType<typeof createRoot>
const date = '2026-10-28'
const summary = (day = date) => ({ date: day, timeZoneId: 'America/Mexico_City', salesCount: 2, totalRevenue: 120, cashRevenue: 50, transferRevenue: 30, cardRevenue: 40, unitsSold: 2, variants: [] })
const missing = () => Response.json({ code: 'cash_close_not_found' }, { status: 404 })
const input = (name: string) => document.querySelector<HTMLInputElement>(`input[aria-label="${name}"]`)!
const dateInput = () => document.querySelector<HTMLInputElement>('input[type=date]')!
const content = () => document.body.textContent!
async function mount() {
  root = createRoot(document.getElementById('root')!)
  await act(async () => { root.render(createElement(CashClosePage)) })
}
async function edit(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
  element.focus()
  await act(async () => {
    const prototype = element instanceof dom.window.HTMLTextAreaElement ? dom.window.HTMLTextAreaElement.prototype : dom.window.HTMLInputElement.prototype
    Object.getOwnPropertyDescriptor(prototype, 'value')!.set!.call(element, value)
    element.dispatchEvent(new dom.window.Event('input', { bubbles: true }))
    element.dispatchEvent(new dom.window.Event('change', { bubbles: true }))
  })
}
const receipt = (request: ReturnType<typeof prepareCashCloseAttempt>['attempt']) => ({ ...request!, id: '11111111-1111-4111-8111-111111111111', closedAtUtc: '2026-10-28T12:00:00Z', cashSales: 50, transferSales: 30, cardSales: 40, totalRevenue: 120, salesCount: 2, expectedCash: request!.openingCash + 50, difference: request!.countedCash - request!.openingCash - 50 })
afterEach(async () => {
  if (root) await act(async () => root.unmount())
  localStorage.clear()
})

test('404 reconocido permite editar todos los campos sin GET, loading, pérdida de foco o reinicio; recalcula diferencia', async t => {
  const fetchMock = t.mock.method(globalThis, 'fetch', async (url: string) => url.includes('/summary') ? Response.json(summary()) : missing())
  await mount()
  assert.equal(fetchMock.mock.callCount(), 2)
  const opening = input('Fondo inicial en pesos')
  assert.equal(opening.disabled, false)
  for (const value of ['3', '30', '300']) {
    await edit(opening, value)
    assert.equal(input('Fondo inicial en pesos'), opening)
    assert.equal(document.activeElement, opening)
    assert.equal(opening.value, value)
    assert.equal(opening.disabled, false)
    assert.ok(!content().includes('Cargando ventas'))
    assert.equal(fetchMock.mock.callCount(), 2)
  }
  const counted = input('Efectivo contado en pesos')
  for (const value of ['4', '40', '400']) {
    await edit(counted, value)
    assert.equal(document.activeElement, counted)
    assert.equal(counted.value, value)
    assert.equal(fetchMock.mock.callCount(), 2)
  }
  assert.match(document.querySelector('.has-difference')!.textContent!, /\$50.00/)
  await edit(document.querySelector<HTMLInputElement>('input[maxlength="100"]')!, 'Andi')
  await edit(document.querySelector('textarea')!, 'Conteo')
  assert.equal(fetchMock.mock.callCount(), 2)
  assert.equal(restoreCashClose(localStorage).draft.openingCash, '300')
  assert.equal(document.querySelector<HTMLButtonElement>('.cash-close-actions button')!.disabled, false)
})

test('otro 404 es un error, no permite crear cierre y consultar de nuevo recupera', async t => {
  let fail = true
  t.mock.method(globalThis, 'fetch', async (url: string) => url.includes('/summary') ? Response.json(summary()) : fail ? Response.json({ code: 'route_not_found' }, { status: 404 }) : missing())
  await mount()
  assert.match(content(), /No se pudo consultar el cierre \(404\)/)
  assert.equal(input('Fondo inicial en pesos'), null)
  fail = false
  await act(async () => document.querySelector<HTMLButtonElement>('[role=alert] button')!.click())
  assert.equal(input('Fondo inicial en pesos').disabled, false)
})

test('cambiar fecha carga datos; respuesta antigua tardía no sustituye datos ni borrador', async t => {
  let release!: (value: Response) => void
  let oldSignal: AbortSignal | undefined
  const fetchMock = t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    const day = new URL(url, 'http://localhost').searchParams.get('date') || date
    if (url.includes('/summary')) return Response.json(summary(day))
    if (day === '2026-10-27') {
      oldSignal = options?.signal as AbortSignal
      return new Promise<Response>(resolve => { release = resolve })
    }
    return missing()
  })
  await mount()
  await edit(dateInput(), '2026-10-27')
  assert.ok(content().includes('Cargando ventas'))
  await edit(dateInput(), '2026-10-26')
  assert.equal(oldSignal?.aborted, true)
  await edit(input('Fondo inicial en pesos'), '300')
  const count = fetchMock.mock.callCount()
  // Deliberately return a valid old receipt even though fetch ignored cancellation.
  const oldReceipt = { ...receipt({ idempotencyKey: '22222222-2222-4222-8222-222222222222', businessDate: '2026-10-27', openingCash: 0, countedCash: 0, responsible: 'Andi', observations: null }) }
  await act(async () => release(Response.json(oldReceipt)))
  assert.equal(dateInput().value, '2026-10-26')
  assert.equal(input('Fondo inicial en pesos').value, '300')
  assert.equal(input('Fondo inicial en pesos').disabled, false)
  assert.equal(restoreCashClose(localStorage).draft.businessDate, '2026-10-26')
  assert.equal(fetchMock.mock.callCount(), count)
})

test('POST incierto conserva clave tras remontar y reintenta mostrando comprobante persistido', async t => {
  const base = emptyCashClose(date)
  const pending = prepareCashCloseAttempt(localStorage, { ...base, draft: { ...base.draft, responsible: 'Andi', openingCash: '300', countedCash: '400' } })
  let fail = true
  const requests: unknown[] = []
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') {
      requests.push(JSON.parse(options.body as string))
      if (fail) throw new TypeError('offline')
      return Response.json(receipt(pending.attempt), { status: 200 })
    }
    return url.includes('/summary') ? Response.json(summary()) : missing()
  })
  await mount()
  await act(async () => document.querySelector<HTMLButtonElement>('.cash-close-actions button')!.click())
  assert.equal(restoreCashClose(localStorage).attempt?.idempotencyKey, pending.attempt!.idempotencyKey)
  assert.equal(input('Fondo inicial en pesos').disabled, true)
  await act(async () => root.unmount())
  fail = false
  await mount()
  await act(async () => document.querySelector<HTMLButtonElement>('.cash-close-actions button')!.click())
  assert.deepEqual(requests, [pending.attempt, pending.attempt])
  assert.ok(content().includes('Caja cerrada'))
  assert.ok(content().includes('Tarjeta'))
  assert.equal(restoreCashClose(localStorage).receipt?.idempotencyKey, pending.attempt!.idempotencyKey)
  assert.equal(restoreCashClose(localStorage).attempt, undefined)
})

test('GET recupera intento confirmado y comprobante solo lectura permite cambiar fecha', async t => {
  const base = emptyCashClose(date)
  const pending = prepareCashCloseAttempt(localStorage, { ...base, draft: { ...base.draft, responsible: 'Andi' } })
  t.mock.method(globalThis, 'fetch', async (url: string) => {
    const day = new URL(url, 'http://localhost').searchParams.get('date') || date
    return url.includes('/summary') ? Response.json(summary(day)) : day === date ? Response.json(receipt(pending.attempt)) : missing()
  })
  await mount()
  assert.ok(content().includes('Caja cerrada'))
  assert.ok(content().includes('Tarjeta'))
  assert.equal(input('Fondo inicial en pesos'), null)
  assert.equal(JSON.parse(localStorage.getItem(cashCloseStorageKey)!).attempt, undefined)
  await edit(dateInput(), '2026-10-27')
  assert.equal(input('Fondo inicial en pesos').disabled, false)
})

test('restaura texto monetario en edición y mantiene validación estricta para intentos', async t => {
  const base = emptyCashClose(date)
  localStorage.setItem(cashCloseStorageKey, JSON.stringify({ ...base, draft: { ...base.draft, openingCash: '', countedCash: '300.' } }))
  t.mock.method(globalThis, 'fetch', async (url: string) => url.includes('/summary') ? Response.json(summary()) : missing())
  await mount()
  assert.equal(input('Fondo inicial en pesos').value, '')
  assert.equal(input('Efectivo contado en pesos').value, '300.')
  assert.equal(input('Fondo inicial en pesos').disabled, false)
  assert.equal(document.querySelector<HTMLButtonElement>('.cash-close-actions button')!.disabled, true)
  const pending = prepareCashCloseAttempt(localStorage, { ...base, draft: { ...base.draft, responsible: 'Andi' } })
  localStorage.setItem(cashCloseStorageKey, JSON.stringify({ ...pending, draft: { ...pending.draft, openingCash: '' } }))
  assert.throws(() => restoreCashClose(localStorage), /no coinciden/)
})

test('confirmar un nuevo cierre persiste request antes del POST y muestra el comprobante', async t => {
  t.mock.method(globalThis, 'fetch', async (url: string, options?: RequestInit) => {
    if (options?.method === 'POST') {
      const request = JSON.parse(options.body as string)
      assert.deepEqual(restoreCashClose(localStorage).attempt, request)
      return Response.json(receipt(request), { status: 201 })
    }
    return url.includes('/summary') ? Response.json(summary()) : missing()
  })
  await mount()
  await edit(input('Fondo inicial en pesos'), '300')
  await edit(input('Efectivo contado en pesos'), '400')
  await edit(document.querySelector<HTMLInputElement>('input[maxlength="100"]')!, 'Andi')
  await act(async () => document.querySelector<HTMLButtonElement>('.cash-close-actions button')!.click())
  assert.ok(document.querySelector('dialog')!.hasAttribute('open'))
  await act(async () => document.querySelector<HTMLButtonElement>('dialog .primary')!.click())
  assert.ok(content().includes('Caja cerrada'))
  assert.ok(content().includes('Tarjeta'))
  assert.equal(restoreCashClose(localStorage).receipt?.openingCash, 300)
  assert.equal(input('Fondo inicial en pesos'), null)
})
