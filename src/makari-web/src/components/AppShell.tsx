import type { ReactNode } from 'react'
import { Button } from './ui'

export type Page = 'sale' | 'summary' | 'close'
const sections: { id: Page; label: string }[] = [
  { id: 'sale', label: 'Nueva venta' },
  { id: 'summary', label: 'Ventas del día' },
  { id: 'close', label: 'Cierre de caja' },
]

function Navigation({ page, onNavigate }: { page: Page; onNavigate: (page: Page) => void }) {
  return <nav className="main-nav" aria-label="Secciones">{sections.map(section => <Button key={section.id} aria-current={page === section.id ? 'page' : undefined} onClick={() => onNavigate(section.id)}>{section.label}</Button>)}</nav>
}

function AppHeader({ page, onNavigate }: { page: Page; onNavigate: (page: Page) => void }) {
  return <header className="header"><a href="/" className="brand" aria-label="Makari Drinks inicio"><img className="brand-wordmark" src="/brand/wordmark.png" alt="" width="1254" height="1254" /><img className="brand-palm" src="/brand/palm.png" alt="" width="1448" height="1086" /></a><span className="header-label">Punto de venta</span><Navigation page={page} onNavigate={onNavigate} /></header>
}

export function AppShell({ page, onNavigate, children }: { page: Page; onNavigate: (page: Page) => void; children: ReactNode }) {
  return <><a className="skip-link" href="#content">Saltar al contenido</a><AppHeader page={page} onNavigate={onNavigate} /><div id="content" tabIndex={-1}>{children}</div></>
}
