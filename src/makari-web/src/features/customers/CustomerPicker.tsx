import { useEffect, useState } from 'react'
import { frequentCustomers } from './customers'
import type { Customer } from './customers'

export default function CustomerPicker({ input, generic, disabled, onChange }: {
  input: string; generic?: 'visitante' | 'local'; disabled: boolean
  onChange: (input: string, generic?: 'visitante' | 'local') => void
}) {
  const [frequent, setFrequent] = useState<Customer[]>([])
  const [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    void frequentCustomers(AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]))
      .then(customers => { if (!controller.signal.aborted) setFrequent(customers) })
      .catch(() => { if (!controller.signal.aborted) setError('Frecuentes no disponibles. Puedes escribir el comprador.') })
    return () => controller.abort()
  }, [])
  return <fieldset className="buyer-picker" disabled={disabled}>
    <label htmlFor="buyer-username">Comprador</label>
    <input id="buyer-username" autoComplete="off" maxLength={256} value={input}
      aria-describedby="buyer-help" onChange={event => onChange(event.target.value, event.target.value.trim() ? undefined : 'visitante')} />
    <p id="buyer-help" className="payment-note">Escribe un username o elige uno. Se guarda al confirmar la venta.</p>
    <div className="buyer-actions" aria-label="Compradores rápidos">
      <button type="button" aria-label="Visitante" aria-pressed={!input.trim() && generic !== 'local'} onClick={() => onChange('', 'visitante')}>V</button>
      <button type="button" aria-label="Local" aria-pressed={!input.trim() && generic === 'local'} onClick={() => onChange('', 'local')}>L</button>
      {frequent.map(customer => <button type="button" key={customer.id}
        aria-pressed={input.trim().replace(/\s+/g, ' ').toLowerCase() === customer.username.toLowerCase()}
        onClick={() => onChange(customer.username)}>{customer.username}</button>)}
    </div>
    {error && <p className="payment-note" role="status">{error}</p>}
  </fieldset>
}
