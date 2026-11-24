import { useCallback, useMemo, useState } from 'react';
import { aiApi, sqlApi } from '../../api/endpoints';
import type {
  ChartSuggestion, ExecutionPlanSummary, ExplainSqlResponse, GenerateSqlResponse, OptimizeSqlResponse, QueryParameter, QueryResult, SqlValidationResponse,
} from '../../api/types';

export const parameterSignature = (parameters: QueryParameter[]) =>
  JSON.stringify([...parameters].map((p) => [p.name.replace(/^@/, '').toLowerCase(), p.type, p.value]).sort());

interface ValidationState {
  response: SqlValidationResponse;
  sql: string;
  signature: string;
}

export interface WorkbenchSeed {
  sql: string;
  parameters?: QueryParameter[];
  title?: string;
  question?: string;
  historyId?: number | null;
  chartSuggestion?: ChartSuggestion | null;
}

/**
 * State machine for generate → edit → validate → execute. Any edit to the SQL or parameters invalidates the previous
 * validation; execution is only possible with a token for the exact current SQL and parameters.
 */
export function useQueryWorkbench(connectionId: number | null) {
  const [question, setQuestion] = useState('');
  const [generation, setGeneration] = useState<GenerateSqlResponse | null>(null);
  const [sql, setSqlState] = useState('');
  const [originalSql, setOriginalSql] = useState('');
  const [parameters, setParametersState] = useState<QueryParameter[]>([]);
  const [title, setTitle] = useState('');
  const [historyId, setHistoryId] = useState<number | null>(null);
  const [validation, setValidation] = useState<ValidationState | null>(null);
  const [result, setResult] = useState<QueryResult | null>(null);
  const [chartSuggestion, setChartSuggestion] = useState<ChartSuggestion | null>(null);
  const [explanation, setExplanation] = useState<ExplainSqlResponse | null>(null);
  const [optimization, setOptimization] = useState<OptimizeSqlResponse | null>(null);
  const [plan, setPlan] = useState<ExecutionPlanSummary | null>(null);
  const [busy, setBusy] = useState<null | 'generate' | 'validate' | 'execute' | 'format' | 'explain' | 'optimize' | 'plan'>(null);
  const [error, setError] = useState<unknown>(null);
  const [controller, setController] = useState<AbortController | null>(null);
  const [pendingFormat, setPendingFormat] = useState<{ sql: string; commentsRemoved: number; original: string } | null>(null);

  const signature = useMemo(() => parameterSignature(parameters), [parameters]);
  const validationIsCurrent = !!validation && validation.sql === sql && validation.signature === signature;
  const canExecute = validationIsCurrent && validation!.response.isValid && !!validation!.response.validationToken && !!connectionId;

  const setSql = useCallback((value: string) => {
    setSqlState(value);
    setExplanation(null);
    setOptimization(null);
    setPlan(null);
  }, []);

  const setParameters = useCallback((value: QueryParameter[]) => setParametersState(value), []);

  const run = useCallback(async <T,>(kind: NonNullable<typeof busy>, action: () => Promise<T>): Promise<T | undefined> => {
    setBusy(kind);
    setError(null);
    try {
      return await action();
    } catch (e) {
      setError(e);
      return undefined;
    } finally {
      setBusy(null);
    }
  }, []);

  const load = useCallback((seed: WorkbenchSeed) => {
    setGeneration(null);
    setSqlState(seed.sql);
    setOriginalSql(seed.sql);
    setParametersState(seed.parameters ?? []);
    setTitle(seed.title ?? 'Query');
    setQuestion(seed.question ?? '');
    setHistoryId(seed.historyId ?? null);
    setChartSuggestion(seed.chartSuggestion ?? null);
    setValidation(null);
    setResult(null);
    setExplanation(null);
    setOptimization(null);
    setPlan(null);
    setError(null);
  }, []);

  const generate = useCallback(async (schemaName?: string | null) => {
    if (!connectionId || !question.trim()) return;
    const ac = new AbortController();
    setController(ac);
    const response = await run('generate', () => aiApi.generateSql(connectionId, question.trim(), schemaName, ac.signal));
    setController(null);
    if (!response) return;
    setGeneration(response);
    setResult(null);
    setExplanation(null);
    setOptimization(null);
    setPlan(null);
    setHistoryId(response.queryHistoryId ?? null);
    setTitle(response.queryTitle);
    setChartSuggestion(response.chartSuggestion ?? null);
    if (response.sqlQuery) {
      setSqlState(response.sqlQuery);
      setOriginalSql(response.sqlQuery);
      setParametersState(response.parameters);
      setValidation(response.validation ? { response: response.validation, sql: response.sqlQuery, signature: parameterSignature(response.parameters) } : null);
    }
  }, [connectionId, question, run]);

  const validate = useCallback(async () => {
    if (!connectionId || !sql.trim()) return;
    const currentSql = sql;
    const currentSignature = signature;
    const response = await run('validate', () => sqlApi.validate(connectionId, currentSql, parameters));
    if (response) setValidation({ response, sql: currentSql, signature: currentSignature });
  }, [connectionId, sql, signature, parameters, run]);

  const execute = useCallback(async () => {
    if (!canExecute || !connectionId) return;
    const ac = new AbortController();
    setController(ac);
    const response = await run('execute', () =>
      sqlApi.execute({ connectionId, sql, parameters, validationToken: validation!.response.validationToken, queryHistoryId: historyId, queryTitle: title }, ac.signal));
    setController(null);
    if (!response) return;
    setResult(response);
    if (response.queryHistoryId) setHistoryId(response.queryHistoryId);
    if (!chartSuggestion && response.rowCount > 1) {
      // Only column metadata is sent for the recommendation — never row data.
      aiApi.suggestChart(question || title, response.columns, response.rowCount).then(setChartSuggestion).catch(() => undefined);
    }
  }, [canExecute, connectionId, sql, parameters, validation, historyId, title, chartSuggestion, question, run]);

  const cancel = useCallback(() => controller?.abort(), [controller]);

  /**
   * Formats via the backend. Formatting regenerates SQL from its syntax tree, which drops comments, so when the SQL has
   * comments the result is held in `pendingFormat` until the user confirms or discards it.
   */
  const format = useCallback(async () => {
    const original = sql;
    const response = await run('format', () => sqlApi.format(original));
    if (!response) return;
    if (response.commentsRemoved > 0) setPendingFormat({ sql: response.sql, commentsRemoved: response.commentsRemoved, original });
    else setSql(response.sql);
  }, [sql, run, setSql]);

  const confirmFormat = useCallback(() => {
    // Ignore the pending result if the SQL changed while the dialog was open.
    if (pendingFormat && pendingFormat.original === sql) setSql(pendingFormat.sql);
    setPendingFormat(null);
  }, [pendingFormat, sql, setSql]);

  const discardFormat = useCallback(() => setPendingFormat(null), []);

  const explain = useCallback(async () => {
    if (!connectionId) return;
    const response = await run('explain', () => aiApi.explainSql(connectionId, sql, true));
    if (response) setExplanation(response);
  }, [connectionId, sql, run]);

  const optimize = useCallback(async () => {
    if (!connectionId) return;
    const response = await run('optimize', () => aiApi.optimizeSql(connectionId, sql));
    if (response) setOptimization(response);
  }, [connectionId, sql, run]);

  const explainPlan = useCallback(async () => {
    if (!connectionId) return;
    const response = await run('plan', () => sqlApi.explainPlan(connectionId, sql, parameters));
    if (response) setPlan(response.summary);
  }, [connectionId, sql, parameters, run]);

  const reset = useCallback(() => setSql(originalSql), [originalSql, setSql]);

  return {
    question, setQuestion, generation, sql, setSql, originalSql, parameters, setParameters, title, setTitle, historyId,
    validation: validation?.response ?? null, validationIsCurrent, canExecute, result, chartSuggestion, explanation, optimization, plan,
    busy, error, setError, generate, validate, execute, cancel, format, pendingFormat, confirmFormat, discardFormat, explain, optimize, explainPlan, reset, load,
  };
}
