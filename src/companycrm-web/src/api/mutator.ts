import apiClient, { RequestConfig } from '../services/apiClient';

export type AxiosRequestConfig = RequestConfig;

export const customInstance = <T>(
  config: RequestConfig,
  options?: RequestConfig
): Promise<T> => {
  let url = config.url || '';
  // If url starts with /api and apiClient has baseURL ending in /api, strip /api to avoid /api/api/...
  if (url.startsWith('/api/') || url === '/api') {
    url = url.substring(4) || '/';
  }

  return apiClient<T>({
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
