export type Customer = { id: string; username: string; customerType?: 'Visitor' | 'Local' }
export function isCustomer(value: unknown): value is Customer {
  if (typeof value !== 'object' || value === null) return false
  const item = value as Record<string, unknown>
  return typeof item.id === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(item.id)
    && item.id !== '00000000-0000-0000-0000-000000000000'
    && (item.customerType === undefined || item.customerType === 'Visitor' || item.customerType === 'Local')
    && typeof item.username === 'string' && /^[A-Za-z0-9][A-Za-z0-9 ._-]{0,39}$/.test(item.username)
}
export function normalizeUsername(value: string): string {
  const clean = value.trim().replace(/\s+/g, ' ')
  if (value.length > 256 || !/^[A-Za-z0-9][A-Za-z0-9 ._-]{0,39}$/.test(clean))
    throw new Error('Usa de 1 a 40 caracteres: letras sin acentos, números, espacios, punto, guion o guion bajo. Comienza con letra o número.')
  return ['visitante', 'local'].includes(clean.toLowerCase()) ? clean.toLowerCase() : clean
}
export async function frequentCustomers(signal: AbortSignal): Promise<Customer[]> {
  const response = await fetch('/api/customers/frequent?limit=5', { signal })
  if (!response.ok) throw new Error('No se pudieron cargar los compradores frecuentes.')
  const data: unknown = await response.json()
  if (!Array.isArray(data) || data.length > 5 || !data.every(isCustomer)) throw new Error('La respuesta de compradores frecuentes no es válida.')
  return data
}
