// Traducción de los pasos crudos del ReleaseOrderWorkFlow (_status en
// src/ReleaseOrder/Workflows/ReleaseOrderWorkFlow.cs) al vocabulario de la
// tienda que se imprime en el ticket. Si cambia un string de _status en el
// backend, actualizar acá.

export type TraceKind = 'done' | 'wait' | 'ok' | 'bad' | 'reversed'

interface StepMeta {
  label: string
  kind: TraceKind
}

// Secuencia feliz, en orden. Sirve para rellenar pasos anteriores que el
// polling (cada 2,5 s) no llegó a ver de a uno.
export const FORWARD_STEPS = [
  'Starting',
  'Loading order',
  'Reserving inventory',
  'Processing payment',
  'Waiting for release decision',
  'Completing order',
  'Shipping order',
  'Completed',
] as const

const META: Record<string, StepMeta> = {
  Starting: { label: 'Pedido recibido', kind: 'done' },
  'Loading order': { label: 'Buscando el pedido', kind: 'done' },
  'Reserving inventory': { label: 'Stock reservado', kind: 'done' },
  'Processing payment': { label: 'Pago aprobado', kind: 'done' },
  'Waiting for release decision': { label: 'Esperando tu confirmación', kind: 'wait' },
  'Completing order': { label: 'Cerrando el pedido', kind: 'done' },
  'Shipping order': { label: 'Preparando el envío', kind: 'done' },
  Completed: { label: 'Entregado', kind: 'ok' },
  Compensating: { label: 'Revirtiendo los pasos', kind: 'reversed' },
  Compensated: { label: 'Pedido anulado y revertido', kind: 'bad' },
  CompensationFailed: { label: 'Reverso incompleto — revisar a mano', kind: 'bad' },
  Failed: { label: 'Pedido fallido', kind: 'bad' },
}

const GLYPH: Record<TraceKind, string> = {
  done: '✓',
  wait: '···',
  ok: '✓✓',
  bad: '✗',
  reversed: '↺',
}

const COMPENSATION_STEPS = new Set(['Compensating', 'Compensated', 'CompensationFailed', 'Failed'])
const TERMINAL_STEPS = new Set(['Completed', 'Compensated', 'CompensationFailed', 'Failed'])

export function stepMeta(step: string): StepMeta {
  return META[step] ?? { label: step, kind: 'done' }
}

export function stepGlyph(kind: TraceKind): string {
  return GLYPH[kind]
}

export function isCompensationStep(step: string): boolean {
  return COMPENSATION_STEPS.has(step)
}

export function isTerminalStep(step: string): boolean {
  return TERMINAL_STEPS.has(step)
}

export function isWaitingStep(step: string): boolean {
  return step === 'Waiting for release decision'
}
