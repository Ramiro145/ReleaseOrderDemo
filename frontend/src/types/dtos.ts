// tipos TS espejados de Contracts/Dtos/OrderDto.cs e IReleaseOrderWorkFlow.cs

export interface OrderDto {
  orderId: number
  orderCode: string
  productId: number
  quantity: number
  amount: number
  status: string
  createdAt: string
  updatedAt?: string | null
  address: string
}

export interface CreateOrderRequest {
  orderCode: string
  quantity: number
  productId: number
  amount: number
  address: string
}

export interface ReleaseDecision {
  approved: boolean
  reason?: string | null
}

export interface OrderStatusResponse {
  workflowId: string
  status: string // GetStatus() del workflow (paso actual)
  state: string // estado de ejecución de Temporal (Running/Completed/...)
}

export interface OrderReportResult {
  orderId: number
  status: string
  generatedAt: string
  summary: string
}
