/**
 * Access tokens are held in memory only (never localStorage) to limit exposure to XSS.
 * The refresh token lives in an HttpOnly cookie that JavaScript cannot read.
 */
let accessToken: string | null = null;
const listeners = new Set<(token: string | null) => void>();

export const tokenStore = {
  get: () => accessToken,
  set(token: string | null) {
    accessToken = token;
    listeners.forEach((l) => l(token));
  },
  subscribe(listener: (token: string | null) => void) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};
