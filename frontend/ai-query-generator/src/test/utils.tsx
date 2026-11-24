import type { ReactElement, ReactNode } from 'react';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ColorModeProvider } from '../theme/ColorModeContext';
import { NotifierProvider } from '../components/Notifier';
import type { QueryResult } from '../api/types';

export function renderWithProviders(ui: ReactElement, { route = '/' }: { route?: string } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const Wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <ColorModeProvider>
          <NotifierProvider>{children}</NotifierProvider>
        </ColorModeProvider>
      </MemoryRouter>
    </QueryClientProvider>
  );
  return { ...render(ui, { wrapper: Wrapper }), queryClient };
}

export const sampleResult: QueryResult = {
  queryHistoryId: 7,
  columns: [
    { name: 'CustomerName', dataType: 'nvarchar', clrType: 'String' },
    { name: 'TotalSales', dataType: 'decimal', clrType: 'Decimal' },
  ],
  rows: [
    ['Olivia Smith', 1500.5],
    ['Liam Johnson', 1200],
    [null, 300],
  ],
  rowCount: 3,
  isTruncated: false,
  maxRows: 1000,
  durationMs: 42,
  executedAt: '2026-11-24T10:00:00Z',
  connectionId: 1,
  connectionName: 'Demo',
  warnings: [],
};

export const allPermissions = [
  'dashboard.view', 'query.generate', 'query.edit_sql', 'query.execute', 'query.explain', 'results.export', 'charts.view',
  'history.view', 'history.delete', 'saved_queries.manage', 'saved_queries.share', 'shared_reports.view', 'schema.view', 'connections.view',
];

export function mockAuth(permissions: string[] = allPermissions) {
  const set = new Set(permissions);
  return {
    user: { userId: 1, fullName: 'Dev User', email: 'dev@test.local', roles: ['Developer'], permissions, lastLoginAt: null },
    initializing: false,
    sessionExpired: false,
    login: vi.fn(),
    logout: vi.fn(),
    hasPermission: (p: string) => set.has(p),
    hasAny: (...ps: string[]) => ps.some((p) => set.has(p)),
  };
}
