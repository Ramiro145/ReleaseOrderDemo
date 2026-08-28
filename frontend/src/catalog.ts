// Catálogo de la tienda. Espejado a mano de los productos sembrados en
// scripts/db/init.sql (tabla Products). El backend sólo expone ProductId, así
// que acá le ponemos nombre, código y precio de referencia para el mostrador.
// Si aparece un id que no está sembrado, se cae a "Producto #id".

export interface CatalogProduct {
  productId: number
  code: string
  tag: string // sello corto para el mostrador, p. ej. "LP"
  name: string
  unitPrice: number
}

export const CATALOG: CatalogProduct[] = [
  { productId: 1, code: 'P-1001', tag: 'LP', name: 'Laptop', unitPrice: 100 },
  { productId: 2, code: 'P-1002', tag: 'MS', name: 'Mouse', unitPrice: 150 },
  { productId: 3, code: 'P-1003', tag: 'KB', name: 'Keyboard', unitPrice: 200 },
]

const BY_ID = new Map(CATALOG.map((p) => [p.productId, p]))

export function productName(productId: number): string {
  return BY_ID.get(productId)?.name ?? `Producto #${productId}`
}

export function productTag(productId: number): string {
  return BY_ID.get(productId)?.tag ?? String(productId).padStart(2, '0')
}
