import assert from 'node:assert/strict'
import { test } from 'node:test'
import { isQuote, startQuote } from '../src/features/sales/quote.ts'
import { makeOrder, operation } from '../src/features/sales/model.ts'
import type { Quote, QuoteRequest } from '../src/features/sales/quote.ts'

const id = '11111111-1111-4111-8111-111111111111'
const request: QuoteRequest = { customerType: 'Local', items: [{ productVariantId: id, quantity: 2 }] }
const quote: Quote = { ...request, subtotal: 120, discount: 20, total: 100, items: [{ ...request.items[0], visitorUnitPrice: 60, unitPrice: 50, subtotal: 120, discount: 20, total: 100 }] }
const tick = () => new Promise(resolve => setImmediate(resolve))

test('cotización valida subtotal, descuento y total del pedido vigente', () => {
  assert.equal(isQuote(quote, request), true)
  for (const invalid of [{ ...quote, total: 99 }, { ...quote, discount: -1 }, { ...quote, subtotal: 121 }, { ...quote, customerType: 'Visitor' }, { ...quote, items: [] }]) assert.equal(isQuote(invalid, request), false)
  assert.equal(isQuote(quote, { ...request, items: [{ productVariantId: id, quantity: 3 }] }), false)
})

test('activar/desactivar descuento cambia CustomerType y clave; reintento conserva clave', () => {
  const cart = { [id]: 2 }
  const visitor = operation(makeOrder('Visitor', 'Cash', cart), 120)
  const local = operation(makeOrder('Local', 'Cash', cart), 100)
  assert.equal(local.customerType, 'Local')
  assert.notEqual(visitor.idempotencyKey, local.idempotencyKey)
  assert.equal(operation(makeOrder('Local', 'Cash', cart), 100, local).idempotencyKey, local.idempotencyKey)
  const reverted = operation(makeOrder('Visitor', 'Cash', cart), 120)
  assert.notEqual(reverted.idempotencyKey, local.idempotencyKey)
  assert.equal(reverted.customerType, 'Visitor')
})

test('cotización envía solo tipo y cantidades; retorna resultado válido', async t => {
  t.mock.method(globalThis, 'fetch', async (url: string, options: RequestInit) => {
    assert.equal(url, '/api/sales/quote')
    assert.deepEqual(JSON.parse(options.body as string), request)
    return Response.json(quote)
  })
  const result = await new Promise<Quote>((resolve, reject) => startQuote(request, resolve, reject))
  assert.equal(result.total, 100)
})

test('ignora respuestas tardías al cambiar cantidades o desactivar descuento', async t => {
  let completeOld!: (response: Response) => void
  let calls = 0
  t.mock.method(globalThis, 'fetch', () => ++calls === 1 ? new Promise<Response>(resolve => { completeOld = resolve }) : Promise.resolve(Response.json({ ...quote, subtotal: 180, discount: 30, total: 150, items: [{ ...quote.items[0], quantity: 3, subtotal: 180, discount: 30, total: 150 }] })))
  const results: Quote[] = []
  const cancel = startQuote(request, result => results.push(result), assert.fail)
  cancel()
  startQuote({ ...request, items: [{ productVariantId: id, quantity: 3 }] }, result => results.push(result), assert.fail)
  await tick()
  completeOld(Response.json(quote))
  await tick()
  assert.equal(results.length, 1)
  assert.equal(results[0].total, 150)
})

test('cancelar durante lectura JSON también descarta el resultado', async t => {
  let finish!: (value: unknown) => void
  t.mock.method(globalThis, 'fetch', async () => ({ ok: true, json: () => new Promise(resolve => { finish = resolve }) }))
  const cancel = startQuote(request, () => assert.fail('stale success'), () => assert.fail('stale failure'))
  await tick()
  cancel()
  finish(quote)
  await tick()
})

test('errores HTTP, red y respuesta ambigua nunca producen cotización válida', async t => {
  for (const response of [() => Promise.resolve(new Response('', { status: 400 })), () => Promise.reject(new Error('Sin red')), () => Promise.resolve(Response.json({ total: 1 }))]) {
    const mock = t.mock.method(globalThis, 'fetch', response)
    const error = await new Promise<string>(resolve => startQuote(request, () => assert.fail('unexpected success'), resolve))
    assert.ok(error.length > 0)
    mock.mock.restore()
  }
})
