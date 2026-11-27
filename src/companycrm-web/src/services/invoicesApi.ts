import { apiClient } from './apiClient';

export type InvoiceStatus = 'Draft' | 'Sent' | 'Paid' | 'Void';

export interface InvoiceLineItemDto {
  id: string;
  employeeId: string;
  employeeName: string;
  description: string;
  hours: number;
  hourlyRate: number;
  amount: number;
}

export interface InvoiceDto {
  id: string;
  invoiceNumber: string;
  projectId: string;
  projectName: string;
  projectCode: string;
  year: number;
  month: number;
  status: InvoiceStatus;
  totalBillableHours: number;
  subtotalAmount: number;
  taxRatePercent: number;
  taxAmount: number;
  totalAmount: number;
  currency: string;
  generatedAt: string;
  dueDate: string;
  lineItems: InvoiceLineItemDto[];
  stripeCheckoutSessionId?: string | null;
  stripePaymentUrl?: string | null;
  paidAt?: string | null;
  lastExportedProvider?: string | null;
  lastExportedAt?: string | null;
}

export interface StripeCheckoutSessionDto {
  invoiceId: string;
  sessionId: string;
  checkoutUrl: string;
  isSimulated: boolean;
}

export type AccountingExportFormat = 'XeroCsv' | 'QuickBooksCsv' | 'QuickBooksIif';

export interface GenerateInvoicesRequestDto {
  year: number;
  month: number;
  projectId?: string | null;
  taxRatePercent?: number;
}

export const invoicesApi = {
  getInvoices: async (params?: {
    projectId?: string;
    year?: number;
    month?: number;
  }): Promise<InvoiceDto[]> => {
    const response = await apiClient.get<InvoiceDto[]>('/invoices', { params });
    return response.data;
  },

  generateMonthly: async (dto: GenerateInvoicesRequestDto): Promise<InvoiceDto[]> => {
    const response = await apiClient.post<InvoiceDto[]>('/invoices/generate', dto);
    return response.data;
  },

  updateStatus: async (id: string, status: InvoiceStatus): Promise<InvoiceDto> => {
    const response = await apiClient.patch<InvoiceDto>(`/invoices/${id}/status`, { status });
    return response.data;
  },

  downloadPdf: async (id: string, invoiceNumber: string): Promise<void> => {
    const response = await apiClient.get<Blob>(`/invoices/${id}/pdf`, {
      responseType: 'blob',
    });
    const url = window.URL.createObjectURL(new Blob([response.data], { type: 'application/pdf' }));
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', `${invoiceNumber}.pdf`);
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },

  createStripeCheckout: async (id: string): Promise<StripeCheckoutSessionDto> => {
    const response = await apiClient.post<StripeCheckoutSessionDto>(`/invoices/${id}/stripe/checkout`, {});
    return response.data;
  },

  simulateStripePayment: async (id: string): Promise<InvoiceDto> => {
    const response = await apiClient.post<InvoiceDto>(`/invoices/${id}/stripe/simulate-payment`);
    return response.data;
  },

  exportInvoices: async (
    format: AccountingExportFormat,
    params?: { projectId?: string; year?: number; month?: number }
  ): Promise<void> => {
    const response = await apiClient.post<Blob>(
      '/invoices/export',
      { format, ...params },
      { responseType: 'blob' }
    );
    const ext = format === 'QuickBooksIif' ? 'iif' : 'csv';
    const prefix = format.startsWith('Xero') ? 'xero' : 'quickbooks';
    const url = window.URL.createObjectURL(new Blob([response.data]));
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', `companycrm-${prefix}-invoices.${ext}`);
    document.body.appendChild(link);
    link.click();
    link.remove();
    window.URL.revokeObjectURL(url);
  },
};
