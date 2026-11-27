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
}

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
};
