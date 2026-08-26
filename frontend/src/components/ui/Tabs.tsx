import type { ReactNode, KeyboardEvent } from 'react'

export interface TabItem {
  id: string
  label: string
  content: ReactNode
}

interface TabsProps {
  items: TabItem[]
  activeId: string
  onChange: (id: string) => void
}

export function Tabs({ items, activeId, onChange }: TabsProps) {
  function handleKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    const currentIndex = items.findIndex((item) => item.id === activeId)
    if (currentIndex === -1) return

    if (e.key === 'ArrowRight') {
      e.preventDefault()
      onChange(items[(currentIndex + 1) % items.length].id)
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault()
      onChange(items[(currentIndex - 1 + items.length) % items.length].id)
    }
  }

  const activeItem = items.find((item) => item.id === activeId) ?? items[0]

  return (
    <div>
      <div
        role="tablist"
        onKeyDown={handleKeyDown}
        className="flex gap-1 border-b border-slate-200"
      >
        {items.map((item) => {
          const selected = item.id === activeItem.id
          return (
            <button
              key={item.id}
              type="button"
              role="tab"
              aria-selected={selected}
              tabIndex={selected ? 0 : -1}
              onClick={() => onChange(item.id)}
              className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium transition ${
                selected
                  ? 'border-indigo-600 text-indigo-700'
                  : 'border-transparent text-slate-500 hover:text-slate-700'
              }`}
            >
              {item.label}
            </button>
          )
        })}
      </div>
      <div role="tabpanel" className="pt-4">
        {activeItem.content}
      </div>
    </div>
  )
}
