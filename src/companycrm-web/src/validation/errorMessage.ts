export function getApiErrorMessage(err: any, fallback = 'An unexpected error occurred.'): string {
  if (!err) return fallback;

  // Check err.response.data
  const data = err.response?.data;
  if (data) {
    if (typeof data === 'string') return data;
    if (data.message) return data.message;
    if (data.detail) return data.detail;
    if (data.title && !data.errors) return data.title;
    if (data.errors && typeof data.errors === 'object') {
      const fieldErrors = Object.values(data.errors).flat();
      if (fieldErrors.length > 0 && typeof fieldErrors[0] === 'string') {
        return fieldErrors[0];
      }
    }
  }

  if (err.message) return err.message;
  return fallback;
}
