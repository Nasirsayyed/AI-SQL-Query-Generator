import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Box, Button, Card, Dialog, DialogContent, DialogTitle, Drawer, IconButton, MenuItem, Stack, TextField, Tooltip, Typography,
} from '@mui/material';
import { DataGrid, type GridColDef, type GridPaginationModel, type GridSortModel } from '@mui/x-data-grid';
import { Copy, ExternalLink, Play, Trash2, X } from 'lucide-react';
import { historyApi } from '../../api/endpoints';
import type { QueryHistoryItem, QueryResult } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { PageHeader } from '../../components/PageHeader';
import { StatusChip } from '../../components/StatusChip';
import { ErrorAlert } from '../../components/ErrorAlert';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { ResultsGrid } from '../../components/ResultsGrid';
import { ExecutionInfo } from '../../components/ExecutionInfo';
import { useNotify } from '../../components/Notifier';
import { formatDateTime, formatDuration, formatNumber } from '../../utils/format';

const STATUSES = ['Generated', 'Succeeded', 'Failed', 'TimedOut', 'ValidationFailed', 'ClarificationRequested', 'Cancelled'];

export function HistoryPage() {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const notify = useNotify();
  const queryClient = useQueryClient();
  const [params] = useSearchParams();
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 });
  const [sort, setSort] = useState<GridSortModel>([]);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState(params.get('status') ?? '');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [userId, setUserId] = useState<number | ''>('');
  const [selected, setSelected] = useState<number | null>(null);
  const [toDelete, setToDelete] = useState<QueryHistoryItem | null>(null);
  const [rerunResult, setRerunResult] = useState<{ title: string; result: QueryResult } | null>(null);

  const viewAll = hasPermission(Permissions.HistoryViewAll);
  const list = useQuery({
    queryKey: ['history', pagination, sort, search, status, from, to, userId],
    queryFn: () => historyApi.list({
      page: pagination.page + 1, pageSize: pagination.pageSize, search: search || undefined, status: status || undefined,
      from: from || undefined, to: to || undefined, userId: userId || undefined,
      sortBy: sort[0]?.field, sortDirection: sort[0]?.sort ?? undefined,
    }),
    placeholderData: (prev) => prev,
  });
  const users = useQuery({ queryKey: ['history', 'users'], queryFn: historyApi.users, enabled: viewAll });
  const detail = useQuery({ queryKey: ['history', 'detail', selected], queryFn: () => historyApi.get(selected!), enabled: selected !== null });

  const remove = useMutation({
    mutationFn: (id: number) => historyApi.remove(id),
    onSuccess: () => { setToDelete(null); setSelected(null); notify('History record deleted.'); void queryClient.invalidateQueries({ queryKey: ['history'] }); },
  });
  const rerun = useMutation({
    mutationFn: (item: QueryHistoryItem) => historyApi.rerun(item.queryId).then((result) => ({ title: item.queryTitle, result })),
    onSuccess: (r) => { setRerunResult(r); void queryClient.invalidateQueries({ queryKey: ['history'] }); },
  });

  const copy = (sql?: string | null) => sql && navigator.clipboard.writeText(sql).then(() => notify('SQL copied.'), () => notify('Copy failed.', 'error'));

  const openInAssistant = () => {
    const d = detail.data;
    if (!d) return;
    navigate('/assistant', { state: { seed: {
      sql: d.item.executedSql ?? d.item.generatedSql ?? '', parameters: d.parameters, title: d.item.queryTitle, question: d.item.userQuestion ?? '',
      historyId: d.item.queryId, chartSuggestion: d.chartSuggestion, connectionId: d.item.connectionId,
    } } });
  };

  const columns: GridColDef<QueryHistoryItem>[] = [
    { field: 'queryTitle', headerName: 'Title', flex: 1.4, minWidth: 180 },
    { field: 'userQuestion', headerName: 'Question', flex: 2, minWidth: 220, sortable: false },
    ...(viewAll ? [{ field: 'userName', headerName: 'User', width: 150, sortable: false } as GridColDef<QueryHistoryItem>] : []),
    { field: 'connectionName', headerName: 'Connection', width: 160, sortable: false },
    { field: 'executionStatus', headerName: 'Status', width: 130, sortable: false, renderCell: (p) => <StatusChip status={p.value} /> },
    { field: 'executionDurationMs', headerName: 'Duration', width: 100, align: 'right', headerAlign: 'right', valueFormatter: (v: number | null) => formatDuration(v) },
    { field: 'returnedRows', headerName: 'Rows', width: 90, align: 'right', headerAlign: 'right', valueFormatter: (v: number | null) => formatNumber(v) },
    { field: 'createdAt', headerName: 'Created', width: 150, valueFormatter: (v: string) => formatDateTime(v) },
    {
      field: 'actions', headerName: '', width: 110, sortable: false, filterable: false,
      renderCell: (p) => (
        <Stack direction="row">
          <Tooltip title="Copy SQL"><IconButton size="small" onClick={(e) => { e.stopPropagation(); void copy(p.row.executedSql ?? p.row.generatedSql); }} aria-label="Copy SQL"><Copy size={15} /></IconButton></Tooltip>
          {hasPermission(Permissions.QueryExecute) && (p.row.executedSql || p.row.generatedSql) && (
            <Tooltip title="Re-run (re-validated)"><IconButton size="small" onClick={(e) => { e.stopPropagation(); rerun.mutate(p.row); }} aria-label="Re-run query"><Play size={15} /></IconButton></Tooltip>
          )}
          {hasPermission(Permissions.HistoryDelete) && (
            <Tooltip title="Delete"><IconButton size="small" onClick={(e) => { e.stopPropagation(); setToDelete(p.row); }} aria-label="Delete history record"><Trash2 size={15} /></IconButton></Tooltip>
          )}
        </Stack>
      ),
    },
  ];

  return (
    <Box>
      <PageHeader title="Query History" subtitle="Previous questions, generated SQL and executions. Re-runs are re-authorised and re-validated." />
      <ErrorAlert error={list.error ?? rerun.error} />
      <Card sx={{ p: 2, mb: 2 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
          <TextField label="Search" value={search} onChange={(e) => { setSearch(e.target.value); setPagination((p) => ({ ...p, page: 0 })); }} sx={{ flex: 1 }} />
          <TextField select label="Status" value={status} onChange={(e) => setStatus(e.target.value)} sx={{ minWidth: 160 }}>
            <MenuItem value="">All</MenuItem>
            {STATUSES.map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
          </TextField>
          <TextField label="From" type="date" value={from} onChange={(e) => setFrom(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField label="To" type="date" value={to} onChange={(e) => setTo(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
          {viewAll && users.data && (
            <TextField select label="User" value={userId} onChange={(e) => setUserId(e.target.value === '' ? '' : Number(e.target.value))} sx={{ minWidth: 180 }}>
              <MenuItem value="">All users</MenuItem>
              {users.data.map((u) => <MenuItem key={u.userId} value={u.userId}>{u.fullName}</MenuItem>)}
            </TextField>
          )}
        </Stack>
      </Card>
      <Card sx={{ height: 640 }}>
        <DataGrid
          rows={list.data?.items ?? []}
          getRowId={(r) => r.queryId}
          columns={columns}
          loading={list.isFetching}
          rowCount={list.data?.totalCount ?? 0}
          paginationMode="server"
          sortingMode="server"
          paginationModel={pagination}
          onPaginationModelChange={setPagination}
          sortModel={sort}
          onSortModelChange={setSort}
          pageSizeOptions={[25, 50, 100]}
          onRowClick={(p) => setSelected(p.row.queryId)}
          disableRowSelectionOnClick
          density="compact"
          sx={{ border: 0, '& .MuiDataGrid-row': { cursor: 'pointer' } }}
        />
      </Card>

      <Drawer anchor="right" open={selected !== null} onClose={() => setSelected(null)} slotProps={{ paper: { sx: { width: { xs: '100%', sm: 560 } } } }}>
        <Box sx={{ p: 3 }}>
          <Stack direction="row" sx={{ alignItems: 'center', mb: 2 }}>
            <Typography variant="h6" sx={{ flex: 1 }}>{detail.data?.item.queryTitle ?? 'Query'}</Typography>
            <IconButton onClick={() => setSelected(null)} aria-label="Close"><X size={18} /></IconButton>
          </Stack>
          <ErrorAlert error={detail.error} />
          {detail.data && (
            <Stack spacing={2}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                <StatusChip status={detail.data.item.executionStatus} />
                <Typography variant="body2" color="text.secondary">{detail.data.item.connectionName} · {formatDateTime(detail.data.item.createdAt)}</Typography>
              </Stack>
              {detail.data.item.userQuestion && <Box><Typography variant="subtitle2">Question</Typography><Typography>{detail.data.item.userQuestion}</Typography></Box>}
              {detail.data.explanation && <Box><Typography variant="subtitle2">Explanation</Typography><Typography variant="body2">{detail.data.explanation}</Typography></Box>}
              {detail.data.item.generatedSql && (
                <Box><Typography variant="subtitle2">Generated SQL</Typography><Box component="pre" sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 1, overflow: 'auto', fontSize: 12 }}>{detail.data.item.generatedSql}</Box></Box>
              )}
              {detail.data.item.executedSql && detail.data.item.executedSql !== detail.data.item.generatedSql && (
                <Box><Typography variant="subtitle2">Executed SQL</Typography><Box component="pre" sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 1, overflow: 'auto', fontSize: 12 }}>{detail.data.item.executedSql}</Box></Box>
              )}
              {detail.data.errorMessage && <Typography color="error" variant="body2">{detail.data.errorMessage}</Typography>}
              <Typography variant="body2" color="text.secondary">
                Duration {formatDuration(detail.data.item.executionDurationMs)} · Rows {formatNumber(detail.data.item.returnedRows)}
                {detail.data.aiModel ? ` · ${detail.data.aiModel} (${detail.data.promptVersion})` : ''}
              </Typography>
              <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
                {detail.data.isOwner && <Button variant="contained" startIcon={<ExternalLink size={16} />} onClick={openInAssistant}>Open in assistant</Button>}
                <Button startIcon={<Copy size={16} />} onClick={() => void copy(detail.data!.item.executedSql ?? detail.data!.item.generatedSql)}>Copy SQL</Button>
              </Stack>
            </Stack>
          )}
        </Box>
      </Drawer>

      <Dialog open={!!rerunResult} onClose={() => setRerunResult(null)} fullWidth maxWidth="lg">
        <DialogTitle>{rerunResult?.title}</DialogTitle>
        <DialogContent>
          {rerunResult && (<><ExecutionInfo result={rerunResult.result} /><ResultsGrid result={rerunResult.result} title={rerunResult.title} canExport={hasPermission(Permissions.ResultsExport)} /></>)}
        </DialogContent>
      </Dialog>

      <ConfirmDialog
        open={!!toDelete}
        title="Delete history record?"
        message={`"${toDelete?.queryTitle}" will be removed from your history.`}
        confirmLabel="Delete"
        destructive
        busy={remove.isPending}
        onConfirm={() => toDelete && remove.mutate(toDelete.queryId)}
        onClose={() => setToDelete(null)}
      />
    </Box>
  );
}
