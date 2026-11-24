import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert, Box, Button, Card, CardActions, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogTitle, Grid, MenuItem, Stack, Tab, Tabs, TextField, Typography,
} from '@mui/material';
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid';
import { CheckCircle2, FlaskConical, Pencil, Plus, Trash2 } from 'lucide-react';
import { useFormik } from 'formik';
import * as yup from 'yup';
import { authApi, settingsApi } from '../../api/endpoints';
import type { AiProviderConfiguration, AuditLog, SaveAiProviderConfiguration } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { PageHeader } from '../../components/PageHeader';
import { ErrorAlert } from '../../components/ErrorAlert';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { useNotify } from '../../components/Notifier';
import { formatDateTime } from '../../utils/format';

function ProviderDialog({ config, open, onClose }: { config: AiProviderConfiguration | null; open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient();
  const notify = useNotify();
  const save = useMutation({
    mutationFn: (v: SaveAiProviderConfiguration) => (config ? settingsApi.update(config.aiProviderConfigurationId, v) : settingsApi.create(v)),
    onSuccess: () => { notify('AI provider saved.'); void queryClient.invalidateQueries({ queryKey: ['ai'] }); onClose(); },
  });
  const formik = useFormik<SaveAiProviderConfiguration>({
    enableReinitialize: true,
    initialValues: {
      name: config?.name ?? 'OpenAI', providerType: config?.providerType ?? 'OpenAI', baseUrl: config?.baseUrl ?? 'https://api.openai.com/v1',
      model: config?.model ?? 'gpt-4o-mini', apiKey: '', temperature: config?.temperature ?? 0, maxOutputTokens: config?.maxOutputTokens ?? 2000,
      timeoutSeconds: config?.timeoutSeconds ?? 60, maxSchemaTables: config?.maxSchemaTables ?? 40,
    },
    validationSchema: yup.object({
      name: yup.string().required().max(100),
      baseUrl: yup.string().required().url('Must be a URL').matches(/^https:\/\//, 'Must use HTTPS'),
      model: yup.string().required().max(100),
      temperature: yup.number().min(0).max(1),
      maxOutputTokens: yup.number().min(256).max(16000),
      timeoutSeconds: yup.number().min(5).max(300),
      maxSchemaTables: yup.number().min(1).max(200),
    }),
    onSubmit: async (v) => {
      await save.mutateAsync({ ...v, apiKey: v.apiKey || null, temperature: Number(v.temperature), maxOutputTokens: Number(v.maxOutputTokens), timeoutSeconds: Number(v.timeoutSeconds), maxSchemaTables: Number(v.maxSchemaTables) }).catch(() => undefined);
    },
  });
  const f = formik;
  const field = (name: keyof SaveAiProviderConfiguration) => ({ name, value: f.values[name] ?? '', onChange: f.handleChange, error: !!f.touched[name] && !!f.errors[name], helperText: f.touched[name] && (f.errors[name] as string | undefined), fullWidth: true });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <form onSubmit={f.handleSubmit} noValidate>
        <DialogTitle>{config ? `Edit ${config.name}` : 'Add AI provider'}</DialogTitle>
        <DialogContent>
          <ErrorAlert error={save.error} />
          <Alert severity="info" sx={{ mb: 2 }}>The API key stays on the server. Prefer a secret reference such as <code>env:OPENAI_API_KEY</code>; a pasted key is encrypted before storage and never displayed again.</Alert>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, sm: 6 }}><TextField label="Name" {...field('name')} /></Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <TextField select label="Provider" {...field('providerType')}>
                <MenuItem value="OpenAI">OpenAI</MenuItem>
                <MenuItem value="AzureOpenAI">Azure OpenAI</MenuItem>
                <MenuItem value="OpenAICompatible">OpenAI-compatible</MenuItem>
              </TextField>
            </Grid>
            <Grid size={{ xs: 12, sm: 8 }}><TextField label="Base URL" {...field('baseUrl')} /></Grid>
            <Grid size={{ xs: 12, sm: 4 }}><TextField label={f.values.providerType === 'AzureOpenAI' ? 'Deployment name' : 'Model'} {...field('model')} /></Grid>
            <Grid size={{ xs: 12 }}><TextField label="API key or secret reference" type="password" autoComplete="off" {...field('apiKey')} helperText={config ? (config.hasApiKey ? 'A key is configured. Leave empty to keep it.' : 'No usable key is configured.') : 'Defaults to env:OPENAI_API_KEY when empty.'} /></Grid>
            <Grid size={{ xs: 6, sm: 3 }}><TextField label="Temperature" type="number" slotProps={{ htmlInput: { step: 0.1 } }} {...field('temperature')} /></Grid>
            <Grid size={{ xs: 6, sm: 3 }}><TextField label="Max output tokens" type="number" {...field('maxOutputTokens')} /></Grid>
            <Grid size={{ xs: 6, sm: 3 }}><TextField label="Timeout (s)" type="number" {...field('timeoutSeconds')} /></Grid>
            <Grid size={{ xs: 6, sm: 3 }}><TextField label="Max schema tables" type="number" {...field('maxSchemaTables')} /></Grid>
          </Grid>
        </DialogContent>
        <DialogActions><Button onClick={onClose}>Cancel</Button><Button type="submit" variant="contained" disabled={f.isSubmitting}>Save</Button></DialogActions>
      </form>
    </Dialog>
  );
}

