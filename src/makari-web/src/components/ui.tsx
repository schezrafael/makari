import type { ButtonHTMLAttributes, ReactNode } from 'react'

export function Button({ className = '', ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button type="button" className={className} {...props} />
}

export function PageHeader({ title, description, children, id }: { title: string; description: string; children?: ReactNode; id?: string }) {
  return <div className="page-header"><div><p className="eyebrow">MAKARI DRINKS · PUNTO DE VENTA</p><h1 id={id}>{title}</h1><p>{description}</p></div>{children}</div>
}

const money = new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' })
export function MoneyDisplay({ value }: { value: number }) {
  return <span className="money">{money.format(value)}</span>
}

export function MetricCard({ label, children }: { label: string; children: ReactNode }) {
  return <article className="summary-card"><span>{label}</span><strong>{children}</strong></article>
}

export function StatusMessage({ children }: { children: ReactNode }) {
  return <p className="notice" role="status">{children}</p>
}
