import assert from 'node:assert/strict'
import { test } from 'node:test'
import { httpError, isCatalog, isConfirmedSale, makeOrder, operation, totalCents } from '../src/features/sales/model.ts'

const variantId = '11111111-1111-4111-8111-111111111111'
const id = '22222222-2222-4222-8222-222222222222'
const products = [{ id, name: 'Prueba', category: 'Soda', variants: [{ id: variantId, name: 'Limón', sizeMl: 350, price: 20.10 }] }]
const order = makeOrder('Local', 'Cash', { [variantId]: 3 })
const request = operation(order, 60.30)
const response = { ...request, id, createdAtUtc: '2026-10-21T12:00:00Z', registeredBy: 'Andi', total: 60.30, items: [{ id, productVariantId: variantId, quantity: 3, unitPrice: 20.10, subtotal: 60.30 }] }

test('catálogo valida el contrato y admite catálogo vacío', () => {
  assert.equal(isCatalog({ products }), true)
  assert.equal(isCatalog({ products: [] }), true)
  assert.equal(isCatalog({ products: [{ ...products[0], variants: [{ price: '20' }] }] }), false)
})
test('cantidades, carrito vacío y cálculo en centavos para ambos clientes', () => {
  assert.equal(totalCents(products, { [variantId]: 3 }), 6030)
  assert.equal(totalCents(products, { [variantId]: 2 }), 4020)
  assert.equal(totalCents(products, {}), 0)
  assert.deepEqual(makeOrder('Local', 'Cash', { [variantId]: 0 }).items, [])
  const visitor = [{ ...products[0], variants: [{ ...products[0].variants[0], price: 30.25 }] }]
  assert.equal(totalCents(visitor, { [variantId]: 3 }), 9075)
})
test('reintento conserva clave; cantidades, cliente y pago generan otra', () => {
  assert.equal(operation(order, 60.30, request).idempotencyKey, request.idempotencyKey)
  for (const next of [makeOrder('Visitor', 'Cash', { [variantId]: 3 }), makeOrder('Local', 'Transfer', { [variantId]: 3 }), makeOrder('Local', 'Cash', { [variantId]: 2 })]) {
    assert.notEqual(operation(next, 60.30).idempotencyKey, request.idempotencyKey)
  }
  assert.notEqual(operation(order, 60.30).idempotencyKey, request.idempotencyKey)
})
test('la clave no depende del orden de las líneas', () => {
  const first = makeOrder('Local', 'Cash', { [id]: 1, [variantId]: 2 })
  const prior = operation(first, 100)
  assert.equal(operation(makeOrder('Local', 'Cash', { [variantId]: 2, [id]: 1 }), 100, prior).idempotencyKey, prior.idempotencyKey)
})
test('acepta solo el total confirmado por el operador', () => {
  assert.equal(isConfirmedSale(response, request), true)
  assert.equal(isConfirmedSale({ ...response, total: 66, items: [{ ...response.items[0], unitPrice: 22, subtotal: 66 }] }, request), false)
})
test('rechaza respuestas ambiguas o ajenas al pedido', () => {
  for (const data of [null, {}, { ...response, idempotencyKey: id }, { ...response, customerType: 'Visitor' }, { ...response, paymentMethod: 'Transfer' }, { ...response, total: 50 }, { ...response, items: [] }, { ...response, createdAtUtc: '' }, { ...response, items: [{ ...response.items[0], quantity: 4 }] }]) assert.equal(isConfirmedSale(data, request), false)
})
test('errores de validación, conflicto y respuesta no JSON son legibles', async () => {
  assert.match(await httpError(new Response(JSON.stringify({ errors: { items: ['Producto inactivo'] } }), { status: 400 })), /Producto inactivo/)
  assert.match(await httpError(new Response('{}', { status: 409 })), /otro pedido/)
  assert.match(await httpError(new Response('<html>Error</html>', { status: 502 })), /502/)
})
