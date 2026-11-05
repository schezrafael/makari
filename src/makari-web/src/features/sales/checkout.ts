import { isCustomer, normalizeUsername } from '../customers/customers.ts'
import type { Customer } from '../customers/customers.ts'
import { isConfirmedSale, makeOrder } from './model.ts'
import type { Cart, PaymentMethod, SaleRequest, SaleResponse } from './model.ts'

export const checkoutKey = 'makari.checkout.v1'
export type Draft = { cart: Cart; localDiscount: boolean; payment: PaymentMethod; buyer?: Customer; buyerInput?: string; buyerGeneric?: 'visitante' | 'local' }
export type Checkout = {
  version: 1
  draft: Draft
  attempt?: SaleRequest
  receipt?: { request: SaleRequest; sale: SaleResponse }
}
export type CheckoutStorage = Pick<Storage, 'getItem' | 'setItem'>
export const emptyCheckout = (): Checkout => ({ version: 1, draft: { cart: {}, localDiscount: false, payment: 'Cash' } })
export function buyerInput(draft: Draft): string {
  if (draft.buyerInput !== undefined) return draft.buyerInput
  if (!draft.buyer || ['a1200000-0000-4000-8000-000000000001', 'a1200000-0000-4000-8000-000000000002'].includes(draft.buyer.id)) return ''
  return draft.buyer.username
}
export function buyerGeneric(draft: Draft): 'visitante' | 'local' | undefined {
  if (buyerInput(draft).trim()) return undefined
  return draft.buyerGeneric ?? (draft.buyer?.id === 'a1200000-0000-4000-8000-000000000002' ? 'local' : 'visitante')
}
export function buyerUsername(draft: Draft): string {
  return buyerInput(draft).trim() ? normalizeUsername(buyerInput(draft)) : buyerGeneric(draft) ?? 'visitante'
}
const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const uuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const quantity = (value: unknown): value is number => Number.isInteger(value) && Number(value) > 0 && Number(value) <= 999
const amount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 && Number.isSafeInteger(Math.round(value * 100)) && Math.abs(value * 100 - Math.round(value * 100)) < 0.00001

function validRequest(value: unknown): value is SaleRequest {
  return object(value) && (value.customerUsername === undefined || typeof value.customerUsername === 'string' && value.customerUsername.length <= 256)
    && !(value.customerId != null && value.customerUsername !== undefined) && (value.customerId == null || uuid(value.customerId)) && uuid(value.idempotencyKey) && amount(value.expectedTotal) &&
    ['Local', 'Visitor'].includes(String(value.customerType)) && ['Cash', 'Transfer', 'Card'].includes(String(value.paymentMethod)) &&
    Array.isArray(value.items) && value.items.length > 0 && value.items.every(item => object(item) && uuid(item.productVariantId) && quantity(item.quantity)) &&
    new Set(value.items.map(item => item.productVariantId)).size === value.items.length &&
    Object.keys(value).every(key => ['customerType', 'paymentMethod', 'items', 'idempotencyKey', 'expectedTotal', 'customerId', 'customerUsername'].includes(key)) &&
    value.items.every(item => Object.keys(item).every(key => ['productVariantId', 'quantity'].includes(key)))
}

export function restoreCheckout(storage: CheckoutStorage): Checkout {
  const raw = storage.getItem(checkoutKey)
  if (raw === null) return emptyCheckout()
  let value: unknown
  try { value = JSON.parse(raw) }
  catch { throw new Error('El borrador guardado no se puede leer. No se ha borrado; revisa el almacenamiento antes de cobrar.') }
  if (!object(value) || value.version !== 1 || !object(value.draft) || !object(value.draft.cart) ||
    typeof value.draft.localDiscount !== 'boolean' || !['Cash', 'Transfer', 'Card'].includes(String(value.draft.payment)) ||
    !Object.entries(value.draft.cart).every(([id, count]) => uuid(id) && quantity(count)))
    throw new Error('El borrador guardado es inválido o usa otra versión. No se ha borrado; revisa el almacenamiento antes de cobrar.')
  const draft: Draft = { cart: { ...value.draft.cart } as Cart, localDiscount: value.draft.localDiscount, payment: value.draft.payment as PaymentMethod }
  if (value.draft.buyer !== undefined) {
    if (!isCustomer(value.draft.buyer)) throw new Error('El comprador guardado no es válido. No se ha borrado el borrador.')
    draft.buyer = value.draft.buyer
  }
  if (value.draft.buyerInput !== undefined) {
    if (typeof value.draft.buyerInput !== 'string' || value.draft.buyerInput.length > 256) throw new Error('El comprador guardado no es válido.')
    draft.buyerInput = value.draft.buyerInput
  }
  if (value.draft.buyerGeneric !== undefined) {
    if (!['visitante', 'local'].includes(String(value.draft.buyerGeneric))) throw new Error('El comprador genérico no es válido.')
    draft.buyerGeneric = value.draft.buyerGeneric as 'visitante' | 'local'
  }
  const result: Checkout = { version: 1, draft }
  if (value.attempt !== undefined) {
    if (!validRequest(value.attempt)) throw new Error('El intento guardado es inválido. No inicies otra venta hasta revisar su clave.')
    const order = makeOrder(draft.localDiscount ? 'Local' : 'Visitor', draft.payment, draft.cart, value.attempt.customerUsername === undefined ? draft.buyer?.id : undefined, value.attempt.customerUsername === undefined ? undefined : buyerUsername(draft))
    const attemptOrder = makeOrder(value.attempt.customerType, value.attempt.paymentMethod, Object.fromEntries(value.attempt.items.map(item => [item.productVariantId, item.quantity])), value.attempt.customerId, value.attempt.customerUsername)
    if (JSON.stringify(order) !== JSON.stringify(attemptOrder)) throw new Error('El borrador y el intento guardado no coinciden. Se requiere revisar la operación.')
    result.attempt = value.attempt
  }
  if (value.receipt !== undefined) {
    if (!object(value.receipt) || !validRequest(value.receipt.request) || !isConfirmedSale(value.receipt.sale, value.receipt.request) || value.attempt !== undefined)
      throw new Error('El comprobante guardado no es válido. Revisa la operación antes de cobrar.')
    result.receipt = { request: value.receipt.request, sale: value.receipt.sale }
  }
  return result // No stored catalog or draft prices are restored.
}

