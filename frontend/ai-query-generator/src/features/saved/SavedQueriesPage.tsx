import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box, Button, Card, CardActions, CardContent, Chip, Dialog, DialogContent, DialogTitle, Grid, IconButton, MenuItem, Pagination, Stack, Tab, Tabs, TextField, Tooltip, Typography,
} from '@mui/material';
import { ExternalLink, Play, Share2, Star, Trash2 } from 'lucide-react';
import { savedQueriesApi } from '../../api/endpoints';
import type { QueryResult, SavedQuery } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { PageHeader } from '../../components/PageHeader';
import { EmptyState } from '../../components/EmptyState';
import { ErrorAlert } from '../../components/ErrorAlert';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { ResultsGrid } from '../../components/ResultsGrid';
import { ResultChart } from '../../components/ResultChart';
import { ExecutionInfo } from '../../components/ExecutionInfo';
import { useNotify } from '../../components/Notifier';
import { fromNow } from '../../utils/format';
import type { ChartSuggestion } from '../../api/types';

type Scope = 'all' | 'mine' | 'shared' | 'favorites';

function chartFromSaved(json?: string | null): ChartSuggestion | null {
  if (!json) return null;
  try {
    const c = JSON.parse(json) as { chartType?: string; xAxis?: string; yAxis?: string; aggregation?: string };
    return { recommended: true, chartType: c.chartType as ChartSuggestion['chartType'], xAxis: c.xAxis, yAxis: c.yAxis, aggregation: c.aggregation as ChartSuggestion['aggregation'], source: 'saved' };
  } catch {
    return null;
  }
}

