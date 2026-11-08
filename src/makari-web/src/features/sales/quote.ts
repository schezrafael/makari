import type { Order } from './model.ts'

export type QuoteRequest = Pick<Order, 'customerType' | 'items' | 'saleCondition'>
export type Quote = QuoteRequest & {
  subtotal: number
  discount: number
  total: number
  items: (QuoteRequest['items'][number] & {
    visitorUnitPrice: number; unitPrice: number; subtotal: number; discount: number; total: number
  })[]
}
const cents = (value: number) => Math.round(value * 100)
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const amount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0

export function isQuote(value: unknown, request: QuoteRequest): value is Quote {
  if (!record(value) || (request.saleCondition === undefined && value.customerType !== request.customerType) || !amount(value.subtotal) || !amount(value.discount) || !amount(value.total) || !Array.isArray(value.items) || value.items.length !== request.items.length) return false
  if (request.saleCondition !== undefined && value.saleCondition !== request.saleCondition) return false
  const items = value.items
  return request.items.every(expected => items.filter(item => record(item) && item.productVariantId === expected.productVariantId && item.quantity === expected.quantity && amount(item.visitorUnitPrice) && amount(item.unitPrice) && amount(item.subtotal) && amount(item.discount) && amount(item.total) && cents(item.subtotal) === cents(item.visitorUnitPrice) * expected.quantity && cents(item.total) === cents(item.unitPrice) * expected.quantity && cents(item.subtotal) - cents(item.discount) === cents(item.total)).length === 1)
    && cents(value.subtotal) === items.reduce((sum, item) => sum + cents(item.subtotal), 0)
    && cents(value.discount) === items.reduce((sum, item) => sum + cents(item.discount), 0)
    && cents(value.total) === items.reduce((sum, item) => sum + cents(item.total), 0)
    && ((request.saleCondition !== undefined ? request.saleCondition !== 'Normal' : request.customerType !== 'Visitor') || value.discount === 0)
}

// Cancellation also guards callbacks: a completed fetch may still be parsing JSON.
export function startQuote(request: QuoteRequest, success: (quote: Quote) => void, failure: (message: string) => void): () => void {
  const controller = new AbortController()
  void (async () => {
    try {
      const response = await fetch('/api/sales/quote', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request),
        signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]),
      })
      if (!response.ok) throw new Error(`No se pudo calcular el descuento local (${response.status}). Revisa las bebidas o reintenta.`)
      const value: unknown = await response.json()
      if (!isQuote(value, request)) throw new Error('La cotización recibida no corresponde al pedido.')
      if (!controller.signal.aborted) success(value)
    } catch (cause) {
      if (!controller.signal.aborted) failure(cause instanceof Error ? cause.message : 'No se pudo conectar para cotizar.')
    }
  })()
  return () => controller.abort()
}
