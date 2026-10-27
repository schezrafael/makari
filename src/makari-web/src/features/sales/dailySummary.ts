export type DailySummary = {
  date: string
  timeZoneId: string
  salesCount: number
  totalRevenue: number
  cashRevenue: number
  transferRevenue: number
  unitsSold: number
  variants: { productVariantId: string; productName: string; variantName: string; quantity: number; revenue: number }[]
}

export function isDailySummary(value: unknown): value is DailySummary {
  if (!value || typeof value !== 'object') return false
  const item = value as Record<string, unknown>
  return typeof item.date === 'string' && typeof item.timeZoneId === 'string'
    && ['salesCount', 'totalRevenue', 'cashRevenue', 'transferRevenue', 'unitsSold'].every(key => typeof item[key] === 'number' && Number.isFinite(item[key]))
    && Array.isArray(item.variants) && item.variants.every(variant => {
      if (!variant || typeof variant !== 'object') return false
      const line = variant as Record<string, unknown>
      return typeof line.productVariantId === 'string' && typeof line.productName === 'string'
        && typeof line.variantName === 'string' && typeof line.quantity === 'number'
        && typeof line.revenue === 'number'
    })
}

export async function fetchDailySummary(date?: string, signal?: AbortSignal): Promise<DailySummary> {
  const query = date ? `?date=${encodeURIComponent(date)}` : ''
  const response = await fetch(`/api/sales/summary${query}`, { signal })
  if (!response.ok) throw new Error(`No se pudo cargar el resumen (${response.status}).`)
  const data: unknown = await response.json()
  if (!isDailySummary(data)) throw new Error('El resumen recibido no tiene el formato esperado.')
  return data
}