export function SavedQueriesPage() {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const notify = useNotify();
  const queryClient = useQueryClient();
  const [scope, setScope] = useState<Scope>('all');
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [page, setPage] = useState(1);
  const [toDelete, setToDelete] = useState<SavedQuery | null>(null);
  const [running, setRunning] = useState<{ query: SavedQuery; result: QueryResult } | null>(null);
  const [resultTab, setResultTab] = useState<'results' | 'chart'>('results');

  const list = useQuery({
    queryKey: ['saved', scope, search, category, page],
    queryFn: () => savedQueriesApi.list({
      page, pageSize: 12, search: search || undefined, category: category || undefined,
      scope: scope === 'favorites' ? 'all' : scope, favoritesOnly: scope === 'favorites',
    }),
    placeholderData: (prev) => prev,
  });
  const categories = useQuery({ queryKey: ['saved', 'categories'], queryFn: savedQueriesApi.categories });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['saved'] });
  const favorite = useMutation({ mutationFn: (id: number) => savedQueriesApi.toggleFavorite(id), onSuccess: () => void invalidate() });
  const remove = useMutation({ mutationFn: (id: number) => savedQueriesApi.remove(id), onSuccess: () => { setToDelete(null); notify('Saved query deleted.'); void invalidate(); } });
  const run = useMutation({
    mutationFn: (q: SavedQuery) => savedQueriesApi.run(q.savedQueryId).then((result) => ({ query: q, result })),
    onSuccess: (r) => { setResultTab('results'); setRunning(r); },
  });

  const canUseAssistant = hasPermission(Permissions.QueryGenerate);

  return (
    <Box>
      <PageHeader title="Saved Queries" subtitle="Reusable queries and shared reports. Every run is re-validated against current permissions." />
      <ErrorAlert error={list.error ?? run.error ?? remove.error} />
      <Card sx={{ mb: 2 }}>
        <Tabs value={scope} onChange={(_, v) => { setScope(v); setPage(1); }} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }} variant="scrollable">
          <Tab value="all" label="All" />
          {hasPermission(Permissions.SavedQueriesManage) && <Tab value="mine" label="My queries" />}
          <Tab value="shared" label="Shared with me" />
          <Tab value="favorites" label="Favourites" />
        </Tabs>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ p: 2 }}>
          <TextField label="Search" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} sx={{ flex: 1 }} />
          <TextField select label="Category" value={category} onChange={(e) => { setCategory(e.target.value); setPage(1); }} sx={{ minWidth: 200 }}>
            <MenuItem value="">All categories</MenuItem>
            {(categories.data ?? []).map((c) => <MenuItem key={c} value={c}>{c}</MenuItem>)}
          </TextField>
        </Stack>
      </Card>

      {list.data && list.data.items.length === 0 && (
        <EmptyState title="No saved queries" description={canUseAssistant ? 'Save a query from the AI SQL Assistant to reuse it later.' : 'No reports have been shared with you yet.'} />
      )}

      <Grid container spacing={2}>
        {(list.data?.items ?? []).map((q) => (
          <Grid key={q.savedQueryId} size={{ xs: 12, md: 6, xl: 4 }}>
            <Card sx={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
              <CardContent sx={{ flex: 1 }}>
                <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 1 }}>
                  <Typography variant="h6" sx={{ flex: 1 }}>{q.queryTitle}</Typography>
                  <Tooltip title={q.isFavorite ? 'Remove favourite' : 'Add favourite'}>
                    <IconButton size="small" onClick={() => favorite.mutate(q.savedQueryId)} aria-label="Toggle favourite" color={q.isFavorite ? 'warning' : 'default'}>
                      <Star size={18} fill={q.isFavorite ? 'currentColor' : 'none'} />
                    </IconButton>
                  </Tooltip>
                </Stack>
                {q.description && <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{q.description}</Typography>}
                <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.5, mb: 1 }}>
                  {q.category && <Chip size="small" label={q.category} />}
                  {q.isShared && <Chip size="small" icon={<Share2 size={12} />} label="Shared" variant="outlined" />}
                  <Chip size="small" label={q.connectionName} variant="outlined" />
                </Stack>
                <Box component="pre" sx={{ m: 0, p: 1, bgcolor: 'action.hover', borderRadius: 1, fontSize: 12, maxHeight: 110, overflow: 'hidden', whiteSpace: 'pre-wrap' }}>{q.sqlQuery}</Box>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                  {q.isOwner ? 'You' : q.ownerName} · updated {fromNow(q.updatedAt ?? q.createdAt)}
                </Typography>
              </CardContent>
              <CardActions sx={{ px: 2, pb: 2 }}>
                <Button size="small" variant="contained" startIcon={<Play size={15} />} onClick={() => run.mutate(q)} disabled={run.isPending}>Run</Button>
                {canUseAssistant && (
                  <Button size="small" startIcon={<ExternalLink size={15} />} onClick={() => navigate('/assistant', { state: { seed: {
                    sql: q.sqlQuery, parameters: q.parameters, title: q.queryTitle, question: q.userQuestion ?? '', chartSuggestion: chartFromSaved(q.chartConfigJson), connectionId: q.connectionId,
                  } } })}>Open</Button>
                )}
                <Box sx={{ flex: 1 }} />
                {q.isOwner && (
                  <Tooltip title="Delete"><IconButton size="small" onClick={() => setToDelete(q)} aria-label="Delete saved query"><Trash2 size={16} /></IconButton></Tooltip>
                )}
              </CardActions>
            </Card>
          </Grid>
        ))}
      </Grid>

      {list.data && list.data.totalPages > 1 && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
          <Pagination count={list.data.totalPages} page={page} onChange={(_, p) => setPage(p)} />
        </Box>
      )}

      <Dialog open={!!running} onClose={() => setRunning(null)} fullWidth maxWidth="lg">
        <DialogTitle>{running?.query.queryTitle}</DialogTitle>
        <DialogContent>
          {running && (
            <>
              <ExecutionInfo result={running.result} />
              {hasPermission(Permissions.ChartsView) && (
                <Tabs value={resultTab} onChange={(_, v) => setResultTab(v)} sx={{ mb: 1 }}>
                  <Tab value="results" label="Results" />
                  <Tab value="chart" label="Chart" />
                </Tabs>
              )}
              {resultTab === 'results' ? (
                <ResultsGrid result={running.result} title={running.query.queryTitle} canExport={hasPermission(Permissions.ResultsExport)} />
              ) : (
                <ResultChart result={running.result} suggestion={chartFromSaved(running.query.chartConfigJson)} title={running.query.queryTitle} />
              )}
            </>
          )}
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={!!toDelete}
        title="Delete saved query?"
        message={`"${toDelete?.queryTitle}" will be permanently deleted${toDelete?.isShared ? ' for everyone it is shared with' : ''}.`}
        confirmLabel="Delete"
        destructive
        busy={remove.isPending}
        onConfirm={() => toDelete && remove.mutate(toDelete.savedQueryId)}
        onClose={() => setToDelete(null)}
      />
    </Box>
  );
}
