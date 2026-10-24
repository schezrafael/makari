import { useEffect, useRef, useState } from 'react'
import { httpError, isCatalog, isConfirmedSale, makeOrder, operation, totalCents } from './model'
import type { Cart, CustomerType, PaymentMethod, Product, SaleRequest, SaleResponse } from './model'

const currency = (value: number) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(value)

function Quantity({ name, value, disabled, onChange }: { name: string; value: number; disabled: boolean; onChange: (value: number) => void }) {
  return <div className="quantity"><button aria-label={`Quitar ${name}`} disabled={disabled || value === 0} onClick={() => onChange(value - 1)}>−</button><output aria-label={`Cantidad de ${name}`}>{value}</output><button aria-label={`Agregar ${name}`} disabled={disabled || value >= 999} onClick={() => onChange(value + 1)}>+</button></div>
}

export default function SalesPage() {
  const [customer, setCustomer] = useState<CustomerType>('Local')
  const [payment, setPayment] = useState<PaymentMethod>('Cash')
  const [products, setProducts] = useState<Product[]>([])
  const [cart, setCart] = useState<Cart>({})
  const [loading, setLoading] = useState(true)
  const [catalogError, setCatalogError] = useState('')
  const [reload, setReload] = useState(0)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState<SaleResponse | null>(null)
  const [sending, setSending] = useState(false)
  const pending = useRef<SaleRequest | undefined>(undefined)
  const inFlight = useRef(false)
  const dialog = useRef<HTMLDialogElement>(null)
  const [confirming, setConfirming] = useState(false)
  const total = totalCents(products, cart) / 100
  const count = Object.values(cart).reduce((sum, quantity) => sum + quantity, 0)
  const locked = sending || confirming
  const lines = products.flatMap(product => product.variants.map(variant => ({ ...variant, product: product.name }))).filter(variant => cart[variant.id] > 0)
  const unavailable = Object.entries(cart).some(([id, quantity]) => quantity > 0 && !lines.some(line => line.id === id))

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const response = await fetch(`/api/catalog?customerType=${customer}`, { signal: controller.signal })
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
  }, [customer, reload])

  function changed() {
    pending.current = undefined
    setError('')
    setSuccess(null)
  }
  function quantity(id: string, value: number) {
    changed()
    setCart(previous => ({ ...previous, [id]: value }))
  }
  function selectCustomer(value: CustomerType) {
    if (value === customer) return
    changed()
    setLoading(true)
    setCatalogError('')
    setProducts([])
    setCustomer(value)
  }
  function closeConfirmation() {
    if (inFlight.current) return
    dialog.current?.close()
    setConfirming(false)
  }
  async function submit() {
    if (inFlight.current) return
    inFlight.current = true
    setSending(true)
    setError('')
    try {
      if (!crypto.randomUUID) throw new Error('Para registrar ventas abre la aplicación mediante HTTPS o localhost.')
      const request = operation(makeOrder(customer, payment, cart), pending.current)
      pending.current = request
      const response = await fetch('/api/sales', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(request), signal: AbortSignal.timeout(30000) })
      if (!response.ok) throw new Error(await httpError(response))
      const data: unknown = await response.json()
      if (![200, 201].includes(response.status) || !isConfirmedSale(data, request)) throw new Error('La respuesta recibida es ambigua. La venta no está confirmada en esta pantalla.')
      setSuccess(data)
      setCart({})
      pending.current = undefined
    } catch (cause) {
      setError(`${cause instanceof Error ? cause.message : 'Error de conexión.'} Conservamos el pedido. Reintenta sin modificarlo para consultar o completar la misma venta; no vuelvas a cobrar.`)
    } finally {
      inFlight.current = false
      setSending(false)
      closeConfirmation()
    }
  }

  return <>
    <header className="header"><a href="/" className="brand" aria-label="Makari Drinks inicio">makari<span>DRINKS · ZIPOLITE</span></a><span className="header-label">Punto de venta <span className="dot" /></span></header>
    <main className="layout">
      <section className="catalog" aria-labelledby="page-title">
        <div className="intro"><p className="eyebrow">HECHO CON CALMA. SERVIDO CON AMOR.</p><h1 id="page-title">Algo fresco,<br /><em>algo Makari.</em></h1><p>Elige las bebidas y registra el cobro.</p></div>
        <fieldset className="customer" disabled={locked}><legend>¿Quién nos visita?</legend><div className="segmented">{(['Local', 'Visitor'] as const).map(type => <button key={type} aria-pressed={customer === type} onClick={() => selectCustomer(type)}>{type === 'Local' ? 'Local' : 'Visitante'}</button>)}</div><p>Precios para {customer === 'Local' ? 'clientes locales' : 'visitantes'} · MXN</p></fieldset>
        <div className="section-title"><h2>Nuestras bebidas</h2><span>Elige tu favorita</span></div>
        {loading ? <p className="notice" role="status">Cargando bebidas y precios…</p> : catalogError ? <div className="notice error" role="alert"><p>{catalogError}</p><button onClick={() => { setLoading(true); setCatalogError(''); setReload(value => value + 1) }}>Reintentar catálogo</button></div> : products.length === 0 ? <p className="notice">No hay bebidas disponibles para este tipo de cliente.</p> : <div className="products">{products.map((product, index) => <article className="product" key={product.id}><div className={`product-banner tone-${index % 3}`}><span>{product.category}</span><span aria-hidden="true" className="drink-mark">◒</span></div><div className="product-body"><h3>{product.name}</h3>{product.variants.length === 0 && <p>Sin presentaciones disponibles.</p>}{product.variants.map(variant => <div className="variant" key={variant.id}><div><h4>{variant.name}</h4><span className="size">{variant.sizeMl} ml</span><strong>{currency(variant.price)}</strong></div><Quantity name={`${product.name}, ${variant.name}`} value={cart[variant.id] ?? 0} disabled={locked} onChange={value => quantity(variant.id, value)} /></div>)}</div></article>)}</div>}
      </section>
      <aside className="order" aria-labelledby="order-title"><div className="section-title"><h2 id="order-title">Tu pedido</h2><span className="badge">{count}</span></div>
        {success && <div className="notice success" role="status"><strong>Venta registrada</strong><p>Total confirmado: {currency(success.total)}</p><small>Folio: {success.id}</small></div>}
        {error && <div className="notice error" role="alert"><strong>No se confirmó la venta</strong><p>{error}</p><small>Modificar el pedido inicia otra operación. Si hubo un fallo de red, resuelve primero el reintento.</small></div>}
        {count === 0 ? <div className="empty"><span aria-hidden="true">✳</span><h3>Una pausa refrescante</h3><p>Agrega una bebida para comenzar.</p></div> : loading ? <p role="status">Actualizando precios del pedido…</p> : <ul className="cart-lines">{lines.map(line => <li key={line.id}><div><strong>{line.product}</strong><span>{line.name} · {line.sizeMl} ml</span><b>{currency(Math.round(line.price * 100) * cart[line.id] / 100)}</b></div><Quantity name={`${line.product}, ${line.name} del pedido`} value={cart[line.id]} disabled={locked} onChange={value => quantity(line.id, value)} /></li>)}</ul>}
        {unavailable && !loading && <div className="notice error" role="alert">Hay bebidas sin precio disponible para este cliente. Vuelve al tipo de cliente anterior o <button disabled={locked} onClick={() => { changed(); setCart(Object.fromEntries(lines.map(line => [line.id, cart[line.id]]))) }}>quita las bebidas no disponibles</button>.</div>}
        <fieldset disabled={locked}><legend>Forma de pago</legend><div className="segmented">{(['Cash', 'Transfer'] as const).map(method => <button key={method} aria-pressed={payment === method} onClick={() => { if (payment !== method) { changed(); setPayment(method) } }}>{method === 'Cash' ? 'Efectivo' : 'Transferencia'}</button>)}</div></fieldset>
        <p className="payment-note">Verifica que recibiste el pago antes de registrar.</p>
        <div className="checkout"><div><span>Total a cobrar</span><strong>{loading || catalogError || unavailable ? '—' : currency(total)}</strong></div><button className="primary" disabled={locked || loading || !!catalogError || unavailable || count === 0} onClick={() => { setConfirming(true); dialog.current?.showModal() }}>{sending ? 'Registrando…' : error ? 'Reintentar venta' : 'Registrar venta'} <span aria-hidden="true">↗</span></button></div><p className="total-note">Los precios se confirman al registrar.</p>
      </aside>
    </main>
    <footer>MAKARI DRINKS <span>Un sorbo de Zipolite.</span></footer>
    <dialog ref={dialog} aria-labelledby="confirm-title" onCancel={event => { event.preventDefault(); closeConfirmation() }}><h2 id="confirm-title">Confirmar venta</h2><p>{customer === 'Local' ? 'Cliente local' : 'Visitante'} · {payment === 'Cash' ? 'Efectivo' : 'Transferencia'}</p><ul className="confirmation-lines">{lines.map(line => <li key={line.id}>{cart[line.id]} × {line.product} · {line.name}</li>)}</ul><div className="confirm-total">Total estimado <strong>{currency(total)}</strong></div><p>Confirma que ya recibiste el pago. Los precios finales se validan al registrar.</p><div className="dialog-actions"><button disabled={sending} onClick={closeConfirmation}>Volver</button><button className="primary" disabled={sending} onClick={() => void submit()}>{sending ? 'Registrando…' : 'Confirmar y registrar'}</button></div><p role="status">{sending ? 'Espera la confirmación. No cierres esta página.' : ''}</p></dialog>
  </>
}
