import { AxiosRequestConfig } from 'axios';
import apiClient from '../services/apiClient';

export const customInstance = <T>(
  config: AxiosRequestConfig,
  options?: AxiosRequestConfig
): Promise<T> => {
  let url = config.url || '';
  // If url starts with /api and apiClient has baseURL ending in /api, strip /api to avoid /api/api/...
  if (url.startsWith('/api/') || url === '/api') {
    url = url.substring(4) || '/';
  }

  return apiClient({
    ...config,
    ...options,
    url,
    headers: {
      ...config.headers,
      ...options?.headers,
    },
  }).then((response) => response.data);
};

export default customInstance;
