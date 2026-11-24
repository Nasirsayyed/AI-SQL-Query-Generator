import { act, renderHook } from '@testing-library/react';
import { useQueryWorkbench } from './useQueryWorkbench';
import { aiApi, sqlApi } from '../../api/endpoints';
import type { GenerateSqlResponse, SqlValidationResponse } from '../../api/types';
import { sampleResult } from '../../test/utils';

vi.mock('../../api/endpoints', () => ({
  aiApi: { generateSql: vi.fn(), suggestChart: vi.fn(), explainSql: vi.fn(), optimizeSql: vi.fn() },
  sqlApi: { validate: vi.fn(), execute: vi.fn(), format: vi.fn(), explainPlan: vi.fn() },
}));

const SQL = 'SELECT TOP (10) c.CustomerName FROM dbo.Customers c';
const valid = (token: string): SqlValidationResponse => ({
  isValid: true, message: 'ok', errors: [], warnings: [], referencedObjects: ['dbo.Customers'], referencedParameters: [], validationToken: token, validatedAt: '',
});
const generation: GenerateSqlResponse = {
  success: true, needsClarification: false, sqlQuery: SQL, queryTitle: 'Top customers', explanation: 'x', tablesUsed: [], columnsUsed: [],
  parameters: [], warnings: [], chartSuggestion: null, validation: valid('tok-1'), queryHistoryId: 5, model: 'm', promptVersion: 'sql-generation.v1',
};

describe('useQueryWorkbench', () => {
  beforeEach(() => vi.clearAllMocks());

  it('allows executing generated SQL with its validation token', async () => {
    vi.mocked(aiApi.generateSql).mockResolvedValue(generation);
    vi.mocked(sqlApi.execute).mockResolvedValue(sampleResult);
    vi.mocked(aiApi.suggestChart).mockResolvedValue({ recommended: false, source: 'heuristic' });
    const { result } = renderHook(() => useQueryWorkbench(1));

    act(() => result.current.setQuestion('top customers'));
    await act(() => result.current.generate());
    expect(result.current.sql).toBe(SQL);
    expect(result.current.canExecute).toBe(true);

    await act(() => result.current.execute());
    expect(sqlApi.execute).toHaveBeenCalledWith(expect.objectContaining({ sql: SQL, validationToken: 'tok-1', queryHistoryId: 5 }), expect.any(AbortSignal));
    expect(result.current.result?.rowCount).toBe(3);
  });

  it('invalidates validation when the SQL or parameters change', async () => {
    vi.mocked(aiApi.generateSql).mockResolvedValue(generation);
    vi.mocked(sqlApi.validate).mockResolvedValue(valid('tok-2'));
    const { result } = renderHook(() => useQueryWorkbench(1));
    act(() => result.current.setQuestion('top customers'));
    await act(() => result.current.generate());

    act(() => result.current.setSql(SQL + ' ORDER BY c.CustomerName'));
    expect(result.current.canExecute).toBe(false);
    expect(result.current.validationIsCurrent).toBe(false);
    await act(() => result.current.execute());
    expect(sqlApi.execute).not.toHaveBeenCalled();

    await act(() => result.current.validate());
    expect(result.current.canExecute).toBe(true);

    act(() => result.current.setParameters([{ name: 'X', type: 'Int', value: '1' }]));
    expect(result.current.canExecute).toBe(false);
  });

  it('does not allow execution after failed validation', async () => {
    vi.mocked(sqlApi.validate).mockResolvedValue({ ...valid(''), isValid: false, validationToken: null, errors: [{ code: 'STATEMENT_NOT_ALLOWED', message: 'no' }] });
    const { result } = renderHook(() => useQueryWorkbench(1));
    act(() => result.current.setSql('DROP TABLE dbo.Orders'));
    await act(() => result.current.validate());
    expect(result.current.validation?.isValid).toBe(false);
    expect(result.current.canExecute).toBe(false);
  });

  it('surfaces API errors without throwing', async () => {
    vi.mocked(aiApi.generateSql).mockRejectedValue(new Error('AI unavailable'));
    const { result } = renderHook(() => useQueryWorkbench(1));
    act(() => result.current.setQuestion('x y z'));
    await act(() => result.current.generate());
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.busy).toBeNull();
  });
});
