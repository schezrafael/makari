import SalesPage from './features/sales/SalesPage'
import DailySalesPage from './features/sales/DailySalesPage'
import { useState } from 'react'

export default function App() {
  const [page, setPage] = useState<'sale' | 'summary'>('sale')
  return <>
    <header className="header"><a href="/" className="brand" aria-label="Makari Drinks inicio">makari<span>DRINKS · ZIPOLITE</span></a><nav className="main-nav" aria-label="Secciones"><button aria-current={page === 'sale' ? 'page' : undefined} onClick={() => setPage('sale')}>Nueva venta</button><button aria-current={page === 'summary' ? 'page' : undefined} onClick={() => setPage('summary')}>Ventas del día</button></nav></header>
    {page === 'sale' ? <SalesPage /> : <DailySalesPage />}
  </>
}
