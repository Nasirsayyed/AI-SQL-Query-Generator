import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { App } from './App';
import { AuthProvider } from './auth/AuthContext';
import { ColorModeProvider } from './theme/ColorModeContext';
import { NotifierProvider } from './components/Notifier';
import { ApiError } from './api/client';
import { config } from './config';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
    },
    mutations: { retry: false },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter basename={config.basePath}>
        <ColorModeProvider>
          <NotifierProvider>
            <AuthProvider>
              <App />
            </AuthProvider>
          </NotifierProvider>
        </ColorModeProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
