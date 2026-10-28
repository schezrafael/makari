import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchDailySummary } from '../sales/dailySummary'
import type { DailySummary } from '../sales/dailySummary'
import { calculateCashClose, completeCashClose, emptyCashClose, isCashClose, prepareCashCloseAttempt, resolveCashCloseAttempt, restoreCashClose, saveCashClose, updateCashCloseDraft, validateCashCloseDraft } from './cashClose'
import type { CashClose, CashCloseDraft, CashCloseState } from './cashClose'

const money = (value: number) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(value)
const centsMoney = (cents: number) => money(cents / 100)

export default function CashClosePage() {
  const [restored] = useState(() => {
    try { return { state: restoreCashClose(localStorage), ready: true, error: '' } }
    catch (cause) { return { state: emptyCashClose(), ready: false, error: cause instanceof Error ? cause.message : 'No se pudo recuperar el borrador del cierre.' } }
  })
  const [state, setState] = useState<CashCloseState>(restored.state)
  const stateRef = useRef(state)
  const commitState = useCallback((next: CashCloseState) => { stateRef.current = next; setState(next) }, [])
  const [ready] = useState(restored.ready)
  const [summary, setSummary] = useState<DailySummary | null>(null)
  const [businessToday, setBusinessToday] = useState('')
  const [existing, setExisting] = useState<CashClose | null>(null)
  const [loading, setLoading] = useState(restored.ready)
  const [error, setError] = useState(restored.error)
  const [success, setSuccess] = useState('')
  const [reload, setReload] = useState(0)
  const [sending, setSending] = useState(false)
  const inFlight = useRef(false)
  const dialog = useRef<HTMLDialogElement>(null)

  const businessDate = state.draft.businessDate
  useEffect(() => {
    if (!ready) return
    const controller = new AbortController()
    void (async () => {
      try {
        const today = await fetchDailySummary(undefined, controller.signal)
        setBusinessToday(today.date)
        const currentState = stateRef.current
        const daily = businessDate ? await fetchDailySummary(businessDate, controller.signal) : today
        if (controller.signal.aborted) return
        setSummary(daily)
        let nextState = stateRef.current
        if (!businessDate) {
          nextState = { ...currentState, draft: { ...currentState.draft, businessDate: daily.date } }
          saveCashClose(localStorage, nextState)
          commitState(nextState)
        }
        const response = await fetch(`/api/cash-closes?date=${encodeURIComponent(daily.date)}`, { signal: controller.signal })
        if (response.status === 404) {
          if (!controller.signal.aborted) setExisting(null)
          return
        }
        if (!response.ok) throw new Error(`No se pudo consultar el cierre (${response.status}).`)
        const body: unknown = await response.json()
        if (!isCashClose(body)) throw new Error('El cierre recibido no tiene el formato esperado.')
        if (controller.signal.aborted) return
        setExisting(body)
        if (nextState.attempt?.idempotencyKey === body.idempotencyKey) {
          const completed = completeCashClose(localStorage, nextState, body)
          commitState(completed)
          setSuccess('Cierre recuperado desde el servidor después de una interrupción.')
        } else if (nextState.attempt) {
          const resolved: CashCloseState = { version: 1, draft: nextState.draft }
          saveCashClose(localStorage, resolved)
          commitState(resolved)
          setError('Otra operación confirmó el cierre de esta fecha. Se cargó el comprobante existente.')
        }
      } catch (cause) {
        if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'No se pudo conectar con la API.')
      } finally {
        if (!controller.signal.aborted) setLoading(false)
      }
    })()
    return () => controller.abort()
  }, [ready, businessDate, reload, commitState])

  const attempt = state.attempt
  const closed = existing && existing.businessDate === state.draft.businessDate ? existing
    : state.receipt && state.receipt.businessDate === state.draft.businessDate ? state.receipt : null
  const errors = validateCashCloseDraft(state.draft)
  const { expectedCents, countedCents, differenceCents } = calculateCashClose(
    state.draft.openingCash, state.draft.countedCash, summary?.cashRevenue ?? 0)
  const locked = !ready || loading || !!error && !summary || !!attempt || !!closed || sending

  function editDraft(next: CashCloseDraft) {
    if (locked) return
    try {
      const updated = updateCashCloseDraft(localStorage, state, next)
      commitState(updated)
      setError('')
      setSuccess('')
      setLoading(true)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'No se pudo guardar el borrador del cierre.')
    }
  }

  function closeDialog() {
    if (inFlight.current) return
    dialog.current?.close()
  }

  async function submit() {
    if (inFlight.current || !ready || !summary) return
    inFlight.current = true
    setSending(true)
    setError('')
    let prepared = state
    try {
      prepared = attempt ? state : prepareCashCloseAttempt(localStorage, state)
      if (!attempt) commitState(prepared)
      const resolution = await resolveCashCloseAttempt(localStorage, prepared)
      commitState(resolution.state)
      if (resolution.receipt) {
        setExisting(resolution.receipt)
        setSuccess('Cierre confirmado y comprobante guardado.')
        return
      }
      if (resolution.conflict === 'cash_close_exists') {
        const lookup = await fetch(`/api/cash-closes?date=${encodeURIComponent(prepared.attempt!.businessDate)}`)
        if (lookup.ok) {
          const confirmed: unknown = await lookup.json()
          if (isCashClose(confirmed)) setExisting(confirmed)
        }
        setError('Esta fecha ya tiene un cierre confirmado. Consulta el comprobante mostrado antes de continuar.')
        return
      }
      if (resolution.message) setError(resolution.message)
    } catch (cause) {
      const message = cause instanceof Error ? cause.message : 'No se pudo confirmar el cierre.'
      setError(`${message}${prepared.attempt ? ' El intento y su clave se conservaron para reintentar.' : ''}`)
    } finally {
      inFlight.current = false
      setSending(false)
      closeDialog()
    }
  }

  return <main className="cash-close-page">
    <div className="cash-close-heading"><div><p className="eyebrow">MAKARI DRINKS · ZIPOLITE</p><h1>Cierre de caja</h1><p>Un cierre compartido por fecha operativa.</p></div><label className="summary-date">Fecha operativa<input type="date" value={state.draft.businessDate || summary?.date || ''} max={businessToday || summary?.date} disabled={!!attempt || loading} onChange={event => editDraft({ ...state.draft, businessDate: event.target.value })} /></label></div>
    {loading && <p className="notice" role="status">Cargando ventas y cierre de la fecha…</p>}
    {error && <div className="notice error" role="alert"><p>{error}</p>{!attempt && <button onClick={() => setReload(value => value + 1)}>Consultar de nuevo</button>}</div>}
    {success && <p className="notice success" role="status">{success}</p>}
    {attempt && <div className="notice recovery" role="status"><strong>Intento de cierre pendiente</strong><p>Se conservaron exactamente los importes y la clave. Reintenta para recuperar el mismo comprobante.</p><small>Clave: {attempt.idempotencyKey}</small></div>}
    {summary && <>
      <section className="cash-close-metrics" aria-label="Ventas de la fecha">
        <article><span>Ventas registradas</span><strong>{summary.salesCount}</strong></article>
        <article><span>Ventas en efectivo</span><strong>{money(summary.cashRevenue)}</strong></article>
        <article><span>Transferencias</span><strong>{money(summary.transferRevenue)}</strong></article>
        <article><span>Total vendido</span><strong>{money(summary.totalRevenue)}</strong></article>
      </section>
      {closed ? <CashCloseReceipt close={closed} /> : <section className="cash-close-form" aria-labelledby="cash-close-form-title">
        <h2 id="cash-close-form-title">Vista previa y conteo</h2>
        <label>Fondo inicial
          <span className="money-input"><span>$</span><input inputMode="decimal" value={state.draft.openingCash} disabled={locked} onChange={event => editDraft({ ...state.draft, openingCash: event.target.value })} aria-label="Fondo inicial en pesos" /></span>
        </label>
        <div className="close-calculation"><span>Efectivo esperado</span><strong>{centsMoney(expectedCents)}</strong><small>Fondo inicial + ventas en efectivo</small></div>
        <label>Efectivo contado
          <span className="money-input"><span>$</span><input inputMode="decimal" value={state.draft.countedCash} disabled={locked} onChange={event => editDraft({ ...state.draft, countedCash: event.target.value })} aria-label="Efectivo contado en pesos" /></span>
        </label>
        <div className={`close-calculation ${differenceCents === 0 ? '' : 'has-difference'}`}><span>Diferencia</span><strong>{differenceCents < 0 ? '−' : ''}{centsMoney(Math.abs(differenceCents))}</strong><small>Contado − esperado</small></div>
        <label>Responsable<input value={state.draft.responsible} disabled={locked} maxLength={100} onChange={event => editDraft({ ...state.draft, responsible: event.target.value })} placeholder="Nombre de quien cierra" /></label>
        <label>Observaciones <span className="optional">Opcional</span><textarea value={state.draft.observations} disabled={locked} maxLength={2000} rows={3} onChange={event => editDraft({ ...state.draft, observations: event.target.value })} placeholder="Anota una diferencia o incidencia" /></label>
        {errors.length > 0 && !attempt && <ul className="field-errors" aria-live="polite">{errors.map(item => <li key={item}>{item}</li>)}</ul>}
        <div className="cash-close-actions"><button className="primary" disabled={sending || loading || !ready || (attempt ? false : !!error || errors.length > 0)} onClick={() => { if (attempt) { void submit(); return } dialog.current?.showModal() }}>{sending ? 'Confirmando…' : attempt ? 'Reintentar cierre' : 'Confirmar cierre'}</button></div>
      </section>}
    </>}
    <dialog ref={dialog} aria-labelledby="cash-close-confirm-title" onCancel={event => { event.preventDefault(); closeDialog() }}>
      <h2 id="cash-close-confirm-title">Confirmar cierre de caja</h2><p>El cierre será definitivo para {state.draft.businessDate}.</p>
      <dl><div><dt>Efectivo esperado</dt><dd>{centsMoney(expectedCents)}</dd></div><div><dt>Efectivo contado</dt><dd>{centsMoney(countedCents)}</dd></div><div><dt>Diferencia</dt><dd>{differenceCents < 0 ? '−' : ''}{centsMoney(Math.abs(differenceCents))}</dd></div></dl>
      <p>Una vez confirmado no se podrá editar. Las ventas futuras de esta fecha se bloquearán.</p>
      <div className="dialog-actions"><button disabled={sending} onClick={closeDialog}>Revisar</button><button className="primary" disabled={sending} onClick={() => void submit()}>{sending ? 'Guardando…' : 'Confirmar definitivamente'}</button></div>
      <p role="status">{sending ? 'Esperando el comprobante del servidor…' : ''}</p>
    </dialog>
  </main>
}

