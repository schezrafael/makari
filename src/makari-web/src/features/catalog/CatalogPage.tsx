import { useEffect, useState } from 'react'
import { Button, MoneyDisplay, PageHeader, StatusMessage } from '../../components/ui'
import { catalogRequest, type Product, type Variant } from './catalog'

type Editor = { kind: 'product' | 'variant' | 'prices'; product?: Product; variant?: Variant }

export default function CatalogPage() {
  const [products, setProducts] = useState<Product[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [editor, setEditor] = useState<Editor | null>(null)
  const [saving, setSaving] = useState(false)

  async function reload() {
    setLoading(true)
    setError('')
    try { setProducts(await catalogRequest<Product[]>()) }
    catch (e) { setError(e instanceof Error ? e.message : 'No se pudo cargar el catálogo.') }
    finally { setLoading(false) }
  }
  useEffect(() => { let cancelled = false
    catalogRequest<Product[]>().then(data => { if (!cancelled) setProducts(data) }).catch(() => { if (!cancelled) setError('No se pudo cargar el catálogo.') }).finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  function open(next: Editor) { setEditor(next); setSuccess(''); setError('') }
  async function save(form: HTMLFormElement) {
    if (!editor || saving) return
    const data = new FormData(form)
    const { product, variant, kind } = editor
    let path = product ? `/${product.id}` : ''
    const body: Record<string, unknown> = { version: product?.version }
    if (kind !== 'prices') { body.name = data.get('name'); body.isActive = data.get('isActive') === 'on' }
    if (kind === 'product' && !product) body.category = data.get('category')
    if (kind !== 'product') {
      path += `/variants${variant ? `/${variant.id}` : ''}`
      if (kind === 'variant') body.sizeMl = Number(data.get('sizeMl'))
      if (kind === 'prices' || !variant) {
        body.visitorPrice = Number(data.get('visitorPrice')); body.localPrice = Number(data.get('localPrice'))
      }
      if (kind === 'prices') path += '/prices'
    }
    setSaving(true); setError(''); setSuccess('')
    try {
      await catalogRequest(path, (!product || (kind === 'variant' && !variant)) ? 'POST' : 'PUT', body)
      setEditor(null)
      setSuccess('Cambios guardados.')
      await reload()
    } catch (e) { setError(e instanceof Error ? e.message : 'No se pudo guardar. Intenta de nuevo.') }
    finally { setSaving(false) }
  }

  return <main className="catalog-admin summary-layout">
    <PageHeader title="Catálogo" description="Administra bebidas, presentaciones y precios.">
      <Button className="primary" disabled={loading || saving || !!editor} onClick={() => open({ kind: 'product' })}>Nueva bebida</Button>
    </PageHeader>
    {success && <StatusMessage>{success}</StatusMessage>}
    {error && <div role="alert" className="notice error"><p>{error}</p><Button disabled={loading || saving} onClick={() => { setEditor(null); void reload() }}>Recargar catálogo</Button>{editor && <p>Recargar descarta el formulario y permite revisar la versión actual.</p>}</div>}
    {editor && <form className="catalog-editor" onSubmit={event => { event.preventDefault(); void save(event.currentTarget) }}>
      <h2>{editor.kind === 'prices' ? 'Editar precios' : editor.kind === 'product' ? editor.product ? 'Editar bebida' : 'Nueva bebida' : editor.variant ? 'Editar variante' : 'Nueva variante'}</h2>
      {editor.product && <p>{editor.product.name}{editor.variant ? ` · ${editor.variant.name} · ${editor.variant.sizeMl} ml` : ''}</p>}
      <fieldset disabled={saving}>
        {editor.kind !== 'prices' && <label>Nombre<input autoFocus name="name" required maxLength={100} defaultValue={editor.kind === 'product' ? editor.product?.name : editor.variant?.name} /></label>}
        {editor.kind === 'product' && !editor.product && <label>Categoría<input name="category" required maxLength={100} /></label>}
        {editor.kind === 'variant' && <label>Presentación (ml)<input name="sizeMl" type="number" min="1" max="2147483647" step="1" required defaultValue={editor.variant?.sizeMl} /></label>}
        {(editor.kind === 'prices' || (editor.kind === 'variant' && !editor.variant)) && <div className="catalog-prices">{(['Visitor', 'Local'] as const).map(type => <label key={type}>Precio {type} (MXN)<input name={`${type.toLowerCase()}Price`} type="number" inputMode="decimal" min="0" max="9999999999.99" step="0.01" required defaultValue={editor.variant?.[type === 'Visitor' ? 'visitorPrice' : 'localPrice'] ?? ''} /></label>)}</div>}
        {editor.kind !== 'prices' && (editor.variant || (editor.kind === 'product' && editor.product)) && <label className="catalog-active"><input name="isActive" type="checkbox" defaultChecked={editor.variant?.isActive ?? editor.product?.isActive} />Activa</label>}
        <p>Al guardar, los cambios estarán disponibles para las nuevas ventas.</p>
        <div className="catalog-actions"><Button className="primary" type="submit">{saving ? 'Guardando…' : 'Guardar cambios'}</Button><Button onClick={() => { setEditor(null); setError('') }}>Cancelar</Button></div>
      </fieldset>
    </form>}
    {loading ? <StatusMessage>Cargando catálogo…</StatusMessage> : <div className="catalog-list">
      {!products.length && !error && <StatusMessage>Aún no hay bebidas. Crea la primera para comenzar.</StatusMessage>}
      {products.map(product => <article className="catalog-card" key={product.id}>
        <header><div><h2>{product.name}</h2><p>{product.category} · <span className={product.isActive ? 'catalog-enabled' : 'catalog-disabled'}>{product.isActive ? 'Activa' : 'Inactiva'}</span></p></div><Button disabled={!!editor} onClick={() => open({ kind: 'product', product })}>Editar bebida</Button></header>
        {!product.isActive && <p className="notice">Esta bebida y sus variantes no están disponibles para vender.</p>}
        {product.variants.map(variant => <section className="catalog-variant" key={variant.id}>
          <h3>{variant.name} · {variant.sizeMl} ml</h3><p>{variant.isActive ? 'Activa' : 'Inactiva'}</p>
          <dl className="catalog-prices">{(['Visitor', 'Local'] as const).map(type => { const price = variant[type === 'Visitor' ? 'visitorPrice' : 'localPrice']; return <div key={type}><dt>{type}</dt><dd>{price === null ? 'Sin precio' : <MoneyDisplay value={price} />}</dd></div> })}</dl>
          <div className="catalog-actions"><Button disabled={!!editor} onClick={() => open({ kind: 'variant', product, variant })}>Editar variante</Button><Button disabled={!!editor} onClick={() => open({ kind: 'prices', product, variant })}>Editar precios</Button></div>
        </section>)}
        {!product.variants.length && <p>Sin variantes. Agrega una presentación y sus precios para vender.</p>}
        <Button disabled={!!editor} onClick={() => open({ kind: 'variant', product })}>Agregar variante</Button>
      </article>)}
    </div>}
  </main>
}
