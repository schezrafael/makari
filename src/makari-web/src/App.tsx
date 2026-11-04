import SalesPage from './features/sales/SalesPage'
import DailySalesPage from './features/sales/DailySalesPage'
import CashClosePage from './features/cash-closes/CashClosePage'
import { useState } from 'react'
import { AppShell } from './components/AppShell'

export default function App() {
  const [page, setPage] = useState<'sale' | 'summary' | 'close'>('sale')
  return <AppShell page={page} onNavigate={setPage}>
    {page === 'sale' ? <SalesPage /> : page === 'summary' ? <DailySalesPage /> : <CashClosePage />}
  </AppShell>
}
