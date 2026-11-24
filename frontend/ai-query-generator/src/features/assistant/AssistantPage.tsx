import { lazy, Suspense, useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  Alert, Box, Button, ButtonGroup, Card, CardContent, Chip, CircularProgress, Divider, IconButton, LinearProgress, MenuItem, Skeleton, Stack, Tab, Tabs, TextField, Tooltip, Typography,
} from '@mui/material';
import {
  BookOpen, Check, CircleStop, Copy, Download, Gauge, Maximize2, Minimize2, Play, RotateCcw, Save, ShieldCheck, Sparkles, Wand2, Zap,
} from 'lucide-react';
import { aiApi, savedQueriesApi, schemaApi } from '../../api/endpoints';
import { useActiveConnection } from '../../context/ActiveConnectionContext';
import { useAuth } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { PageHeader } from '../../components/PageHeader';
import { ErrorAlert } from '../../components/ErrorAlert';
import { EmptyState } from '../../components/EmptyState';
import { ValidationPanel } from '../../components/ValidationPanel';
import { ParametersEditor } from '../../components/ParametersEditor';
import { ResultsGrid } from '../../components/ResultsGrid';
import { ResultChart } from '../../components/ResultChart';
import { ExecutionInfo } from '../../components/ExecutionInfo';
import { SaveQueryDialog, type SaveQueryValues } from '../../components/SaveQueryDialog';
import { useNotify } from '../../components/Notifier';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { downloadBlob, safeFileName } from '../../utils/export';
import { useQueryWorkbench, type WorkbenchSeed } from './useQueryWorkbench';
import { ExplanationPanel, OptimizationPanel, PlanPanel } from './AnalysisPanels';
import { ResizeHandle } from '../../components/ResizeHandle';
import type { ChartConfig } from '../../utils/chart';
import { EXAMPLE_QUESTIONS } from './exampleQuestions';

const SqlEditor = lazy(() => import('../../components/SqlEditor'));

const EDITOR_HEIGHT_KEY = 'aiqg.editorHeight';
const EDITOR_DEFAULT_HEIGHT = 280;
const EDITOR_EXPANDED_HEIGHT = 560;
const EDITOR_MIN_HEIGHT = 140;
const EDITOR_MAX_HEIGHT = 1200;

/** Remembered editor height (per browser); falls back to the default when storage is unavailable. */
function readEditorHeight(): number {
  try {
    const v = Number(localStorage.getItem(EDITOR_HEIGHT_KEY));
    return Number.isFinite(v) && v >= EDITOR_MIN_HEIGHT && v <= EDITOR_MAX_HEIGHT ? v : EDITOR_DEFAULT_HEIGHT;
  } catch {
    return EDITOR_DEFAULT_HEIGHT;
  }
}



type ResultTab = 'results' | 'chart' | 'explanation' | 'optimize' | 'plan';

