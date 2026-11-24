import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders, mockAuth, sampleResult } from '../../test/utils';
import { HistoryPage } from './HistoryPage';
import { historyApi } from '../../api/endpoints';

vi.mock('../../auth/AuthContext', () => ({ useAuth: () => mockAuth() }));
vi.mock('../../api/endpoints', () => ({
  historyApi: { list: vi.fn(), get: vi.fn(), remove: vi.fn(), rerun: vi.fn() },
  usersApi: { list: vi.fn() },
}));

const item = {
  queryId: 11, queryTitle: 'Monthly revenue', userQuestion: 'Calculate monthly revenue', generatedSql: 'SELECT 1', executedSql: 'SELECT 1',
  connectionId: 1, connectionName: 'Demo', userId: 1, userName: 'Dev User', executionDurationMs: 120, returnedRows: 12,
  executionStatus: 'Succeeded' as const, hasParameters: false, createdAt: '2026-11-20T10:00:00Z',
};

describe('HistoryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(historyApi.list).mockResolvedValue({ items: [item], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 });
  });

  it('lists history and filters by search text', async () => {
    renderWithProviders(<HistoryPage />);
    expect(await screen.findByText('Monthly revenue')).toBeInTheDocument();
    expect(screen.getByText('Succeeded')).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Search'), 'rev');
    await waitFor(() => expect(historyApi.list).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'rev', page: 1 })));
  });

  it('re-runs a query and shows the results', async () => {
    vi.mocked(historyApi.rerun).mockResolvedValue(sampleResult);
    renderWithProviders(<HistoryPage />);
    await userEvent.click(await screen.findByRole('button', { name: 'Re-run query' }));
    expect(await screen.findByText('Olivia Smith')).toBeInTheDocument();
    expect(historyApi.rerun).toHaveBeenCalledWith(11);
  });

  it('opens details with the SQL', async () => {
    vi.mocked(historyApi.get).mockResolvedValue({ item: { ...item, generatedSql: 'SELECT 42 AS Answer' }, tablesUsed: [], parameters: [], isOwner: true, explanation: 'Returns the answer.' });
    renderWithProviders(<HistoryPage />);
    await userEvent.click(await screen.findByText('Monthly revenue'));
    expect(await screen.findByText('SELECT 42 AS Answer')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /open in assistant/i })).toBeInTheDocument();
  });
});
