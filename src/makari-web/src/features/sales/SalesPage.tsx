import CustomerPicker from '../customers/CustomerPicker'
import { StatusMessage, PageHeader } from '../../components/ui'
import { buyerInput, buyerGeneric, buyerUsername, emptyCheckout, prepareAttempt, resolveAttempt, restoreCheckout, updateDraft } from './checkout'
import type { Draft } from './checkout'
import { startQuote } from './quote'
import type { Quote, QuoteRequest } from './quote'
import { useEffect, useRef, useState } from 'react'
import { isCatalog, makeOrder, operation, totalCents } from './model'
import type { CustomerType, Product } from './model'

const currency = (value: number) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(value)

function Quantity({ name, value, disabled, onChange }: { name: string; value: number; disabled: boolean; onChange: (value: number) => void }) {
  return <div className="quantity"><button aria-label={`Quitar ${name}`} disabled={disabled || value === 0} onClick={() => onChange(value - 1)}>−</button><output aria-label={`Cantidad de ${name}`}>{value}</output><button aria-label={`Agregar ${name}`} disabled={disabled || value >= 999} onClick={() => onChange(value + 1)}>+</button></div>
}

export default function SalesPage() {
  const [checkout, setCheckout] = useState(emptyCheckout)
  const { cart, localDiscount, payment } = checkout.draft
  const attempt = checkout.attempt
  const success = checkout.receipt?.sale
  const [ready, setReady] = useState(false)
  const [storageError, setStorageError] = useState(() => navigator.locks ? '' : 'Este navegador necesita HTTPS o localhost y Web Locks para recuperar ventas con seguridad.')
  const customer: CustomerType = localDiscount ? 'Local' : 'Visitor'
  const [quotation, setQuotation] = useState<{ key: string; quote?: Quote; error?: string } | null>(null)
  const [quoteRetry, setQuoteRetry] = useState(0)
  const [products, setProducts] = useState<Product[]>([])
  const [loading, setLoading] = useState(true)
  const [catalogError, setCatalogError] = useState('')
  const [reload, setReload] = useState(0)
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)
  const inFlight = useRef(false)
  const dialog = useRef<HTMLDialogElement>(null)
  const [confirming, setConfirming] = useState(false)
  const visitorSubtotal = totalCents(products, cart) / 100
  const quoteKey = JSON.stringify({ customerType: customer, items: makeOrder(customer, payment, cart).items })
  const currentQuote = quotation?.key === quoteKey ? quotation : null
  const quote = currentQuote?.quote
  const quoteBlocked = !quote
  const subtotal = quote?.subtotal ?? visitorSubtotal
  const discount = quote?.discount ?? 0
  const total = attempt?.expectedTotal ?? quote?.total ?? visitorSubtotal
  let buyerError = ''
  try { buyerUsername(checkout.draft) }
  catch (cause) { buyerError = cause instanceof Error ? cause.message : 'Revisa el username.' }
  const count = Object.values(cart).reduce((sum, quantity) => sum + quantity, 0)
  const locked = sending || confirming || !!attempt || !ready || !!storageError
  const lines = products.flatMap(product => product.variants.map(variant => ({ ...variant, product: product.name }))).filter(variant => cart[variant.id] > 0)
  const unavailable = Object.entries(cart).some(([id, quantity]) => quantity > 0 && !lines.some(line => line.id === id))

  useEffect(() => {
    const controller = new AbortController()
    let release: (() => void) | undefined
    if (!navigator.locks) return
    // One editor per origin, across tabs. The lock is released by browser on reload/crash.
    void navigator.locks.request('makari-checkout-v1', { signal: controller.signal }, async () => {
      if (controller.signal.aborted) return
      try {
        setCheckout(restoreCheckout(localStorage))
        setReady(true)
      } catch (cause) {
        setStorageError(cause instanceof Error ? cause.message : 'No se puede leer el almacenamiento local.')
      }
      await new Promise<void>(resolve => { release = resolve })
    }).catch(cause => {
      if (!controller.signal.aborted) setStorageError(String(cause))
    })
    return () => { controller.abort(); release?.() }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const response = await fetch('/api/catalog', { signal: controller.signal })
        if (!response.ok) throw new Error(`No se pudo cargar el catálogo (${response.status}).`)
        const data: unknown = await response.json()
        if (!isCatalog(data)) throw new Error('El catálogo recibido no tiene el formato esperado.')
        if (!controller.signal.aborted) setProducts(data.products)
      } catch (cause) {
        if (!controller.signal.aborted) setCatalogError(cause instanceof Error ? cause.message : 'No se pudo conectar con la API.')
      } finally { if (!controller.signal.aborted) setLoading(false) }
    }
    void load()
    return () => controller.abort()
  }, [reload])

  useEffect(() => {
    const request: QuoteRequest = JSON.parse(quoteKey)
    if (!ready || attempt || request.items.length === 0) return
    return startQuote(request,
      quote => setQuotation({ key: quoteKey, quote }),
      error => setQuotation({ key: quoteKey, error }))
  }, [quoteKey, quoteRetry, ready, attempt])

  function editDraft(next: Draft) {
    if (locked) return
    try {
      setCheckout(updateDraft(localStorage, checkout, next))
      setError('')
    } catch (cause) {
      setStorageError(cause instanceof Error ? cause.message : 'No se pudo guardar el borrador.')
    }
  }
  function selectBuyer(input: string, generic?: 'visitante' | 'local') {
    if (locked) return
    editDraft({
      ...checkout.draft, buyer: undefined, buyerInput: input, buyerGeneric: generic,
      localDiscount: generic === 'visitante' ? false : generic === 'local' ? true : checkout.draft.localDiscount,
    })
  }
  function quantity(id: string, value: number) {
    editDraft({ ...checkout.draft, cart: { ...cart, [id]: value } })
  }
  function toggleDiscount() {
    editDraft({ ...checkout.draft, localDiscount: !localDiscount })
  }
  function closeConfirmation() {
    if (inFlight.current) return
    dialog.current?.close()
    setConfirming(false)
  }
  async function submit() {
    if (inFlight.current || !ready || storageError) return
    if (!attempt && (quoteBlocked || buyerError || count === 0 || loading || catalogError || unavailable)) return
    inFlight.current = true
    setSending(true)
    setError('')
    try {
      const request = operation(makeOrder(customer, payment, cart, undefined, buyerUsername(checkout.draft)), total, attempt)
      const saved = prepareAttempt(localStorage, checkout, request)
      setCheckout(saved)
      const result = await resolveAttempt(localStorage, saved)
      setCheckout(result.state)
      setError(result.message + (result.priceChanged !== undefined ? ` Importe actualizado: ${currency(result.priceChanged)}.` : ''))
      setQuotation(null)
      setQuoteRetry(value => value + 1)
    } catch (cause) {
      const message = cause instanceof SyntaxError || cause instanceof TypeError
        ? 'No se recibió confirmación del servidor.'
        : cause instanceof Error ? cause.message : 'No se pudo confirmar la operación.'
      setError(`${message} Conserva esta página o recárgala para recuperar el intento. No vuelvas a cobrar.`)
      // Read back even when storage write succeeded but its verification failed.
      try { setCheckout(restoreCheckout(localStorage)) }
      catch { setStorageError('No se pudo recuperar el almacenamiento. No inicies otra venta hasta revisarlo.') }
    } finally {
      inFlight.current = false
      setSending(false)
      closeConfirmation()
    }
  }

  return <>
    <main className="layout">
      {(!ready || storageError) && <div className="notice error checkout-notice" role="alert">{storageError || 'Esperando acceso al checkout. Cierra otras pestañas de Makari para continuar.'}</div>}
      <section className="catalog" aria-labelledby="page-title">
        <PageHeader id="page-title" title="Nueva venta" description="Elige las bebidas y registra el cobro." />
        <p className="base-prices">Precios base de visitante · MXN</p>
        <div className="section-title"><h2>Nuestras bebidas</h2><span>Elige tu favorita</span></div>
        {loading ? <StatusMessage>Cargando bebidas y precios…</StatusMessage> : catalogError ? <div className="notice error" role="alert"><p>{catalogError}</p><button onClick={() => { setLoading(true); setCatalogError(''); setReload(value => value + 1) }}>Reintentar catálogo</button></div> : products.length === 0 ? <p className="notice">No hay bebidas disponibles.</p> : <div className="products">{products.map((product) => <article className="product" key={product.id}><div className="product-banner"><span>{product.category}</span></div><div className="product-body"><h3>{product.name}</h3>{product.variants.length === 0 && <p>Sin presentaciones disponibles.</p>}{product.variants.map(variant => <div className="variant" key={variant.id}><div><h4>{variant.name}</h4><span className="size">{variant.sizeMl} ml</span><strong>{currency(variant.price)}</strong></div><Quantity name={`${product.name}, ${variant.name}`} value={cart[variant.id] ?? 0} disabled={locked} onChange={value => quantity(variant.id, value)} /></div>)}</div></article>)}</div>}
      </section>
      <aside className="order" aria-labelledby="order-title"><div className="section-title"><h2 id="order-title">Tu pedido</h2><span className="badge">{count}</span></div>
        {success && <div className="notice success" role="status"><strong>Venta registrada</strong>{success.customerUsernameSnapshot && <p>Comprador: {success.customerUsernameSnapshot}</p>}<p>Total confirmado: {currency(success.total)}</p><small>Folio: {success.id}</small></div>}
        {attempt && <div className="notice recovery" role="status"><strong>Operación pendiente de resolver</strong><p>El pedido está bloqueado. Reintenta para recuperar o completar la misma venta, sin cobrar de nuevo.</p><p>Importe confirmado: {currency(attempt.expectedTotal)}</p><small>Clave: {attempt.idempotencyKey}</small><ul>{attempt.items.map(item => <li key={item.productVariantId}>{item.quantity} × {lines.find(line => line.id === item.productVariantId)?.product ?? item.productVariantId}</li>)}</ul></div>}
        {error && <div className="notice error" role="alert"><strong>No se confirmó la venta</strong><p>{error}</p></div>}
        {count === 0 ? <div className="empty"><span aria-hidden="true">✳</span><h3>Pedido vacío</h3><p>Agrega una bebida para comenzar.</p></div> : loading ? <p role="status">Actualizando precios del pedido…</p> : <ul className="cart-lines">{lines.map(line => <li key={line.id}><div><strong>{line.product}</strong><span>{line.name} · {line.sizeMl} ml</span><b>{currency(Math.round(line.price * 100) * cart[line.id] / 100)}</b></div><Quantity name={`${line.product}, ${line.name} del pedido`} value={cart[line.id]} disabled={locked} onChange={value => quantity(line.id, value)} /></li>)}</ul>}
        {unavailable && !loading && <div className="notice error" role="alert">Hay bebidas que ya no están disponibles. <button disabled={locked} onClick={() => { editDraft({ ...checkout.draft, cart: Object.fromEntries(lines.map(line => [line.id, cart[line.id]])) }) }}>quita las bebidas no disponibles</button>.</div>}
        <CustomerPicker input={buyerInput(checkout.draft)} generic={buyerGeneric(checkout.draft)} disabled={locked} onChange={selectBuyer} />
        {buyerError && <p className="notice error" role="alert">{buyerError}</p>}
        <div className="discount-control">
          <label><input type="checkbox" checked={localDiscount} disabled={locked} onChange={toggleDiscount} /> Descuento local</label>
          <p>Aplica los precios locales vigentes a este pedido.</p>
        </div>
        {!attempt && count > 0 && quoteBlocked && (currentQuote?.error
          ? <div className="notice error" role="alert"><p>{currentQuote.error}</p><button disabled={locked} onClick={() => { setQuotation(null); setQuoteRetry(value => value + 1) }}>Reintentar cotización</button></div>
          : <p className="notice" role="status">Confirmando precios del pedido…</p>)}
        <dl className="price-breakdown" aria-live="polite">
          <div><dt>Subtotal visitante</dt><dd>{currency(subtotal)}</dd></div>
          <div><dt>Descuento local</dt><dd>{!attempt && count > 0 && quoteBlocked ? '—' : `−${currency(discount)}`}</dd></div>
        </dl>
        <fieldset disabled={locked}><legend>Forma de pago</legend><div className="segmented">{(['Cash', 'Transfer', 'Card'] as const).map(method => <button key={method} aria-pressed={payment === method} onClick={() => { if (payment !== method) { editDraft({ ...checkout.draft, payment: method }) } }}>{method === 'Cash' ? 'Efectivo' : method === 'Transfer' ? 'Transferencia' : 'Tarjeta'}</button>)}</div></fieldset>
        <p className="payment-note">Verifica que recibiste el pago antes de registrar.</p>
        <div className="checkout"><div><span>Total a cobrar</span><strong>{!attempt && (loading || catalogError || unavailable || (count > 0 && quoteBlocked)) ? '—' : currency(total)}</strong></div><button className="primary" disabled={sending || confirming || !ready || !!storageError || (!attempt && (loading || !!catalogError || unavailable || count === 0 || quoteBlocked || !!buyerError))} onClick={() => { if (attempt) { void submit(); return } setConfirming(true); dialog.current?.showModal() }}>{sending ? 'Registrando…' : attempt ? 'Recuperar venta' : 'Registrar venta'} <span aria-hidden="true">↗</span></button></div><p className="total-note">Los precios se confirman al registrar.</p>
      </aside>
    </main>
    <footer>MAKARI DRINKS <span>Un sorbo de Zipolite.</span></footer>
    <dialog ref={dialog} aria-labelledby="confirm-title" onCancel={event => { event.preventDefault(); closeConfirmation() }}><h2 id="confirm-title">Confirmar venta</h2><p>Comprador: {buyerInput(checkout.draft).trim() || buyerGeneric(checkout.draft) || 'visitante'}</p><p>{customer === 'Local' ? 'Cliente local' : 'Visitante'} · {payment === 'Cash' ? 'Efectivo' : payment === 'Transfer' ? 'Transferencia' : 'Tarjeta'}</p><ul className="confirmation-lines">{lines.map(line => <li key={line.id}>{cart[line.id]} × {line.product} · {line.name}</li>)}</ul><div className="confirm-total">Total a confirmar <strong>{currency(total)}</strong></div><p>Confirma este importe y el pago recibido. Si el precio cambió, no se registrará la venta y pediremos una nueva confirmación.</p><div className="dialog-actions"><button disabled={sending} onClick={closeConfirmation}>Volver</button><button className="primary" disabled={sending || quoteBlocked} onClick={() => void submit()}>{sending ? 'Registrando…' : 'Confirmar y registrar'}</button></div><p role="status">{sending ? 'Espera la confirmación. No cierres esta página.' : ''}</p></dialog>
  </>
}
