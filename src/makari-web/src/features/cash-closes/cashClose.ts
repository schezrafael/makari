export const cashCloseStorageKey = 'makari.cash-close.v1'

export type CashCloseDraft = {
  businessDate: string
  openingCash: string
  countedCash: string
  responsible: string
  observations: string
}

export type CashCloseRequest = {
  idempotencyKey: string
  businessDate: string
  openingCash: number
  countedCash: number
  responsible: string
  observations: string | null
}

export type CashClose = CashCloseRequest & {
  id: string
  closedAtUtc: string
  cashSales: number
  transferSales: number
  cardSales: number
  totalRevenue: number
  salesCount: number
  expectedCash: number
  difference: number
}

export type CashCloseState = {
  version: 1
  draft: CashCloseDraft
  attempt?: CashCloseRequest
  receipt?: CashClose
}

export type CloseStorage = Pick<Storage, 'getItem' | 'setItem'>

export function emptyCashClose(businessDate = ''): CashCloseState {
  return { version: 1, draft: { businessDate, openingCash: '0.00', countedCash: '0.00', responsible: '', observations: '' } }
}

const isRecord = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const isUuid = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000'
const validDate = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value
const validAmount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 && Number.isSafeInteger(value * 100)

export function parseMoneyCents(value: string): number | null {
  if (!/^\d{1,14}(?:\.\d{1,2})?$/.test(value.trim())) return null
  const amount = Number(value)
  const cents = Math.round(amount * 100)
  return Number.isSafeInteger(cents) ? cents : null
}

export function calculateCashClose(openingCash: string, countedCash: string, cashSales: number) {
  const openingCents = parseMoneyCents(openingCash) ?? 0
  const countedCents = parseMoneyCents(countedCash) ?? 0
  const cashSalesCents = Math.round(cashSales * 100)
  const expectedCents = openingCents + cashSalesCents
  return { openingCents, countedCents, expectedCents, differenceCents: countedCents - expectedCents }
}

export function validateCashCloseDraft(draft: CashCloseDraft): string[] {
  const errors: string[] = []
  if (!validDate(draft.businessDate)) errors.push('Selecciona una fecha operativa válida.')
  if (parseMoneyCents(draft.openingCash) === null) errors.push('El fondo inicial debe ser cero o un importe positivo con máximo dos decimales.')
  if (parseMoneyCents(draft.countedCash) === null) errors.push('El efectivo contado debe ser cero o un importe positivo con máximo dos decimales.')
  if (!draft.responsible.trim() || draft.responsible.trim().length > 100) errors.push('Indica quién realiza el cierre (1 a 100 caracteres).')
  if (draft.observations.length > 2000) errors.push('Las observaciones no pueden superar 2000 caracteres.')
  return errors
}

function validRequest(value: unknown): value is Record<string, unknown> & CashCloseRequest {
  return isRecord(value) && isUuid(value.idempotencyKey) && validDate(value.businessDate)
    && validAmount(value.openingCash)
    && validAmount(value.countedCash)
    && typeof value.responsible === 'string' && value.responsible.trim().length > 0 && value.responsible.length <= 100
    && (value.observations === null || typeof value.observations === 'string' && value.observations.length <= 2000)
}

export function isCashClose(value: unknown): value is CashClose {
  if (!validRequest(value) || !isRecord(value) || !isUuid(value.id) || typeof value.closedAtUtc !== 'string' || !Number.isFinite(Date.parse(value.closedAtUtc))) return false
  return ['cashSales', 'transferSales', 'cardSales', 'totalRevenue', 'salesCount', 'expectedCash', 'difference']
    .every(key => typeof value[key] === 'number' && Number.isFinite(value[key]))
    && Number.isInteger(value.salesCount) && Number(value.salesCount) >= 0
}

function validDraft(value: unknown): value is CashCloseDraft {
  return isRecord(value) && (value.businessDate === '' || validDate(value.businessDate))
    // A draft may contain intermediate input (empty or a trailing decimal point).
    // Amount validation belongs to confirmation; pending requests remain strict.
    && typeof value.openingCash === 'string'
    && typeof value.countedCash === 'string'
    && typeof value.responsible === 'string' && value.responsible.length <= 100
    && typeof value.observations === 'string' && value.observations.length <= 2000
}

export function restoreCashClose(storage: CloseStorage): CashCloseState {
  const raw = storage.getItem(cashCloseStorageKey)
  if (raw === null) return emptyCashClose()
  let value: unknown
  try { value = JSON.parse(raw) }
  catch { throw new Error('El borrador de cierre guardado no se puede leer; no se ha borrado.') }
  if (!isRecord(value) || value.version !== 1 || !validDraft(value.draft))
    throw new Error('El borrador de cierre es inválido o usa otra versión; no se ha borrado.')
  const state: CashCloseState = { version: 1, draft: value.draft }
  if (value.attempt !== undefined) {
    const request = value.attempt
    if (!validRequest(request) || Object.keys(request).some(key => !['idempotencyKey', 'businessDate', 'openingCash', 'countedCash', 'responsible', 'observations'].includes(key)))
      throw new Error('El intento de cierre guardado es inválido; no generes otra clave.')
    if (validateCashCloseDraft(value.draft).length > 0 || request.businessDate !== value.draft.businessDate
      || request.openingCash !== (parseMoneyCents(value.draft.openingCash)! / 100)
      || request.countedCash !== (parseMoneyCents(value.draft.countedCash)! / 100)
      || request.responsible !== value.draft.responsible.trim()
      || request.observations !== (value.draft.observations.trim() || null))
      throw new Error('El borrador y el intento de cierre no coinciden; se requiere revisar la operación.')
    state.attempt = request
  }
  if (value.receipt !== undefined) {
    const receipt = isRecord(value.receipt) && !('cardSales' in value.receipt)
      ? { ...value.receipt, cardSales: 0 } : value.receipt
    if (!isCashClose(receipt) || value.attempt !== undefined) throw new Error('El comprobante de cierre guardado no es válido.')
    state.receipt = receipt
  }
  return state
}

