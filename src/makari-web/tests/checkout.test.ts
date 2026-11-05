import assert from 'node:assert/strict'
import { test } from 'node:test'
import { checkoutKey, emptyCheckout, prepareAttempt, resolveAttempt, restoreCheckout, saveCheckout, updateDraft } from '../src/features/sales/checkout.ts'
import { makeOrder, operation } from '../src/features/sales/model.ts'
import type { CheckoutStorage } from '../src/features/sales/checkout.ts'
const id = '11111111-1111-4111-8111-111111111111'
const saleId = '22222222-2222-4222-8222-222222222222'
function storage(): CheckoutStorage {
  const values = new Map<string, string>()
  return { getItem: key => values.get(key) ?? null, setItem: (key, value) => { values.set(key, value) } }
}
const draft = { cart: { [id]: 2 }, localDiscount: true, payment: 'Transfer' as const }
const request = () => operation(makeOrder('Local', 'Transfer', draft.cart), 100)
const sale = (req: ReturnType<typeof request>) => ({ ...req, id: saleId, createdAtUtc: '2026-10-23T10:00:00Z', registeredBy: 'Andi', total: 100, items: [{ id: saleId, productVariantId: id, quantity: 2, unitPrice: 50, subtotal: 100 }] })

test('restaura productos, cantidades, descuento y pago sin usar precios guardados', () => {
  const db = storage()
  updateDraft(db, emptyCheckout(), draft)
  assert.deepEqual(restoreCheckout(db).draft, draft)
  db.setItem(checkoutKey, JSON.stringify({ version: 1, draft: { ...draft, price: 1, products: [{ id, price: 1 }] } }))
  assert.deepEqual(restoreCheckout(db).draft, draft)
})

test('no borra borradores corruptos, versiones desconocidas ni intentos inconsistentes', () => {
  const db = storage()
  for (const raw of ['invalid', JSON.stringify({ version: 2, draft }), JSON.stringify({ version: 1, draft: { ...draft, cart: { [id]: -1 } } }), JSON.stringify({ version: 1, draft, attempt: { ...request(), items: [{ productVariantId: id, quantity: 3 }] } })]) {
    db.setItem(checkoutKey, raw)
    assert.throws(() => restoreCheckout(db))
    assert.equal(db.getItem(checkoutKey), raw)
  }
})

test('guarda antes de enviar; fallo de red conserva solicitud y clave tras recarga y bloquea edición', async t => {
  const db = storage()
  const state = updateDraft(db, emptyCheckout(), draft)
  const pending = prepareAttempt(db, state, request())
  t.mock.method(globalThis, 'fetch', async (_url: string, options: RequestInit) => {
    assert.deepEqual(restoreCheckout(db).attempt, JSON.parse(options.body as string))
    throw new Error('Sin conexión')
  })
  await assert.rejects(resolveAttempt(db, pending), /Sin conexión/)
  const restored = restoreCheckout(db)
  assert.deepEqual(restored.attempt, pending.attempt)
  assert.throws(() => updateDraft(db, restored, { ...draft, localDiscount: false }), /pendiente/)
  assert.deepEqual(operation(makeOrder('Visitor', 'Cash', { [id]: 9 }), 900, restored.attempt), restored.attempt)
})

test('no envía si falla el guardado previo', () => {
  const db: CheckoutStorage = { getItem: () => null, setItem: () => { throw new Error('QuotaExceeded') } }
  assert.throws(() => prepareAttempt(db, { version: 1, draft }, request()), /QuotaExceeded/)
})

test('reintento tras recarga envía exactamente la misma solicitud y guarda comprobante al confirmar', async t => {
  const db = storage()
  const pending = prepareAttempt(db, { version: 1, draft }, request())
  t.mock.method(globalThis, 'fetch', async (_url: string, options: RequestInit) => {
    assert.deepEqual(JSON.parse(options.body as string), pending.attempt)
    return Response.json(sale(pending.attempt!), { status: 200 })
  })
  const result = await resolveAttempt(db, restoreCheckout(db))
  assert.deepEqual(result.state.draft, emptyCheckout().draft)
  assert.equal(result.state.attempt, undefined)
  assert.equal(restoreCheckout(db).receipt?.sale.id, saleId)
  assert.equal(restoreCheckout(db).receipt?.sale.total, 100)
})

