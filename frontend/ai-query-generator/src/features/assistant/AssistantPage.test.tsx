import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders, mockAuth, sampleResult } from '../../test/utils';
import { AssistantPage } from './AssistantPage';
import { EXAMPLE_QUESTIONS } from './exampleQuestions';
import { aiApi, sqlApi } from '../../api/endpoints';

const auth = { current: mockAuth() };
const setActiveConnectionId = vi.fn();
vi.mock('../../auth/AuthContext', () => ({ useAuth: () => auth.current }));
vi.mock('../../context/ActiveConnectionContext', () => ({
  useActiveConnection: () => ({
    connections: [
      { connectionId: 1, connectionName: 'Demo', databaseName: 'AIQueryDemoDB', isActive: true, status: 'Healthy' },
      { connectionId: 2, connectionName: 'Sales', databaseName: 'SalesDB', isActive: true, status: 'Healthy' },
    ],
    activeConnection: { connectionId: 1, connectionName: 'Demo', databaseName: 'AIQueryDemoDB', isActive: true, status: 'Healthy' },
    setActiveConnectionId,
    isLoading: false,
  }),
}));
// Monaco cannot run in jsdom; a textarea stands in for the editor.
vi.mock('../../components/SqlEditor', () => ({
  default: ({ value, onChange, readOnly }: { value: string; onChange: (v: string) => void; readOnly?: boolean }) => (
    <textarea aria-label="SQL editor" value={value} readOnly={readOnly} onChange={(e) => onChange(e.target.value)} />
  ),
}));
vi.mock('../../components/EChart', () => ({ EChart: ({ ariaLabel }: { ariaLabel: string }) => <div role="img" aria-label={ariaLabel} /> }));
vi.mock('../../api/endpoints', () => ({
  aiApi: { status: vi.fn(), generateSql: vi.fn(), suggestChart: vi.fn(), explainSql: vi.fn(), optimizeSql: vi.fn() },
  sqlApi: { validate: vi.fn(), execute: vi.fn(), format: vi.fn(), explainPlan: vi.fn() },
  schemaApi: { overview: vi.fn(), completions: vi.fn() },
  savedQueriesApi: { create: vi.fn(), categories: vi.fn() },
}));

const SQL = 'SELECT TOP (10) c.CustomerName, SUM(o.TotalAmount) AS TotalSales FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.CustomerId GROUP BY c.CustomerName';
const validation = (token: string | null, isValid = true) => ({
  isValid, message: isValid ? 'ok' : 'Only read-only SELECT queries are allowed.', errors: isValid ? [] : [{ code: 'STATEMENT_NOT_ALLOWED', message: 'DROP TABLE statements are not allowed.' }],
  warnings: [], referencedObjects: isValid ? ['dbo.Customers', 'dbo.Orders'] : [], referencedParameters: [], validationToken: token, validatedAt: '',
});

