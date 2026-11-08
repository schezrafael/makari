import { normalizeUsername } from '../customers/customers.ts'

export const saleConditions = ['Normal', 'LocalDiscount', 'Courtesy', 'Staff', 'Birthday'] as const
export type SaleCondition = typeof saleConditions[number]
export const conditionLabels: Record<SaleCondition, string> = { Normal: 'Normal', LocalDiscount: 'Descuento local', Courtesy: 'Cortesía', Staff: 'Staff', Birthday: 'Cumpleaños' }
export type CustomerType = 'Local' | 'Visitor'
export type PaymentMethod = 'Cash' | 'Transfer' | 'Card'
export type Variant = { id: string; name: string; sizeMl: number; price: number }
export type Product = { id: string; name: string; category: string; variants: Variant[] }
export type Cart = Record<string, number>
export type Order = { customerType?: CustomerType; paymentMethod: PaymentMethod | null; saleCondition?: SaleCondition; customerId?: string | null; customerUsername?: string; items: { productVariantId: string; quantity: number }[] }
export type SaleRequest = Order & { idempotencyKey: string; expectedTotal: number }
export type SaleResponse = Omit<SaleRequest, 'items' | 'expectedTotal'> & {
  id: string
  createdAtUtc: string
  total: number
  registeredBy: string
  customerUsernameSnapshot?: string | null
  items: { id: string; productVariantId: string; quantity: number; unitPrice: number; subtotal: number }[]
}

export function makeOrder(customerType: CustomerType | undefined, paymentMethod: PaymentMethod | null, cart: Cart, customerId?: string | null, customerUsername?: string, saleCondition?: SaleCondition): Order {
  return { ...(saleCondition ? { saleCondition } : { customerType }), paymentMethod, ...(customerId ? { customerId } : {}), ...(customerUsername !== undefined ? { customerUsername } : {}), items: Object.entries(cart).filter(([, quantity]) => quantity > 0)
    .sort(([a], [b]) => a.localeCompare(b)).map(([productVariantId, quantity]) => ({ productVariantId, quantity })) }
}

export function operation(order: Order, expectedTotal: number, previous?: SaleRequest): SaleRequest {
  if (previous) return previous // An unresolved operation is immutable, even if the draft changes.
  return { ...order, expectedTotal, idempotencyKey: crypto.randomUUID() }
}

export function totalCents(products: Product[], cart: Cart): number {
  return products.flatMap(product => product.variants).reduce((total, variant) => total + Math.round(variant.price * 100) * (cart[variant.id] ?? 0), 0)
}

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0
const guid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)

export function isCatalog(value: unknown): value is { products: Product[] } {
  return record(value) && Array.isArray(value.products) && value.products.every(p => record(p) && guid(p.id) && typeof p.name === 'string' && typeof p.category === 'string' && Array.isArray(p.variants) && p.variants.every(v => record(v) && guid(v.id) && typeof v.name === 'string' && typeof v.sizeMl === 'number' && money(v.price)))
}

export function isConfirmedSale(value: unknown, request: SaleRequest): value is SaleResponse {
  if (!record(value) || !guid(value.id) || typeof value.createdAtUtc !== 'string' || !Number.isFinite(Date.parse(value.createdAtUtc)) || typeof value.registeredBy !== 'string' || !money(value.total) || Math.round(value.total * 100) !== Math.round(request.expectedTotal * 100) || value.idempotencyKey !== request.idempotencyKey || (request.saleCondition === undefined && value.customerType !== request.customerType) || value.paymentMethod !== request.paymentMethod || !Array.isArray(value.items) || value.items.length !== request.items.length) return false
  if (request.saleCondition !== undefined && value.saleCondition !== request.saleCondition) return false
  if (request.customerUsername !== undefined) {
    if (!guid(value.customerId) || typeof value.customerUsernameSnapshot !== 'string') return false
    try {
      if (normalizeUsername(value.customerUsernameSnapshot).toLowerCase() !== normalizeUsername(request.customerUsername.trim() || 'visitante').toLowerCase()) return false
    } catch { return false }
  } else if (request.customerId) {
    if (value.customerId !== request.customerId) return false
  } else if (value.customerId != null && value.customerUsernameSnapshot !== 'visitante') return false
  if (request.customerId && (typeof value.customerUsernameSnapshot !== 'string' || !value.customerUsernameSnapshot)) return false
  const items = value.items
  return request.items.every(expected => items.filter(item => record(item) && guid(item.id) && item.productVariantId === expected.productVariantId && item.quantity === expected.quantity && money(item.unitPrice) && money(item.subtotal) && Math.round(item.subtotal * 100) === Math.round(item.unitPrice * 100) * expected.quantity).length === 1)
    && Math.round(value.total * 100) === items.reduce((sum, item) => sum + Math.round(item.subtotal * 100), 0)
}

export async function httpError(response: Response): Promise<string> {
  const fallback = response.status === 409 ? 'Esta clave ya corresponde a otro pedido. No se confirmó esta venta.' : response.status === 400 ? 'Revisa el pedido: la API rechazó sus datos.' : `La API devolvió un error (${response.status}). No se pudo confirmar la venta.`
  try {
    const body: unknown = await response.json()
    if (record(body)) {
      const details = record(body.errors) ? Object.values(body.errors).flat().filter(v => typeof v === 'string').join(' ') : typeof body.detail === 'string' ? body.detail : ''
      return `${fallback} ${details}`.trim()
    }
  } catch { /* A proxy may return HTML or an empty body. */ }
  return fallback
}
