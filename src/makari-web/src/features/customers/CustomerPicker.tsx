import { useCallback, useEffect, useRef, useState } from 'react'
import { frequentCustomers, isCustomer, normalizeUsername } from './customers'
import type { Customer } from './customers'

export default function CustomerPicker({ input, generic, disabled, onChange }: {
  input: string; generic?: 'visitante' | 'local'; disabled: boolean
  onChange: (input: string, generic?: 'visitante' | 'local') => void
}) {
  const [frequent, setFrequent] = useState<Customer[]>([])
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<Customer | null>(null)
  const [classificationMessage, setClassificationMessage] = useState('Clasificación sin consultar.')
  const [busy, setBusy] = useState(false)
  const currentUsername = (input.trim().replace(/\s+/g, ' ') || generic || 'visitante').toLowerCase()
  const currentCustomer = selected?.username.toLowerCase() === currentUsername ? selected : frequent.find(customer => customer.username.toLowerCase() === currentUsername)
  const lookupId = useRef(0)
  const invalidateLookup = useCallback(() => { lookupId.current++ }, [])
  function change(value: string, kind?: 'visitante' | 'local', customer?: Customer) {
    lookupId.current++
    setBusy(false)
    setSelected(customer ?? null)
    setClassificationMessage(customer ? '' : 'Clasificación sin consultar.')
    onChange(value, kind)
  }
  async function lookup(username = input.trim() || generic || 'visitante') {
    const id = ++lookupId.current
    setBusy(true)
    try {
      const response = await fetch(`/api/customers?username=${encodeURIComponent(normalizeUsername(username))}`, { signal: AbortSignal.timeout(15000) })
      if (id !== lookupId.current) return
      if (response.status === 404) { setSelected(null); setClassificationMessage('Nuevo comprador · Visitor. Se creará al registrar la venta.'); return }
      if (!response.ok) throw new Error('No se pudo consultar la clasificación.')
      const data: unknown = await response.json()
      if (!isCustomer(data) || data.customerType === undefined) throw new Error('Clasificación no disponible.')
      if (id === lookupId.current) { setSelected(data); setClassificationMessage('') }
    } catch (cause) { if (id === lookupId.current) setClassificationMessage(cause instanceof Error ? cause.message : 'No se pudo consultar.') }
    finally { if (id === lookupId.current) setBusy(false) }
  }
  async function classify() {
    if (!currentCustomer?.customerType || busy) return
    const id = ++lookupId.current
    const next = currentCustomer.customerType === 'Visitor' ? 'Local' : 'Visitor'
    setBusy(true)
    try {
      const response = await fetch(`/api/customers/${currentCustomer.id}/classification`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ customerType: next, expectedCustomerType: currentCustomer.customerType }), signal: AbortSignal.timeout(15000) })
      if (!response.ok) throw new Error(response.status === 409 ? 'La clasificación cambió. Consulta al comprador nuevamente.' : 'No se pudo actualizar la clasificación.')
      if (id === lookupId.current) { setSelected({ ...currentCustomer, customerType: next }); setClassificationMessage('Clasificación guardada. La condición de venta no cambió.') }
    } catch (cause) { if (id === lookupId.current) setClassificationMessage(cause instanceof Error ? cause.message : 'No se pudo actualizar.') }
    finally { if (id === lookupId.current) setBusy(false) }
  }
  useEffect(() => {
    const controller = new AbortController()
    void frequentCustomers(AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]))
      .then(customers => { if (!controller.signal.aborted) setFrequent(customers) })
      .catch(() => { if (!controller.signal.aborted) setError('Frecuentes no disponibles. Puedes escribir el comprador.') })
    return () => { controller.abort(); invalidateLookup() }
  }, [invalidateLookup])
  return <fieldset className="buyer-picker" disabled={disabled}>
    <label htmlFor="buyer-username">Comprador</label>
    <input id="buyer-username" autoComplete="off" maxLength={256} value={input}
      aria-describedby="buyer-help" onBlur={() => void lookup()} onChange={event => change(event.target.value, event.target.value.trim() ? undefined : 'visitante')} />
    <p id="buyer-help" className="payment-note">Escribe un username o elige uno. Se guarda al confirmar la venta.</p>
    <div className="buyer-actions" aria-label="Compradores rápidos">
      <button type="button" aria-label="Visitante" aria-pressed={!input.trim() && generic !== 'local'} onClick={() => { change('', 'visitante'); void lookup('visitante') }}>V</button>
      <button type="button" aria-label="Local" aria-pressed={!input.trim() && generic === 'local'} onClick={() => { change('', 'local'); void lookup('local') }}>L</button>
      {frequent.map(customer => <button type="button" key={customer.id}
        aria-pressed={input.trim().replace(/\s+/g, ' ').toLowerCase() === customer.username.toLowerCase()}
        onClick={() => change(customer.username, undefined, customer)}>{customer.username}</button>)}
    </div>
    <div className="buyer-classification">
      <p role="status">{currentCustomer ? `Clasificación: ${currentCustomer.customerType ?? 'Sin consultar'}` : selected ? 'Clasificación sin consultar.' : classificationMessage}</p>
      {currentCustomer && classificationMessage && <p role="status">{classificationMessage}</p>}
      <div className="buyer-actions"><button type="button" disabled={busy} onClick={() => void lookup()}>{busy ? 'Consultando…' : 'Consultar comprador'}</button>
      {currentCustomer?.customerType && <button type="button" disabled={busy} onClick={() => void classify()}>Clasificar como {currentCustomer.customerType === 'Visitor' ? 'Local' : 'Visitor'}</button>}</div>
    </div>
    {error && <p className="payment-note" role="status">{error}</p>}
  </fieldset>
}