describe('AssistantPage', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    auth.current = mockAuth();
    vi.mocked(aiApi.status).mockResolvedValue({ configured: true, providerName: 'OpenAI', model: 'gpt-4o-mini' });
    vi.mocked(aiApi.suggestChart).mockResolvedValue({ recommended: false, source: 'heuristic' });
    vi.mocked((await import('../../api/endpoints')).schemaApi.overview).mockResolvedValue({ connectionId: 1, databaseName: 'AIQueryDemoDB', canConfigure: false, schemas: [{ schemaName: 'dbo', isAllowed: true, tables: [] }], relationships: [] });
    vi.mocked((await import('../../api/endpoints')).schemaApi.completions).mockResolvedValue([]);
    vi.mocked(aiApi.generateSql).mockResolvedValue({
      success: true, needsClarification: false, sqlQuery: SQL, queryTitle: 'Top 10 Customers by Sales', explanation: 'Sums order totals per customer.',
      tablesUsed: ['dbo.Customers'], columnsUsed: [], parameters: [], warnings: [], validation: validation('tok-1'), queryHistoryId: 9, model: 'gpt-4o-mini', promptVersion: 'v1',
      chartSuggestion: { recommended: true, chartType: 'bar', xAxis: 'CustomerName', yAxis: 'TotalSales', aggregation: 'none', source: 'ai' },
    });
  });

  it('shows example questions and fills the input when one is clicked', async () => {
    renderWithProviders(<AssistantPage />);
    await userEvent.click(screen.getByText(EXAMPLE_QUESTIONS[1]));
    expect(screen.getByLabelText(/ask a question/i)).toHaveValue(EXAMPLE_QUESTIONS[1]);
  });

  it('switches the active database', async () => {
    renderWithProviders(<AssistantPage />);
    await userEvent.click(screen.getByRole('combobox', { name: /database/i }));
    await userEvent.click(await screen.findByRole('option', { name: /Sales \(SalesDB\)/ }));
    expect(setActiveConnectionId).toHaveBeenCalledWith(2);
  });

  it('generates, edits, re-validates and executes SQL, then renders results and chart', async () => {
    vi.mocked(sqlApi.validate).mockResolvedValue(validation('tok-2'));
    vi.mocked(sqlApi.execute).mockResolvedValue(sampleResult);
    renderWithProviders(<AssistantPage />);

    await userEvent.type(screen.getByLabelText(/ask a question/i), 'Show the top 10 customers by sales');
    await waitFor(() => expect(screen.getByRole('button', { name: /generate sql/i })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: /generate sql/i }));

    const editor = await screen.findByLabelText('SQL editor');
    expect(editor).toHaveValue(SQL);
    expect(screen.getByText('Top 10 Customers by Sales')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /execute query/i })).toBeEnabled();

    // Editing invalidates the previous validation.
    await userEvent.type(editor, ' ');
    expect(screen.getByRole('button', { name: /execute query/i })).toBeDisabled();
    expect(screen.getByText(/changed after the last validation/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /validate sql/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /execute query/i })).toBeEnabled());

    await userEvent.click(screen.getByRole('button', { name: /execute query/i }));
    await waitFor(() => expect(sqlApi.execute).toHaveBeenCalledWith(expect.objectContaining({ validationToken: 'tok-2', queryHistoryId: 9 }), expect.any(AbortSignal)));

    const grid = await screen.findByRole('grid');
    expect(within(grid).getByText('Olivia Smith')).toBeInTheDocument();
    expect(within(grid).getByText('NULL')).toBeInTheDocument();
    expect(screen.getByText('3 rows')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('tab', { name: 'Chart' }));
    expect(await screen.findByRole('img', { name: /bar chart of TotalSales by CustomerName/i })).toBeInTheDocument();
  });

  it('shows validation errors and keeps execution disabled', async () => {
    vi.mocked(sqlApi.validate).mockResolvedValue(validation(null, false));
    renderWithProviders(<AssistantPage />);
    await userEvent.type(screen.getByLabelText('SQL editor'), 'DROP TABLE dbo.Orders');
    await userEvent.click(screen.getByRole('button', { name: /validate sql/i }));
    expect(await screen.findByText('DROP TABLE statements are not allowed.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /execute query/i })).toBeDisabled();
  });

  it('shows the clarification question instead of SQL', async () => {
    vi.mocked(aiApi.generateSql).mockResolvedValue({
      success: false, needsClarification: true, clarificationQuestion: 'Which date range should "recent" cover?', sqlQuery: null, queryTitle: 'x', explanation: '',
      tablesUsed: [], columnsUsed: [], parameters: [], warnings: [], validation: null, queryHistoryId: 3, model: 'm', promptVersion: 'v1',
    });
    renderWithProviders(<AssistantPage />);
    await userEvent.type(screen.getByLabelText(/ask a question/i), 'recent orders');
    await waitFor(() => expect(screen.getByRole('button', { name: /generate sql/i })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: /generate sql/i }));
    expect(await screen.findByText(/Which date range/)).toBeInTheDocument();
  });

  it('makes the editor read-only for roles without SQL editing', async () => {
    auth.current = mockAuth(['query.generate', 'query.execute', 'results.export', 'charts.view', 'schema.view']);
    renderWithProviders(<AssistantPage />);
    await userEvent.type(screen.getByLabelText(/ask a question/i), 'Show the top 10 customers by sales');
    await waitFor(() => expect(screen.getByRole('button', { name: /generate sql/i })).toBeEnabled());
    await userEvent.click(screen.getByRole('button', { name: /generate sql/i }));
    expect(await screen.findByLabelText('SQL editor')).toHaveAttribute('readonly');
    expect(screen.getByText(/cannot edit it/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /format sql/i })).not.toBeInTheDocument();
  });
});
