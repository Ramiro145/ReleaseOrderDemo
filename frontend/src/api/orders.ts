import { apiClient } from './client'
import type {
  CreateOrderRequest,
  OrderDto,
  OrderReportResult,
  OrderStatusResponse,
  ReleaseDecision,
} from '../types/dtos'

export function listOrders(): Promise<OrderDto[]> {
  return apiClient.get<OrderDto[]>('/orders')
}

export function createOrder(request: CreateOrderRequest): Promise<{
  orderId: number
  productId: number
  amount: number
  status: string
}> {
  return apiClient.post('/orders', request)
}

export function releaseOrder(orderId: number): Promise<{ workflowId: string; nextStep: string }> {
  return apiClient.post(`/orders/${orderId}/release`)
}

export function sendReleaseDecisionSignal(
  orderId: number,
  decision: ReleaseDecision,
): Promise<{ workflowId: string; approved: boolean; reason?: string | null }> {
  return apiClient.post(`/orders/${orderId}/release/decision`, decision)
}

export function sendReleaseDecisionUpdate(
  orderId: number,
  decision: ReleaseDecision,
): Promise<{
  workflowId: string
  approved: boolean
  reason?: string | null
  result: string
}> {
  return apiClient.post(`/orders/${orderId}/release/decision-update`, decision)
}

export function getOrderStatus(orderId: number): Promise<OrderStatusResponse> {
  return apiClient.get<OrderStatusResponse>(`/orders/${orderId}/status`)
}

export function getOrderReport(orderId: number): Promise<{ orderId: number; report: OrderReportResult }> {
  return apiClient.get(`/reports/${orderId}`)
}
