import { StatusMessage, PageHeader, MetricCard, MoneyDisplay } from '../../components/ui'
import { useEffect, useState } from 'react'
import { fetchDailySummary } from './dailySummary'
import type { DailySummary } from './dailySummary'

const currency = (value: number) => new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(value)

export default function DailySalesPage() {
  const [date, setDate] = useState('')
  const [summary, setSummary] = useState<DailySummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [reload, setReload] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    void fetchDailySummary(date || undefined, controller.signal).then(data => {
      if (!controller.signal.aborted) {
        setSummary(data)
      }
    }).catch(cause => {
      if (!controller.signal.aborted) setError(cause instanceof Error ? cause.message : 'No se pudo conectar con la API.')
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [date, reload])

  return <main className="summary-layout">
    <PageHeader title="Ventas del día" description="Resumen de ventas registradas en la fecha local del negocio."><label className="summary-date">Fecha<input type="date" value={date || summary?.date || ''} onChange={event => { setDate(event.target.value); setLoading(true); setError('') }} aria-label="Fecha del resumen" /></label></PageHeader>
    {loading ? <StatusMessage>Cargando resumen…</StatusMessage> : error ? <div className="notice error summary-error" role="alert"><p>{error}</p><button onClick={() => { setLoading(true); setError(''); setReload(value => value + 1) }}>Reintentar</button></div> : summary && <>
      <section className="summary-cards" aria-label="Totales del día">
        <MetricCard label="Total vendido"><MoneyDisplay value={summary.totalRevenue} /></MetricCard>
        <MetricCard label="Ventas registradas">{summary.salesCount}</MetricCard>
        <MetricCard label="Efectivo"><MoneyDisplay value={summary.cashRevenue} /></MetricCard>
        <MetricCard label="Transferencias"><MoneyDisplay value={summary.transferRevenue} /></MetricCard>
      </section>
      <section className="summary-variants" aria-labelledby="variants-title"><h2 id="variants-title">Bebidas vendidas <span className="badge">{summary.unitsSold} unidades</span></h2>
        {summary.variants.length === 0 ? <div className="summary-empty"><span aria-hidden="true">✳</span><p>No hay ventas registradas para esta fecha.</p></div> : <ul>{summary.variants.map(variant => <li key={variant.productVariantId}><div><strong>{variant.productName} · {variant.variantName}</strong><span>{variant.quantity} {variant.quantity === 1 ? 'unidad' : 'unidades'}</span></div><strong>{currency(variant.revenue)}</strong></li>)}</ul>}
      </section>
      <p className="payment-note">Zona horaria: {summary.timeZoneId}</p>
    </>}
  </main>
}
