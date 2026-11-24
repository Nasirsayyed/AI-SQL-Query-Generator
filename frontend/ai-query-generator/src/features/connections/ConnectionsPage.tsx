import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, Box, Button, Card, CardActions, CardContent, Chip, CircularProgress, Grid, IconButton, Stack, Switch, Tooltip, Typography, FormControlLabel } from '@mui/material';
import { Database, FlaskConical, Pencil, Plus, RefreshCw, ShieldCheck, Trash2 } from 'lucide-react';
import { connectionsApi } from '../../api/endpoints';
import type { Connection, ConnectionTestResult, SaveConnectionRequest } from '../../api/types';
import { PageHeader } from '../../components/PageHeader';
import { StatusChip } from '../../components/StatusChip';
import { ErrorAlert } from '../../components/ErrorAlert';
import { EmptyState } from '../../components/EmptyState';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { useNotify } from '../../components/Notifier';
import { fromNow } from '../../utils/format';
import { ConnectionFormDialog } from './ConnectionFormDialog';

export function ConnectionsPage() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const notify = useNotify();
  const [editing, setEditing] = useState<Connection | null | undefined>(undefined);
  const [toDelete, setToDelete] = useState<Connection | null>(null);
  const [testResults, setTestResults] = useState<Record<number, ConnectionTestResult>>({});

  const list = useQuery({ queryKey: ['connections', 'managed'], queryFn: connectionsApi.managed });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['connections'] });

  const save = useMutation({
    mutationFn: (body: SaveConnectionRequest) => (editing ? connectionsApi.update(editing.connectionId, body) : connectionsApi.create(body)),
    onSuccess: () => { setEditing(undefined); notify('Connection saved. Test it before activating.'); void invalidate(); },
  });
  const test = useMutation({
    mutationFn: (id: number) => connectionsApi.test(id).then((r) => ({ id, r })),
    onSuccess: ({ id, r }) => { setTestResults((s) => ({ ...s, [id]: r })); void invalidate(); },
  });
  const toggle = useMutation({ mutationFn: ({ id, active }: { id: number; active: boolean }) => connectionsApi.setActive(id, active), onSuccess: () => void invalidate() });
  const refresh = useMutation({
    mutationFn: ({ id, full }: { id: number; full: boolean }) => connectionsApi.refreshSchema(id, full),
    onSuccess: (r) => {
      notify(`Schema refreshed: ${r.tables} tables, ${r.views} views (${r.tablesAdded} added, ${r.tablesUpdated} updated, ${r.tablesRemoved} removed).`);
      void invalidate();
      void queryClient.invalidateQueries({ queryKey: ['schema'] });
    },
  });
  const remove = useMutation({ mutationFn: (id: number) => connectionsApi.remove(id), onSuccess: () => { setToDelete(null); notify('Connection deleted.'); void invalidate(); } });

  return (
    <Box>
      <PageHeader
        title="Database Connections"
        subtitle="Admin-only. Credentials are encrypted and never returned to the browser."
        actions={<Button variant="contained" startIcon={<Plus size={17} />} onClick={() => { save.reset(); setEditing(null); }}>Add connection</Button>}
      />
      <ErrorAlert error={list.error ?? test.error ?? toggle.error ?? refresh.error ?? remove.error} />
      {list.data?.length === 0 && <EmptyState title="No connections yet" description="Add a SQL Server connection that uses a dedicated read-only login." icon={<Database size={36} />} />}

      <Grid container spacing={2}>
        {(list.data ?? []).map((c) => {
          const t = testResults[c.connectionId];
          const testing = test.isPending && test.variables === c.connectionId;
          const refreshing = refresh.isPending && refresh.variables?.id === c.connectionId;
          return (
            <Grid key={c.connectionId} size={{ xs: 12, lg: 6 }}>
              <Card>
                <CardContent>
                  <Stack direction="row" sx={{ alignItems: 'center', gap: 1, mb: 1 }}>
                    <Typography variant="h6" sx={{ flex: 1 }}>{c.connectionName}</Typography>
                    <StatusChip status={c.status} />
                    <Chip size="small" label={c.isActive ? 'Active' : 'Inactive'} color={c.isActive ? 'success' : 'default'} />
                  </Stack>
                  <Typography variant="body2" color="text.secondary">
                    {c.serverName} / {c.databaseName} · {c.authenticationType}{c.username ? ` (${c.username})` : ''}
                  </Typography>
                  <Typography variant="body2" color="text.secondary">
                    Max {c.maxRows.toLocaleString()} rows · {c.commandTimeoutSeconds}s timeout · Roles: {c.allowedRoles.join(', ') || 'admins only'}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    Last test {fromNow(c.lastTestedAt)} · Schema refreshed {fromNow(c.lastSchemaRefreshAt)}
                  </Typography>
                  {c.lastTestMessage && !t && <Alert severity={c.status === 'Healthy' ? 'success' : c.status === 'Untested' ? 'info' : 'warning'} sx={{ mt: 1 }}>{c.lastTestMessage}</Alert>}
                  {t && (
                    <Alert severity={t.success && t.isLeastPrivilege ? 'success' : t.success ? 'warning' : 'error'} sx={{ mt: 1 }}>
                      {t.message}
                      {t.loginName && <div>Login: {t.loginName} · SQL Server {t.serverVersion}</div>}
                      {t.privilegeWarnings.length > 0 && <ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>{t.privilegeWarnings.map((w) => <li key={w}>{w}</li>)}</ul>}
                    </Alert>
                  )}
                </CardContent>
                <CardActions sx={{ px: 2, pb: 2, flexWrap: 'wrap', gap: 1 }}>
                  <Button size="small" startIcon={testing ? <CircularProgress size={14} /> : <FlaskConical size={16} />} onClick={() => test.mutate(c.connectionId)} disabled={testing}>Test</Button>
                  <Button size="small" startIcon={refreshing ? <CircularProgress size={14} /> : <RefreshCw size={16} />} onClick={() => refresh.mutate({ id: c.connectionId, full: false })} disabled={refreshing || c.status !== 'Healthy'}>Refresh schema</Button>
                  <Button size="small" startIcon={<ShieldCheck size={16} />} onClick={() => navigate(`/schema?connectionId=${c.connectionId}`)}>Permissions</Button>
                  <FormControlLabel
                    sx={{ ml: 1 }}
                    control={<Switch size="small" checked={c.isActive} onChange={(e) => toggle.mutate({ id: c.connectionId, active: e.target.checked })} disabled={!c.isActive && c.status !== 'Healthy'} />}
                    label={<Typography variant="body2">Active</Typography>}
                  />
                  <Box sx={{ flex: 1 }} />
                  <Tooltip title="Edit"><IconButton size="small" onClick={() => { save.reset(); setEditing(c); }} aria-label="Edit connection"><Pencil size={16} /></IconButton></Tooltip>
                  <Tooltip title="Delete"><IconButton size="small" onClick={() => setToDelete(c)} aria-label="Delete connection"><Trash2 size={16} /></IconButton></Tooltip>
                </CardActions>
              </Card>
            </Grid>
          );
        })}
      </Grid>

      <ConnectionFormDialog
        open={editing !== undefined}
        connection={editing ?? null}
        onClose={() => setEditing(undefined)}
        onSubmit={async (body) => { await save.mutateAsync(body).catch(() => undefined); }}
        error={save.error}
      />
      <ConfirmDialog
        open={!!toDelete}
        title="Delete connection?"
        message={`"${toDelete?.connectionName}" will be deactivated and its stored credential destroyed. History is preserved.`}
        confirmLabel="Delete"
        destructive
        busy={remove.isPending}
        onConfirm={() => toDelete && remove.mutate(toDelete.connectionId)}
        onClose={() => setToDelete(null)}
      />
    </Box>
  );
}