export function AssistantPage() {
  const { activeConnection, connections, setActiveConnectionId } = useActiveConnection();
  const { hasPermission } = useAuth();
  const notify = useNotify();
  const location = useLocation();
  const navigate = useNavigate();
  const connectionId = activeConnection?.connectionId ?? null;
  const wb = useQueryWorkbench(connectionId);
  const [schemaName, setSchemaName] = useState('');
  const [tab, setTab] = useState<ResultTab>('results');
  const [editorHeight, setEditorHeight] = useState(readEditorHeight);
  const [saveOpen, setSaveOpen] = useState(false);
  const [chartConfig, setChartConfig] = useState<ChartConfig | null>(null);

  const expanded = editorHeight >= EDITOR_EXPANDED_HEIGHT;
  const resizeEditor = (height: number) => {
    setEditorHeight(height);
    try {
      localStorage.setItem(EDITOR_HEIGHT_KEY, String(height));
    } catch {
      /* storage unavailable */
    }
  };

  const canEdit = hasPermission(Permissions.QueryEditSql);
  const canExecute = hasPermission(Permissions.QueryExecute);
  const canExplain = hasPermission(Permissions.QueryExplain);
  const canSave = hasPermission(Permissions.SavedQueriesManage);
  const canExport = hasPermission(Permissions.ResultsExport);
  const canChart = hasPermission(Permissions.ChartsView);

  const { data: aiStatus } = useQuery({ queryKey: ['ai', 'status'], queryFn: aiApi.status, staleTime: 300_000 });
  const { data: overview } = useQuery({ queryKey: ['schema', connectionId], queryFn: () => schemaApi.overview(connectionId!), enabled: !!connectionId && hasPermission(Permissions.SchemaView) });
  const { data: completions = [] } = useQuery({ queryKey: ['completions', connectionId], queryFn: () => schemaApi.completions(connectionId!), enabled: !!connectionId && hasPermission(Permissions.SchemaView), staleTime: 300_000 });

  // Load SQL handed over from history or saved queries.
  useEffect(() => {
    const seed = (location.state as { seed?: WorkbenchSeed & { connectionId?: number } } | null)?.seed;
    if (seed) {
      if (seed.connectionId && seed.connectionId !== connectionId) setActiveConnectionId(seed.connectionId);
      wb.load(seed);
      navigate(location.pathname, { replace: true, state: null });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state]);

  useEffect(() => {
    if (wb.result) setTab('results');
  }, [wb.result]);

  const save = useMutation({
    mutationFn: (v: SaveQueryValues) => savedQueriesApi.create({
      connectionId: connectionId!, queryTitle: v.queryTitle, description: v.description || null, category: v.category || null, isShared: v.isShared,
      sqlQuery: wb.sql, parameters: wb.parameters, userQuestion: wb.question || null, chartConfigJson: chartConfig ? JSON.stringify(chartConfig) : null,
      sourceQueryHistoryId: wb.historyId,
    }),
    onSuccess: () => { setSaveOpen(false); notify('Query saved.'); },
  });

  if (connections.length === 0)
    return (
      <>
        <PageHeader title="AI SQL Assistant" />
        <EmptyState title="No active database connection" description="Ask an administrator to configure and activate a database connection and grant your role access to it." />
      </>
    );

  const hasSql = wb.sql.trim().length > 0;
  const generating = wb.busy === 'generate';

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(wb.sql);
      notify('SQL copied to clipboard.');
    } catch {
      notify('Copy failed.', 'error');
    }
  };

  return (
    <Box>
      <PageHeader
        title="AI SQL Assistant"
        subtitle="Ask a question in plain language. Generated SQL is validated by the server and never runs until you execute it."
        actions={
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Chip
              icon={<Sparkles size={14} />}
              label={aiStatus?.configured ? `${aiStatus.model}` : 'AI not configured'}
              color={aiStatus?.configured ? 'secondary' : 'warning'}
              variant="outlined"
            />
          </Stack>
        }
      />

      <Card sx={{ mb: 2 }}>
        <CardContent>
          <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5} sx={{ mb: 2 }}>
            <TextField select label="Database" value={connectionId ?? ''} onChange={(e) => setActiveConnectionId(Number(e.target.value))} sx={{ minWidth: 240 }}>
              {connections.map((c) => <MenuItem key={c.connectionId} value={c.connectionId}>{c.connectionName} ({c.databaseName})</MenuItem>)}
            </TextField>
            <TextField select label="Schema" value={schemaName} onChange={(e) => setSchemaName(e.target.value)} sx={{ minWidth: 180 }}>
              <MenuItem value="">All permitted schemas</MenuItem>
              {overview?.schemas.map((s) => <MenuItem key={s.schemaName} value={s.schemaName}>{s.schemaName}</MenuItem>)}
            </TextField>
          </Stack>

          <Box component="form" onSubmit={(e) => { e.preventDefault(); void wb.generate(schemaName); }}>
            <TextField
              fullWidth
              multiline
              minRows={2}
              maxRows={6}
              label="Ask a question about your data"
              placeholder="e.g. Show the top 10 customers by total sales this year"
              value={wb.question}
              onChange={(e) => wb.setQuestion(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); void wb.generate(schemaName); }
              }}
              slotProps={{ htmlInput: { maxLength: 2000 } }}
            />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ mt: 1.5, alignItems: { sm: 'center' } }}>
              <Button type="submit" variant="contained" startIcon={generating ? <CircularProgress size={16} color="inherit" /> : <Wand2 size={17} />} disabled={!wb.question.trim() || generating || !aiStatus?.configured}>
                Generate SQL
              </Button>
              {generating && <Button startIcon={<CircleStop size={16} />} onClick={wb.cancel}>Cancel</Button>}
              <Typography variant="caption" color="text.secondary">Ctrl + Enter to generate</Typography>
            </Stack>
          </Box>

          <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1, mt: 2 }}>
            {EXAMPLE_QUESTIONS.map((q) => (
              <Chip key={q} label={q} size="small" variant="outlined" onClick={() => wb.setQuestion(q)} clickable />
            ))}
          </Stack>
        </CardContent>
        {generating && <LinearProgress />}
      </Card>

      <ErrorAlert error={wb.error} onClose={() => wb.setError(null)} />

      {wb.generation?.needsClarification && (
        <Alert severity="info" sx={{ mb: 2 }}>
          <strong>The assistant needs more information:</strong> {wb.generation.clarificationQuestion}
        </Alert>
      )}

      {(hasSql || canEdit) && (
        <Card sx={{ mb: 0.5 }}>
          <CardContent>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ mb: 1.5, alignItems: { md: 'center' } }}>
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography variant="h6">{wb.title || 'SQL editor'}</Typography>
                {wb.generation?.explanation && <Typography variant="body2" color="text.secondary">{wb.generation.explanation}</Typography>}
              </Box>
              <Stack direction="row" spacing={0.5}>
                <Tooltip title="Copy SQL"><span><IconButton onClick={() => void copy()} disabled={!hasSql} aria-label="Copy SQL"><Copy size={17} /></IconButton></span></Tooltip>
                <Tooltip title="Download .sql"><span><IconButton onClick={() => downloadBlob(new Blob([wb.sql], { type: 'text/plain' }), safeFileName(wb.title || 'query', 'sql'))} disabled={!hasSql} aria-label="Download SQL"><Download size={17} /></IconButton></span></Tooltip>
                {canEdit && <Tooltip title="Reset to generated SQL"><span><IconButton onClick={wb.reset} disabled={!wb.originalSql || wb.sql === wb.originalSql} aria-label="Reset SQL"><RotateCcw size={17} /></IconButton></span></Tooltip>}
                <Tooltip title={expanded ? 'Shrink editor' : 'Expand editor'}><IconButton onClick={() => resizeEditor(expanded ? EDITOR_DEFAULT_HEIGHT : EDITOR_EXPANDED_HEIGHT)} aria-label="Toggle editor size">{expanded ? <Minimize2 size={17} /> : <Maximize2 size={17} />}</IconButton></Tooltip>
              </Stack>
            </Stack>

            {wb.generation && wb.generation.warnings.length > 0 && (
              <Alert severity="warning" sx={{ mb: 1.5 }}>{wb.generation.warnings.map((w, i) => <div key={i}>{w}</div>)}</Alert>
            )}
            {!canEdit && hasSql && <Alert severity="info" sx={{ mb: 1.5 }}>Your role can run AI-generated SQL but cannot edit it.</Alert>}

            <Suspense fallback={<Skeleton variant="rectangular" height={editorHeight} />}>
              <SqlEditor value={wb.sql} onChange={wb.setSql} readOnly={!canEdit} height={editorHeight} completions={completions} onExecuteShortcut={() => void wb.execute()} />
            </Suspense>

            <Box sx={{ mt: 2 }}>
              <ParametersEditor parameters={wb.parameters} onChange={wb.setParameters} readOnly={!canEdit} />
            </Box>

            <Divider sx={{ my: 2 }} />

            <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
              <ButtonGroup variant="outlined">
                <Button startIcon={wb.busy === 'validate' ? <CircularProgress size={14} /> : <ShieldCheck size={17} />} onClick={() => void wb.validate()} disabled={!hasSql || !!wb.busy}>
                  Validate SQL
                </Button>
                {canEdit && <Button startIcon={<BookOpen size={17} />} onClick={() => void wb.format()} disabled={!hasSql || !!wb.busy}>Format SQL</Button>}
              </ButtonGroup>
              {canExecute && (
                <Tooltip title={wb.canExecute ? 'Ctrl + Enter' : 'Validate the current SQL before executing'}>
                  <span>
                    <Button variant="contained" color="success" startIcon={wb.busy === 'execute' ? <CircularProgress size={14} color="inherit" /> : <Play size={17} />} onClick={() => void wb.execute()} disabled={!wb.canExecute || !!wb.busy}>
                      Execute Query
                    </Button>
                  </span>
                </Tooltip>
              )}
              {wb.busy === 'execute' && <Button startIcon={<CircleStop size={16} />} onClick={wb.cancel}>Cancel</Button>}
              {canExplain && <Button startIcon={<Sparkles size={17} />} onClick={() => { void wb.explain(); setTab('explanation'); }} disabled={!hasSql || !!wb.busy}>Explain Query</Button>}
              {canExplain && <Button startIcon={<Zap size={17} />} onClick={() => { void wb.optimize(); setTab('optimize'); }} disabled={!hasSql || !!wb.busy}>Optimise</Button>}
              {canEdit && <Button startIcon={<Gauge size={17} />} onClick={() => { void wb.explainPlan(); setTab('plan'); }} disabled={!hasSql || !!wb.busy}>Execution plan</Button>}
              {canSave && <Button startIcon={<Save size={17} />} onClick={() => setSaveOpen(true)} disabled={!hasSql || !wb.validationIsCurrent || !wb.validation?.isValid}>Save Query</Button>}
              {wb.canExecute && <Chip icon={<Check size={14} />} color="success" label="Ready to execute" variant="outlined" sx={{ alignSelf: 'center' }} />}
            </Stack>

            <ValidationPanel validation={wb.validation} stale={!!wb.validation && !wb.validationIsCurrent} />
          </CardContent>
        </Card>
      )}

      {(hasSql || canEdit) && (
        <ResizeHandle
          value={editorHeight}
          min={EDITOR_MIN_HEIGHT}
          max={EDITOR_MAX_HEIGHT}
          onChange={resizeEditor}
          onReset={() => resizeEditor(EDITOR_DEFAULT_HEIGHT)}
          ariaLabel="Resize SQL editor and results"
        />
      )}

      {(wb.result || wb.explanation || wb.optimization || wb.plan || ['explain', 'optimize', 'plan'].includes(wb.busy ?? '')) && (
        <Card>
          <Tabs value={tab} onChange={(_, v) => setTab(v)} variant="scrollable" sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
            <Tab value="results" label="Results" disabled={!wb.result} />
            {canChart && <Tab value="chart" label="Chart" disabled={!wb.result} />}
            <Tab value="explanation" label="SQL explanation" />
            <Tab value="optimize" label="Optimisation" />
            {canEdit && <Tab value="plan" label="Execution plan" />}
          </Tabs>
          <CardContent>
            {tab === 'results' && wb.result && (
              <>
                <ExecutionInfo result={wb.result} />
                <ResultsGrid result={wb.result} title={wb.title} canExport={canExport} />
              </>
            )}
            {tab === 'chart' && wb.result && <ResultChart result={wb.result} suggestion={wb.chartSuggestion} title={wb.title} onConfigChange={setChartConfig} />}
            {tab === 'explanation' && (wb.busy === 'explain' ? <Skeleton height={160} /> : wb.explanation ? <ExplanationPanel explanation={wb.explanation} /> : <EmptyState title="No explanation yet" description="Click Explain Query to get a step-by-step explanation." />)}
            {tab === 'optimize' && (wb.busy === 'optimize' ? <Skeleton height={160} /> : wb.optimization ? <OptimizationPanel optimization={wb.optimization} onUse={canEdit ? wb.setSql : undefined} /> : <EmptyState title="No suggestions yet" description="Click Optimise for performance suggestions." />)}
            {tab === 'plan' && (wb.busy === 'plan' ? <Skeleton height={160} /> : wb.plan ? <PlanPanel plan={wb.plan} /> : <EmptyState title="No plan yet" description="Click Execution plan for SQL Server's estimated plan (the query is not run)." />)}
          </CardContent>
        </Card>
      )}

      <ConfirmDialog
        open={!!wb.pendingFormat}
        title="Formatting removes comments"
        message={`The formatter rebuilds the SQL from its syntax tree and will remove ${wb.pendingFormat?.commentsRemoved ?? 0} comment${wb.pendingFormat?.commentsRemoved === 1 ? '' : 's'}. Format anyway?`}
        confirmLabel="Format and remove comments"
        onConfirm={wb.confirmFormat}
        onClose={wb.discardFormat}
      />

      <SaveQueryDialog
        open={saveOpen}
        initialTitle={wb.title}
        canShare={hasPermission(Permissions.SavedQueriesShare)}
        onClose={() => setSaveOpen(false)}
        onSave={async (v) => { await save.mutateAsync(v).catch(() => undefined); }}
        error={save.error}
      />
    </Box>
  );
}