export function saveCashClose(storage: CloseStorage, state: CashCloseState): void {
  const json = JSON.stringify(state)
  storage.setItem(cashCloseStorageKey, json)
  if (storage.getItem(cashCloseStorageKey) !== json) throw new Error('No se pudo verificar el guardado local; no se enviará el cierre.')
}

export function makeCashCloseRequest(draft: CashCloseDraft): CashCloseRequest | null {
  if (validateCashCloseDraft(draft).length) return null
  return {
    idempotencyKey: crypto.randomUUID(),
    businessDate: draft.businessDate,
    openingCash: parseMoneyCents(draft.openingCash)! / 100,
    countedCash: parseMoneyCents(draft.countedCash)! / 100,
    responsible: draft.responsible.trim(),
    observations: draft.observations.trim() || null,
  }
}

export function updateCashCloseDraft(storage: CloseStorage, state: CashCloseState, draft: CashCloseDraft): CashCloseState {
  if (state.attempt) throw new Error('Resuelve el intento de cierre pendiente antes de editar.')
  const next: CashCloseState = { version: 1, draft }
  saveCashClose(storage, next)
  return next
}

export function prepareCashCloseAttempt(storage: CloseStorage, state: CashCloseState): CashCloseState {
  if (state.attempt) return state
  const request = makeCashCloseRequest(state.draft)
  if (!request) throw new Error(validateCashCloseDraft(state.draft).join(' '))
  const next: CashCloseState = { version: 1, draft: state.draft, attempt: request }
  saveCashClose(storage, next)
  return next
}

export function completeCashClose(storage: CloseStorage, state: CashCloseState, receipt: CashClose): CashCloseState {
  if (!state.attempt || !isCashClose(receipt) || receipt.idempotencyKey !== state.attempt.idempotencyKey
    || receipt.businessDate !== state.attempt.businessDate || receipt.openingCash !== state.attempt.openingCash
    || receipt.countedCash !== state.attempt.countedCash || receipt.responsible !== state.attempt.responsible
    || receipt.observations !== state.attempt.observations
    || receipt.expectedCash !== receipt.openingCash + receipt.cashSales
    || Math.round(receipt.totalRevenue * 100) !== Math.round(receipt.cashSales * 100) + Math.round(receipt.transferSales * 100) + Math.round(receipt.cardSales * 100)
    || receipt.difference !== receipt.countedCash - receipt.expectedCash)
    throw new Error('La API no confirmó el cierre solicitado. Conserva el intento para reintentarlo.')
  const next: CashCloseState = { version: 1, draft: emptyCashClose(receipt.businessDate).draft, receipt }
  saveCashClose(storage, next)
  return next
}

export type CashCloseResolution = {
  state: CashCloseState
  receipt?: CashClose
  conflict?: 'cash_close_exists'
  message?: string
}

export async function resolveCashCloseAttempt(storage: CloseStorage, state: CashCloseState): Promise<CashCloseResolution> {
  const request = state.attempt
  if (!request) throw new Error('No hay un intento de cierre pendiente.')
  const response = await fetch('/api/cash-closes', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request), signal: AbortSignal.timeout(30000),
  })
  let body: unknown
  try { body = await response.json() }
  catch { throw new Error(`No se recibió un comprobante válido (${response.status}); reintenta la misma operación.`) }
  if ([200, 201].includes(response.status)) {
    if (!isCashClose(body)) throw new Error('La respuesta no confirma este cierre; conserva y reintenta la misma clave.')
    const completed = completeCashClose(storage, state, body)
    return { state: completed, receipt: body }
  }
  if (isRecord(body) && body.idempotencyKey === request.idempotencyKey
    && response.status === 409 && body.code === 'cash_close_exists') {
    const resolved: CashCloseState = { version: 1, draft: state.draft }
    saveCashClose(storage, resolved)
    return { state: resolved, conflict: 'cash_close_exists', message: 'Esta fecha ya tiene un cierre confirmado.' }
  }
  if (isRecord(body) && body.idempotencyKey === request.idempotencyKey
    && response.status === 400 && body.code === 'cash_close_validation_failed') {
    const resolved: CashCloseState = { version: 1, draft: state.draft }
    saveCashClose(storage, resolved)
    return { state: resolved, message: 'La API rechazó los datos; corrige el formulario antes de confirmar.' }
  }
  if (isRecord(body) && typeof body.detail === 'string')
    throw new Error(`No se confirmó el cierre (${response.status}): ${body.detail} Reintenta la misma operación.`)
  throw new Error(`No se confirmó el cierre (${response.status}). Reintenta la misma operación.`)
}