function AiProvidersTab() {
  const queryClient = useQueryClient();
  const notify = useNotify();
  const list = useQuery({ queryKey: ['ai', 'configs'], queryFn: settingsApi.list });
  const [editing, setEditing] = useState<AiProviderConfiguration | null | undefined>(undefined);
  const [toDelete, setToDelete] = useState<AiProviderConfiguration | null>(null);
  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['ai'] });
  const activate = useMutation({ mutationFn: settingsApi.activate, onSuccess: () => { notify('Provider activated.'); void invalidate(); } });
  const remove = useMutation({ mutationFn: settingsApi.remove, onSuccess: () => { setToDelete(null); void invalidate(); } });
  const test = useMutation({ mutationFn: settingsApi.test });

  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ mb: 2 }}>
        <Button variant="contained" startIcon={<Plus size={16} />} onClick={() => setEditing(null)}>Add provider</Button>
        <Button startIcon={<FlaskConical size={16} />} onClick={() => test.mutate()} disabled={test.isPending}>Test active provider</Button>
      </Stack>
      <ErrorAlert error={list.error ?? activate.error ?? remove.error ?? test.error} />
      {test.data && <Alert severity={test.data.success ? 'success' : 'error'} sx={{ mb: 2 }}>{test.data.message}{test.data.model ? ` (${test.data.model}, ${test.data.latencyMs} ms)` : ''}</Alert>}
      {list.data?.length === 0 && <Alert severity="info">No provider is stored in the database; the API uses the <code>Ai:Provider</code> configuration section with <code>OPENAI_API_KEY</code>.</Alert>}
      <Grid container spacing={2}>
        {(list.data ?? []).map((c) => (
          <Grid key={c.aiProviderConfigurationId} size={{ xs: 12, md: 6 }}>
            <Card>
              <CardContent>
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
                  <Typography variant="h6" sx={{ flex: 1 }}>{c.name}</Typography>
                  {c.isActive && <Chip size="small" color="success" icon={<CheckCircle2 size={14} />} label="Active" />}
                  <Chip size="small" variant="outlined" label={c.hasApiKey ? 'Key configured' : 'No key'} color={c.hasApiKey ? 'default' : 'warning'} />
                </Stack>
                <Typography variant="body2" color="text.secondary">{c.providerType} · {c.model} · {c.baseUrl}</Typography>
                <Typography variant="body2" color="text.secondary">Key: {c.apiKeySecretReference} · temperature {c.temperature} · {c.maxSchemaTables} tables max</Typography>
              </CardContent>
              <CardActions sx={{ px: 2, pb: 2 }}>
                {!c.isActive && <Button size="small" onClick={() => activate.mutate(c.aiProviderConfigurationId)}>Activate</Button>}
                <Button size="small" startIcon={<Pencil size={15} />} onClick={() => setEditing(c)}>Edit</Button>
                <Button size="small" color="error" startIcon={<Trash2 size={15} />} onClick={() => setToDelete(c)}>Delete</Button>
              </CardActions>
            </Card>
          </Grid>
        ))}
      </Grid>
      <ProviderDialog open={editing !== undefined} config={editing ?? null} onClose={() => setEditing(undefined)} />
      <ConfirmDialog open={!!toDelete} title="Delete provider?" message={`"${toDelete?.name}" will be removed.`} destructive confirmLabel="Delete"
        onConfirm={() => toDelete && remove.mutate(toDelete.aiProviderConfigurationId)} onClose={() => setToDelete(null)} />
    </Box>
  );
}

