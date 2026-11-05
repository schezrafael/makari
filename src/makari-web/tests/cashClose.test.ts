import assert from 'node:assert/strict'
import { test } from 'node:test'
import { calculateCashClose, cashCloseStorageKey, completeCashClose, emptyCashClose, parseMoneyCents, prepareCashCloseAttempt, resolveCashCloseAttempt, restoreCashClose, validateCashCloseDraft } from '../src/features/cash-closes/cashClose.ts'
import type { CashClose, CashCloseState, CloseStorage } from '../src/features/cash-closes/cashClose.ts'

const id = '11111111-1111-4111-8111-111111111111'
const storage = (): CloseStorage & { value: string | null } => ({
  value: null,
  getItem() { return this.value },
  setItem(_key, value) { this.value = value },
})

test('valida fondo cero e importes con centavos; calcula la diferencia en centavos', () => {
  const draft = { businessDate: '2026-10-26', openingCash: '0.00', countedCash: '82.50', responsible: 'Andi', observations: '' }
  assert.deepEqual(validateCashCloseDraft(draft), [])
  assert.equal(parseMoneyCents(draft.openingCash), 0)
  assert.deepEqual(calculateCashClose(draft.openingCash, draft.countedCash, 25), {
    openingCents: 0, countedCents: 8250, expectedCents: 2500, differenceCents: 5750,
  })
  assert.equal(calculateCashClose('15', '55', 60).differenceCents, -2000)
  assert.equal(parseMoneyCents('-1'), null)
  assert.equal(parseMoneyCents('1.005'), null)
  assert.ok(validateCashCloseDraft({ ...draft, businessDate: '2026-02-30' }).length > 0)
  assert.ok(validateCashCloseDraft({ ...draft, responsible: '' }).length > 0)
})

test('guarda request exacto antes de enviar y reintenta con la misma clave después de fallo de red', async t => {
  const local = storage()
  let state = emptyCashClose('2026-10-26')
  state = { ...state, draft: { ...state.draft, openingCash: '20', countedCash: '82.50', responsible: 'Andi', observations: 'Conteo final' } }
  const prepared = prepareCashCloseAttempt(local, state)
  const key = prepared.attempt!.idempotencyKey
  assert.equal(JSON.parse(local.value!).attempt.idempotencyKey, key)
  const fail = t.mock.method(globalThis, 'fetch', async () => { throw new TypeError('offline') })
  await assert.rejects(resolveCashCloseAttempt(local, prepared), /offline/)
  fail.mock.restore()

  const recovered = restoreCashClose(local)
  assert.equal(recovered.attempt?.idempotencyKey, key)
  let sent: unknown
  const close: CashClose = { id, businessDate: '2026-10-26', closedAtUtc: '2026-10-27T01:00:00Z', idempotencyKey: key, openingCash: 20, cashSales: 50, transferSales: 30, cardSales: 0, totalRevenue: 80, salesCount: 2, expectedCash: 70, countedCash: 82.5, difference: 12.5, responsible: 'Andi', observations: 'Conteo final' }
  t.mock.method(globalThis, 'fetch', async (_url: string, options: RequestInit) => {
    sent = JSON.parse(options.body as string)
    return Response.json(close, { status: 201 })
  })
  const result = await resolveCashCloseAttempt(local, recovered)
  assert.equal((sent as { idempotencyKey: string }).idempotencyKey, key)
  assert.equal(result.receipt?.id, id)
  assert.equal(result.state.attempt, undefined)
  assert.equal(restoreCashClose(local).receipt?.difference, 12.5)
})

test('no persiste comprobante ante respuesta ambigua y resuelve conflicto de fecha explícito', async t => {
  const local = storage()
  const base = emptyCashClose('2026-10-26')
  const state: CashCloseState = { ...base, draft: { ...base.draft, responsible: 'Rafael' } }
  const prepared = prepareCashCloseAttempt(local, state)
  const ambiguous = t.mock.method(globalThis, 'fetch', async () => Response.json({ unexpected: true }, { status: 200 }))
  await assert.rejects(resolveCashCloseAttempt(local, prepared), /no confirma/)
  assert.equal(restoreCashClose(local).attempt?.idempotencyKey, prepared.attempt?.idempotencyKey)
  ambiguous.mock.restore()

  const conflictMock = t.mock.method(globalThis, 'fetch', async () => Response.json({ code: 'cash_close_exists', idempotencyKey: prepared.attempt!.idempotencyKey }, { status: 409 }))
  const result = await resolveCashCloseAttempt(local, prepared)
  assert.equal(result.conflict, 'cash_close_exists')
  assert.equal(restoreCashClose(local).attempt, undefined)
  conflictMock.mock.restore()
  assert.ok(cashCloseStorageKey.includes('cash-close'))
})

test('valida el comprobante del backend y no borra el intento ante total inconsistente', () => {
  const local = storage()
  const base = emptyCashClose('2026-10-26')
  const state = prepareCashCloseAttempt(local, { ...base, draft: { ...base.draft, responsible: 'Andi', countedCash: '70' } })
  const invalid = { id, businessDate: '2026-10-26', closedAtUtc: '2026-10-26T12:00:00Z', idempotencyKey: state.attempt!.idempotencyKey, openingCash: 0, cashSales: 50, transferSales: 0, cardSales: 0, totalRevenue: 50, salesCount: 1, expectedCash: 50, countedCash: 70, difference: 0, responsible: 'Andi', observations: null } as CashClose
  assert.throws(() => completeCashClose(local, state, invalid), /no confirmó/)
  assert.equal(restoreCashClose(local).attempt?.idempotencyKey, state.attempt?.idempotencyKey)
})

test('restaura comprobante histórico sin cardSales conservando importes', () => {
  const key = crypto.randomUUID()
  const state = emptyCashClose('2020-01-01')
  const receipt = { id, businessDate: '2020-01-01', closedAtUtc: '2020-01-02T01:00:00Z', idempotencyKey: key, openingCash: 20, cashSales: 50, transferSales: 30, totalRevenue: 80, salesCount: 2, expectedCash: 70, countedCash: 82.5, difference: 12.5, responsible: 'Andi', observations: null }
  const db = storage()
  db.setItem(cashCloseStorageKey, JSON.stringify({ ...state, receipt }))
  assert.deepEqual(restoreCashClose(db).receipt, { ...receipt, cardSales: 0 })
})

test('confirma ingresos de tres métodos con centavos sin error de coma flotante', () => {
  const local = storage()
  const base = emptyCashClose('2026-10-26')
  const state = prepareCashCloseAttempt(local, { ...base, draft: { ...base.draft, responsible: 'Andi', countedCash: '0.10' } })
  const receipt: CashClose = { ...state.attempt!, id, closedAtUtc: '2026-10-27T01:00:00Z', cashSales: 0.1, transferSales: 0.2, cardSales: 0.3, totalRevenue: 0.6, salesCount: 3, expectedCash: 0.1, difference: 0 }
  assert.equal(completeCashClose(local, state, receipt).receipt?.totalRevenue, 0.6)
})
