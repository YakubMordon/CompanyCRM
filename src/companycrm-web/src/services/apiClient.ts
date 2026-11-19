import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';

export const apiClient = axios.create({
  baseURL: '/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor: attach Bearer token
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('auth_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error: AxiosError) => Promise.reject(error)
);

// Response interceptor: handle 401 and 403
apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError) => {
    if (error.response?.status === 401) {
      // Token is expired or invalid
      localStorage.removeItem('auth_token');
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    } else if (error.response?.status === 403) {
      // 403 Forbidden: do not remove token! Notify listeners
      window.dispatchEvent(
        new CustomEvent('auth:forbidden', {
          detail: {
            url: error.config?.url,
            message: (error.response.data as any)?.detail || 'Access Restricted to HigherManagement.',
          },
        })
      );
    }
    return Promise.reject(error);
  }
);

export default apiClient;
