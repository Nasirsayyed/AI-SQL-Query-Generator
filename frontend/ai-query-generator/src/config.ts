/** Runtime configuration resolved from Vite build-time variables. No secrets belong here. */
export const config = {
  /** API origin; empty string means same origin as the SPA. */
  apiBaseUrl: (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, ''),
  /** Router basename derived from the Vite `base` option (supports sub-directory hosting). */
  basePath: import.meta.env.BASE_URL.replace(/\/$/, '') || '/',
  appName: 'AI SQL Query Generator',
};
