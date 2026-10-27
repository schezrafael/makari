import assert from 'node:assert/strict'
import { test } from 'node:test'
import { fetchDailySummary, isDailySummary } from '../src/features/sales/dailySummary.ts'
import type { DailySummary } from '../src/features/sales/dailySummary.ts'

const summary: DailySummary = { date: '2026-10-25', timeZoneId: 'America/Mexico_City', salesCount: 1, totalRevenue: 75, cashRevenue: 75, transferRevenue: 0, unitsSold: 1, variants: [{ productVariantId: 'v1', productName: 'Cold Brew', variantName: 'Cítrico', quantity: 1, revenue: 75 }] }

test('valida el resumen y sus renglones', () => {
  assert.equal(isDailySummary(summary), true)
  assert.equal(isDailySummary({ ...summary, variants: [{}] }), false)
  assert.equal(isDailySummary({ ...summary, totalRevenue: Number.NaN }), false)
})

test('consulta fecha específica y fecha de negocio por defecto con rutas relativas', async t => {
  const requests: string[] = []
  t.mock.method(globalThis, 'fetch', async (url: string) => { requests.push(url); return Response.json(summary) })
  assert.deepEqual(await fetchDailySummary('2026-10-25'), summary)
  assert.deepEqual(await fetchDailySummary(), summary)
  assert.deepEqual(requests, ['/api/sales/summary?date=2026-10-25', '/api/sales/summary'])
})

test('expone errores HTTP y formatos incompatibles', async t => {
  const mock = t.mock.method(globalThis, 'fetch', async () => new Response('', { status: 503 }))
  await assert.rejects(fetchDailySummary(), /503/)
  mock.mock.mockImplementation(async () => Response.json({ totalRevenue: 12 }))
  await assert.rejects(fetchDailySummary(), /formato esperado/)
})
