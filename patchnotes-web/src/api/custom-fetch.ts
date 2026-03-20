import { ApiError, API_ROOT } from './client.ts'

/**
 * Custom fetch instance for Orval-generated API hooks.
 *
 * Orval calls this as customFetch<T>(url, requestInit) where T is the
 * generated response type (e.g. { data: PackageDto[], status: 200, headers: Headers }).
 *
 * The generated URLs already include the /api prefix, so we prepend only
 * the base URL (e.g. https://api.myreleasenotes.ai) without /api.
 */
export const customFetch = async <T>(
  url: string,
  init: RequestInit
): Promise<T> => {
  const config: RequestInit = {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...init.headers,
    },
  }

  let response: Response
  try {
    response = await fetch(`${API_ROOT}${url}`, config)
  } catch (error) {
    throw new ApiError(
      0,
      error instanceof Error ? error.message : 'Network request failed',
      undefined,
      true
    )
  }

  if (!response.ok) {
    const errorData = await response.json().catch(() => null)
    throw new ApiError(response.status, response.statusText, errorData)
  }

  const data = response.status === 204 ? undefined : await response.json()

  return { data, status: response.status, headers: response.headers } as T
}
