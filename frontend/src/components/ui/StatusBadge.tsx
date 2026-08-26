const STATUS_STYLES: Record<string, string> = {
  Completed: 'bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-200',
  Failed: 'bg-red-50 text-red-700 ring-1 ring-inset ring-red-200',
  Compensated: 'bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-200',
  CompensationFailed: 'bg-red-50 text-red-700 ring-1 ring-inset ring-red-200',
}

const DEFAULT_STATUS_STYLE = 'bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-200'

export function StatusBadge({ status }: { status: string }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
        STATUS_STYLES[status] ?? DEFAULT_STATUS_STYLE
      }`}
    >
      {status}
    </span>
  )
}