test('price_changed resuelve intento, conserva borrador y exige una nueva confirmación/clave', async t => {
  const db = storage()
  const pending = prepareAttempt(db, { version: 1, draft }, request())
  t.mock.method(globalThis, 'fetch', async () => Response.json({ code: 'price_changed', idempotencyKey: pending.attempt!.idempotencyKey, currentTotal: 110 }, { status: 409 }))
  const result = await resolveAttempt(db, pending)
  assert.equal(result.priceChanged, 110)
  assert.deepEqual(restoreCheckout(db).draft, draft)
  assert.equal(result.state.attempt, undefined)
  const next = operation(makeOrder('Local', 'Transfer', draft.cart), 110)
  assert.notEqual(next.idempotencyKey, pending.attempt!.idempotencyKey)
})

test('conflictos de clave, 5xx, JSON inválido y totales inesperados mantienen el intento bloqueado', async t => {
  for (const kind of ['conflict', 'server', 'json', 'total', 'unrelated']) {
    const db = storage()
    const pending = prepareAttempt(db, { version: 1, draft }, request())
    const mock = t.mock.method(globalThis, 'fetch', async () => {
      if (kind === 'json') return new Response('Not JSON', { status: 502 })
      if (kind === 'total') return Response.json({ ...sale(pending.attempt!), total: 90 })
      if (kind === 'unrelated') return Response.json({ code: 'price_changed', idempotencyKey: saleId, currentTotal: 110 }, { status: 409 })
      return Response.json({ code: 'idempotency_conflict', idempotencyKey: pending.attempt!.idempotencyKey }, { status: kind === 'server' ? 500 : 409 })
    })
    await assert.rejects(resolveAttempt(db, pending))
    assert.deepEqual(restoreCheckout(db).attempt, pending.attempt)
    mock.mock.restore()
  }
})

test('rechazo de validación reconocido conserva borrador y permite corregirlo', async t => {
  const db = storage()
  const pending = prepareAttempt(db, { version: 1, draft }, request())
  t.mock.method(globalThis, 'fetch', async () => Response.json({ code: 'sale_validation_failed', idempotencyKey: pending.attempt!.idempotencyKey, errors: { items: ['Unavailable'] } }, { status: 400 }))
  const result = await resolveAttempt(db, pending)
  assert.equal(result.state.attempt, undefined)
  assert.deepEqual(result.state.draft, draft)
})

test('si falla guardar el éxito, conserva el intento recuperable', async t => {
  const db = storage()
  const pending = prepareAttempt(db, { version: 1, draft }, request())
  t.mock.method(globalThis, 'fetch', async () => Response.json(sale(pending.attempt!), { status: 201 }))
  const failing = { getItem: db.getItem, setItem: () => { throw new Error('Storage unavailable') } }
  await assert.rejects(resolveAttempt(failing, pending), /Storage unavailable/)
  assert.deepEqual(restoreCheckout(db).attempt, pending.attempt)
  saveCheckout(db, pending)
})

test('Card conserva borrador, POST pendiente y clave tras recarga y confirma', async t => {
  const db = storage()
  assert.equal(emptyCheckout().draft.payment, 'Cash')
  const cardDraft = { ...draft, payment: 'Card' as const }
  const state = updateDraft(db, emptyCheckout(), cardDraft)
  assert.deepEqual(restoreCheckout(db).draft, cardDraft)
  const pending = prepareAttempt(db, state, operation(makeOrder('Local', 'Card', cardDraft.cart), 100))
  const mock = t.mock.method(globalThis, 'fetch', async () => { throw new Error('offline') })
  await assert.rejects(resolveAttempt(db, pending), /offline/)
  mock.mock.mockImplementation(async (_url: string, options: RequestInit) => {
    assert.deepEqual(JSON.parse(options.body as string), pending.attempt)
    return Response.json(sale(pending.attempt!), { status: 200 })
  })
  await resolveAttempt(db, restoreCheckout(db))
  assert.equal(restoreCheckout(db).receipt?.sale.paymentMethod, 'Card')
  assert.equal(restoreCheckout(db).receipt?.sale.idempotencyKey, pending.attempt!.idempotencyKey)
})
