import { createContext, useCallback, useContext, useState, type ReactNode } from 'react';
import { Alert, Snackbar, type AlertColor } from '@mui/material';

const NotifyContext = createContext<(message: string, severity?: AlertColor) => void>(() => undefined);

export function NotifierProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{ message: string; severity: AlertColor; key: number } | null>(null);
  const notify = useCallback((message: string, severity: AlertColor = 'success') => setState({ message, severity, key: Date.now() }), []);
  return (
    <NotifyContext.Provider value={notify}>
      {children}
      <Snackbar key={state?.key} open={!!state} autoHideDuration={4500} onClose={() => setState(null)} anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}>
        <Alert severity={state?.severity ?? 'info'} variant="filled" onClose={() => setState(null)} sx={{ width: '100%' }}>
          {state?.message}
        </Alert>
      </Snackbar>
    </NotifyContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export const useNotify = () => useContext(NotifyContext);
