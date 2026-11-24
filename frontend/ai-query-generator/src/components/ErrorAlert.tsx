import { Alert, AlertTitle } from '@mui/material';
import { toApiError } from '../api/client';

/** Shows the server's user-friendly message (never raw stack traces) plus field errors and trace id. */
export function ErrorAlert({ error, title, onClose }: { error: unknown; title?: string; onClose?: () => void }) {
  if (!error) return null;
  const e = toApiError(error);
  return (
    <Alert severity={e.status === 0 || e.status >= 500 ? 'error' : 'warning'} onClose={onClose} sx={{ mb: 2 }}>
      {title && <AlertTitle>{title}</AlertTitle>}
      {e.message}
      {e.errors.length > 0 && (
        <ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>
          {e.errors.slice(0, 8).map((x, i) => (
            <li key={i}>{x.message}</li>
          ))}
        </ul>
      )}
      {e.traceId && e.status >= 500 && <div style={{ fontSize: 12, opacity: 0.7, marginTop: 4 }}>Reference: {e.traceId}</div>}
    </Alert>
  );
}
