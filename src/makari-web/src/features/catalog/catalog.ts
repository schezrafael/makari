export type Variant = { id: string; name: string; sizeMl: number; isActive: boolean; visitorPrice: number | null; localPrice: number | null }
export type Product = { id: string; name: string; category: string; isActive: boolean; version: string; variants: Variant[] }

export async function catalogRequest<T>(path = '', method = 'GET', body?: unknown): Promise<T> {
  const response = await fetch(`/api/products${path}`, { method, signal: AbortSignal.timeout(15000), headers: body ? { 'Content-Type': 'application/json' } : undefined, body: body ? JSON.stringify(body) : undefined })
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}))
    throw new Error(problem.detail ?? (problem.errors ? Object.values(problem.errors).flat().join(' ') : 'No se pudo guardar o consultar el catálogo. Intenta de nuevo.'))
  }
  return response.json() as Promise<T>
}