function AccountTab() {
  const { user, logout } = useAuth();
  const change = useMutation({ mutationFn: (v: { current: string; next: string }) => authApi.changePassword(v.current, v.next), onSuccess: () => void logout() });
  const formik = useFormik({
    initialValues: { current: '', next: '', confirm: '' },
    validationSchema: yup.object({
      current: yup.string().required('Required'),
      next: yup.string().required('Required').min(10, 'At least 10 characters').matches(/[A-Z]/, 'Needs an uppercase letter').matches(/[a-z]/, 'Needs a lowercase letter').matches(/\d/, 'Needs a digit').matches(/[^A-Za-z0-9]/, 'Needs a symbol'),
      confirm: yup.string().oneOf([yup.ref('next')], 'Passwords do not match').required('Required'),
    }),
    onSubmit: async (v) => { await change.mutateAsync(v).catch(() => undefined); },
  });
  return (
    <Grid container spacing={3}>
      <Grid size={{ xs: 12, md: 5 }}>
        <Typography variant="h6" sx={{ mb: 1 }}>Profile</Typography>
        <Typography>{user?.fullName}</Typography>
        <Typography color="text.secondary">{user?.email}</Typography>
        <Stack direction="row" spacing={0.5} sx={{ mt: 1, flexWrap: 'wrap', gap: 0.5 }}>{user?.roles.map((r) => <Chip key={r} label={r} size="small" />)}</Stack>
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>Last sign-in: {formatDateTime(user?.lastLoginAt)}</Typography>
        <Typography variant="subtitle2" sx={{ mt: 2 }}>Permissions</Typography>
        <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.5, mt: 0.5 }}>{user?.permissions.map((p) => <Chip key={p} label={p} size="small" variant="outlined" />)}</Stack>
      </Grid>
      <Grid size={{ xs: 12, md: 7 }}>
        <Typography variant="h6" sx={{ mb: 1 }}>Change password</Typography>
        <ErrorAlert error={change.error} />
        <form onSubmit={formik.handleSubmit} noValidate>
          <Stack spacing={2} sx={{ maxWidth: 420 }}>
            {(['current', 'next', 'confirm'] as const).map((k) => (
              <TextField key={k} type="password" name={k} label={{ current: 'Current password', next: 'New password', confirm: 'Confirm new password' }[k]} autoComplete={k === 'current' ? 'current-password' : 'new-password'}
                value={formik.values[k]} onChange={formik.handleChange} error={formik.touched[k] && !!formik.errors[k]} helperText={formik.touched[k] && formik.errors[k]} />
            ))}
            <Box><Button type="submit" variant="contained" disabled={formik.isSubmitting}>Change password</Button></Box>
          </Stack>
        </form>
      </Grid>
    </Grid>
  );
}

function AuditTab() {
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 });
  const [action, setAction] = useState('');
  const logs = useQuery({ queryKey: ['audit', pagination, action], queryFn: () => settingsApi.auditLogs({ page: pagination.page + 1, pageSize: pagination.pageSize, action: action || undefined }), placeholderData: (p) => p });
  const columns: GridColDef<AuditLog>[] = [
    { field: 'createdAt', headerName: 'When', width: 160, valueFormatter: (v: string) => formatDateTime(v) },
    { field: 'userName', headerName: 'User', width: 160 },
    { field: 'action', headerName: 'Action', width: 220 },
    { field: 'entityType', headerName: 'Entity', width: 160, valueGetter: (_v, r) => (r.entityType ? `${r.entityType} ${r.entityId ?? ''}` : '') },
    { field: 'details', headerName: 'Details', flex: 1, minWidth: 220 },
    { field: 'ipAddress', headerName: 'IP', width: 130 },
  ];
  return (
    <Box>
      <TextField label="Action prefix" placeholder="e.g. auth., connection." value={action} onChange={(e) => setAction(e.target.value)} sx={{ mb: 2, width: 300 }} />
      <ErrorAlert error={logs.error} />
      <Box sx={{ height: 560 }}>
        <DataGrid rows={logs.data?.items ?? []} getRowId={(r) => r.auditLogId} columns={columns} loading={logs.isFetching} rowCount={logs.data?.totalCount ?? 0}
          paginationMode="server" paginationModel={pagination} onPaginationModelChange={setPagination} pageSizeOptions={[25, 50, 100]} density="compact" disableRowSelectionOnClick />
      </Box>
    </Box>
  );
}

export function SettingsPage() {
  const { hasPermission } = useAuth();
  const [params, setParams] = useSearchParams();
  const tabs = [
    { value: 'account', label: 'My account', show: true },
    { value: 'ai', label: 'AI provider', show: hasPermission(Permissions.AiSettingsManage) },
    { value: 'audit', label: 'Audit log', show: hasPermission(Permissions.AuditView) },
  ].filter((t) => t.show);
  const tab = tabs.some((t) => t.value === params.get('tab')) ? params.get('tab')! : tabs[0].value;

  return (
    <Box>
      <PageHeader title="Settings" />
      <Card>
        <Tabs value={tab} onChange={(_, v) => setParams({ tab: v })} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
          {tabs.map((t) => <Tab key={t.value} value={t.value} label={t.label} />)}
        </Tabs>
        <CardContent>
          {tab === 'account' && <AccountTab />}
          {tab === 'ai' && <AiProvidersTab />}
          {tab === 'audit' && <AuditTab />}
        </CardContent>
      </Card>
    </Box>
  );
}
