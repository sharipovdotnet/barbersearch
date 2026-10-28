import { apiRequest } from '@/lib/api'

/**
 * Antiforgery-токен для cookie-мутаций.
 *
 * Токен привязан к текущей личности и не кэшируется: после входа старый становится
 * недействительным, поэтому каждый изменяющий запрос берёт свежий.
 */
type CsrfToken = {
  token: string
}

const CSRF_HEADER = 'X-CSRF-TOKEN'

export async function fetchCsrfToken(): Promise<string> {
  const { token } = await apiRequest<CsrfToken>('/auth/csrf')
  return token
}

export async function csrfHeaders(): Promise<Record<string, string>> {
  return { [CSRF_HEADER]: await fetchCsrfToken() }
}
