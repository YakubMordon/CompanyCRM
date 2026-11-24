export interface RequestConfig {
  url?: string;
  method?: string;
  baseURL?: string;
  params?: Record<string, any>;
  data?: any;
  headers?: Record<string, string>;
  signal?: AbortSignal;
}

export interface ApiResponse<T = any> {
  data: T;
  status: number;
  statusText: string;
  headers: Headers;
}

export class ApiError extends Error {
  response?: {
    status: number;
    statusText: string;
    data: any;
  };
  config?: RequestConfig;

  constructor(
    message: string,
    response?: { status: number; statusText: string; data: any },
    config?: RequestConfig
  ) {
    super(message);
    this.name = 'ApiError';
    this.response = response;
    this.config = config;
  }
}

export async function apiClient<T = any>(config: RequestConfig): Promise<ApiResponse<T>> {
  const baseURL = config.baseURL ?? '/api';
  let url = config.url ?? '';
  if (!url.startsWith('http://') && !url.startsWith('https://')) {
    if (url.startsWith('/')) {
      url = `${baseURL}${url}`;
    } else {
      url = `${baseURL}/${url}`;
    }
  }

  if (config.params) {
    const searchParams = new URLSearchParams();
    for (const [key, val] of Object.entries(config.params)) {
      if (val !== undefined && val !== null) {
        searchParams.append(key, String(val));
      }
    }
    const queryString = searchParams.toString();
    if (queryString) {
      url += (url.includes('?') ? '&' : '?') + queryString;
    }
  }

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(config.headers || {}),
  };

  const token = localStorage.getItem('auth_token');
  if (token && !headers['Authorization'] && !headers['authorization']) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  const method = (config.method || 'GET').toUpperCase();
  let body: BodyInit | undefined;
  if (config.data !== undefined && method !== 'GET' && method !== 'HEAD') {
    body = typeof config.data === 'string' ? config.data : JSON.stringify(config.data);
  }

  let res: Response;
  try {
    res = await fetch(url, {
      method,
      headers,
      body,
      signal: config.signal,
    });
  } catch (err: any) {
    throw new ApiError(err?.message || 'Network error', undefined, config);
  }

  let responseData: any = null;
  const contentType = res.headers.get('content-type') || '';
  if (res.status !== 204) {
    if (contentType.includes('application/json')) {
      responseData = await res.json().catch(() => null);
    } else {
      responseData = await res.text().catch(() => null);
    }
  }

  if (!res.ok) {
    if (res.status === 401) {
      localStorage.removeItem('auth_token');
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    } else if (res.status === 403) {
      window.dispatchEvent(
        new CustomEvent('auth:forbidden', {
          detail: {
            url: config.url,
            message:
              responseData?.detail ||
              responseData?.message ||
              'Access Restricted to HigherManagement.',
          },
        })
      );
    }
    throw new ApiError(
      responseData?.detail ||
        responseData?.message ||
        `Request failed with status ${res.status}`,
      {
        status: res.status,
        statusText: res.statusText,
        data: responseData,
      },
      config
    );
  }

  return {
    data: responseData,
    status: res.status,
    statusText: res.statusText,
    headers: res.headers,
  };
}

apiClient.get = <T = any>(url: string, config?: RequestConfig) =>
  apiClient<T>({ ...config, url, method: 'GET' });

apiClient.post = <T = any>(url: string, data?: any, config?: RequestConfig) =>
  apiClient<T>({ ...config, url, data, method: 'POST' });

apiClient.put = <T = any>(url: string, data?: any, config?: RequestConfig) =>
  apiClient<T>({ ...config, url, data, method: 'PUT' });

apiClient.delete = <T = any>(url: string, config?: RequestConfig) =>
  apiClient<T>({ ...config, url, method: 'DELETE' });

export default apiClient;