function CashCloseReceipt({ close }: { close: CashClose }) {
  return <section className="cash-close-receipt" aria-labelledby="close-receipt-title">
    <div className="receipt-heading"><span aria-hidden="true">✓</span><div><p className="eyebrow">COMPROBANTE CONFIRMADO</p><h2 id="close-receipt-title">Caja cerrada</h2></div></div>
    <dl>
      <div><dt>Folio</dt><dd>{close.id}</dd></div><div><dt>Fecha operativa</dt><dd>{close.businessDate}</dd></div>
      <div><dt>Fondo inicial</dt><dd>{money(close.openingCash)}</dd></div><div><dt>Ventas en efectivo</dt><dd>{money(close.cashSales)}</dd></div>
      <div><dt>Transferencias</dt><dd>{money(close.transferSales)}</dd></div><div><dt>Total vendido</dt><dd>{money(close.totalRevenue)}</dd></div>
      <div><dt>Número de ventas</dt><dd>{close.salesCount}</dd></div><div><dt>Efectivo esperado</dt><dd>{money(close.expectedCash)}</dd></div>
      <div><dt>Efectivo contado</dt><dd>{money(close.countedCash)}</dd></div><div><dt>Diferencia</dt><dd>{close.difference < 0 ? '−' : ''}{money(Math.abs(close.difference))}</dd></div>
      <div><dt>Responsable</dt><dd>{close.responsible}</dd></div><div><dt>Observaciones</dt><dd>{close.observations || '—'}</dd></div>
      <div><dt>Confirmado</dt><dd>{new Intl.DateTimeFormat('es-MX', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(close.closedAtUtc))}</dd></div>
    </dl>
  </section>
}