export function saveCheckout(storage: CheckoutStorage, state: Checkout): void {
  const json = JSON.stringify(state)
  storage.setItem(checkoutKey, json)
  if (storage.getItem(checkoutKey) !== json) throw new Error('No se pudo verificar el guardado local. No se enviará una venta nueva.')
}

export function updateDraft(storage: CheckoutStorage, state: Checkout, draft: Draft): Checkout {
  if (state.attempt) throw new Error('Resuelve el intento pendiente antes de editar.')
  const next: Checkout = { version: 1, draft: { ...draft, cart: Object.fromEntries(Object.entries(draft.cart).filter(([, count]) => count > 0)) } }
  saveCheckout(storage, next)
  return next
}

export function prepareAttempt(storage: CheckoutStorage, state: Checkout, request: SaleRequest): Checkout {
  const next = state.attempt ? state : { ...state, receipt: undefined, attempt: request }
  saveCheckout(storage, next) // Must finish synchronously before the first fetch.
  return next
}

export type Resolution = { state: Checkout; message: string; priceChanged?: number }
export async function resolveAttempt(storage: CheckoutStorage, state: Checkout): Promise<Resolution> {
  const request = state.attempt
  if (!request) throw new Error('No hay intento pendiente.')
  const response = await fetch('/api/sales', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request),
    signal: AbortSignal.timeout(30000),
  })
  let body: unknown
  try { body = await response.json() }
  catch { throw new Error(`No se recibió una confirmación válida (${response.status}). Reintenta la misma operación; no vuelvas a cobrar.`) }
  if ([200, 201].includes(response.status) && isConfirmedSale(body, request)) {
    const next: Checkout = { ...emptyCheckout(), receipt: { request, sale: body } }
    saveCheckout(storage, next) // Receipt and cleared draft/attempt are one atomic storage write.
    return { state: next, message: '' }
  }
  if (object(body) && body.idempotencyKey === request.idempotencyKey) {
    if (response.status === 409 && body.code === 'price_changed' && amount(body.currentTotal)) {
      const next: Checkout = { version: 1, draft: state.draft }
      saveCheckout(storage, next)
      return { state: next, message: 'El importe cambió. No se registró la venta. Revisa la nueva cotización y vuelve a confirmar.', priceChanged: body.currentTotal }
    }
    if (response.status === 400 && body.code === 'sale_validation_failed' && object(body.errors)) {
      const next: Checkout = { version: 1, draft: state.draft }
      saveCheckout(storage, next)
      return { state: next, message: `La venta fue rechazada sin registrarse. ${Object.values(body.errors).flat().filter(value => typeof value === 'string').join(' ')}` }
    }
    if (response.status === 409 && body.code === 'idempotency_conflict')
      throw new Error('Conflicto de idempotencia: se requiere revisar esta clave con el administrador. No inicies otra operación.')
    if (response.status === 409 && body.code === 'daily_cash_closed' && typeof body.businessDate === 'string') {
      const next: Checkout = { version: 1, draft: state.draft }
      saveCheckout(storage, next)
      return { state: next, message: `La fecha operativa ${body.businessDate} ya está cerrada. No se registró esta venta.` }
    }
  }
  throw new Error(`Resultado no confirmado (${response.status}). Reintenta la misma operación; no vuelvas a cobrar.`)
}
