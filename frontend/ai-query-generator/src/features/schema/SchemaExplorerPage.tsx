import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert, Box, Button, Card, CardContent, Checkbox, Chip, Collapse, Grid, List, ListItemButton, ListItemIcon, ListItemText, MenuItem, Stack, Switch, Tab, Table, TableBody,
  TableCell, TableHead, TableRow, Tabs, TextField, Tooltip, Typography,
} from '@mui/material';
import { ChevronDown, ChevronRight, Eye, KeyRound, Link2, Save, Table2 } from 'lucide-react';
import { connectionsApi, schemaApi } from '../../api/endpoints';
import type { AllowedObjectRule } from '../../api/types';
import { useActiveConnection } from '../../context/ActiveConnectionContext';
import { useAuth } from '../../auth/AuthContext';
import { Permissions } from '../../auth/permissions';
import { PageHeader } from '../../components/PageHeader';
import { ErrorAlert } from '../../components/ErrorAlert';
import { EmptyState } from '../../components/EmptyState';
import { useNotify } from '../../components/Notifier';
import { formatDateTime, formatNumber } from '../../utils/format';
import { RelationshipGraph } from './RelationshipGraph';
import { evaluate, setColumnDenied, setSchemaAllowed, setTableAccess, type TableAccess } from './accessRules';

export function SchemaExplorerPage() {
  const { connections, activeConnection } = useActiveConnection();
  const { hasPermission } = useAuth();
  const notify = useNotify();
  const queryClient = useQueryClient();
  const [params, setParams] = useSearchParams();
  const canConfigure = hasPermission(Permissions.SchemaConfigure);

  // Admins may open inactive connections (e.g. to configure permissions before activation).
  const managed = useQuery({ queryKey: ['connections', 'managed'], queryFn: connectionsApi.managed, enabled: hasPermission(Permissions.ConnectionsManage) });
  const options = managed.data?.length
    ? managed.data.map((c) => ({ id: c.connectionId, name: c.connectionName }))
    : connections.map((c) => ({ id: c.connectionId, name: c.connectionName }));
  const connectionId = Number(params.get('connectionId')) || activeConnection?.connectionId || options[0]?.id || null;

  const [filter, setFilter] = useState(params.get('q') ?? '');
  const [selected, setSelected] = useState<string | null>(null);
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [tab, setTab] = useState<'details' | 'graph'>('details');
  const [rules, setRules] = useState<AllowedObjectRule[] | null>(null);

  const overview = useQuery({ queryKey: ['schema', connectionId], queryFn: () => schemaApi.overview(connectionId!), enabled: !!connectionId });
  const table = useQuery({ queryKey: ['schema', connectionId, 'table', selected], queryFn: () => schemaApi.table(connectionId!, selected!), enabled: !!connectionId && !!selected });
  const search = useQuery({ queryKey: ['schema', connectionId, 'search', filter], queryFn: () => schemaApi.search(connectionId!, filter), enabled: !!connectionId && filter.trim().length >= 2 });
  const savedRules = useQuery({ queryKey: ['allowed-objects', connectionId], queryFn: () => connectionsApi.allowedObjects(connectionId!), enabled: !!connectionId && canConfigure });

  useEffect(() => { if (savedRules.data) setRules(savedRules.data); }, [savedRules.data]);
  useEffect(() => { setSelected(null); }, [connectionId]);

  const saveRules = useMutation({
    mutationFn: (r: AllowedObjectRule[]) => connectionsApi.updateAllowedObjects(connectionId!, r),
    onSuccess: () => {
      notify('Permissions saved. AI and users now see only the permitted objects.');
      void queryClient.invalidateQueries({ queryKey: ['schema'] });
      void queryClient.invalidateQueries({ queryKey: ['allowed-objects'] });
      void queryClient.invalidateQueries({ queryKey: ['completions'] });
    },
  });

  const ev = useMemo(() => evaluate(rules ?? []), [rules]);
  const dirty = canConfigure && rules !== null && JSON.stringify(rules) !== JSON.stringify(savedRules.data ?? []);
  const columnMatches = new Set((search.data ?? []).filter((r) => r.kind === 'Column').map((r) => `${r.schemaName}.${r.tableName}`.toLowerCase()));
  const matches = (schema: string, name: string) => !filter.trim() || name.toLowerCase().includes(filter.toLowerCase()) || schema.toLowerCase().includes(filter.toLowerCase()) || columnMatches.has(`${schema}.${name}`.toLowerCase());

  if (!connectionId) return <><PageHeader title="Schema Explorer" /><EmptyState title="No connections available" /></>;

  const data = overview.data;
  const t = table.data;

  return (
    <Box>
      <PageHeader
        title="Schema Explorer"
        subtitle={data ? `${data.databaseName} · refreshed ${formatDateTime(data.lastSchemaRefreshAt)}` : undefined}
        actions={
          <Stack direction="row" spacing={1}>
            <TextField select label="Connection" value={connectionId} onChange={(e) => setParams({ connectionId: e.target.value })} sx={{ minWidth: 220 }}>
              {options.map((o) => <MenuItem key={o.id} value={o.id}>{o.name}</MenuItem>)}
            </TextField>
            {canConfigure && (
              <Button variant="contained" startIcon={<Save size={16} />} disabled={!dirty || saveRules.isPending} onClick={() => rules && saveRules.mutate(rules)}>
                Save permissions
              </Button>
            )}
          </Stack>
        }
      />
      <ErrorAlert error={overview.error ?? saveRules.error} />
      {canConfigure && (
        <Alert severity="info" sx={{ mb: 2 }}>
          Only permitted schemas, tables, views and columns are sent to the AI and can be queried. Nothing is permitted until you allow it.
          Restricted columns are also blocked by the SQL validator.
        </Alert>
      )}
      {data && data.schemas.length === 0 && (
        <EmptyState title="No metadata" description={canConfigure ? 'Test the connection and refresh its schema from the Connections page.' : 'No permitted objects are available on this connection.'} />
      )}

      {data && data.schemas.length > 0 && (
        <Grid container spacing={2}>
          <Grid size={{ xs: 12, md: 4, lg: 3 }}>
            <Card>
              <CardContent sx={{ pb: 1 }}>
                <TextField fullWidth label="Search tables & columns" value={filter} onChange={(e) => setFilter(e.target.value)} />
              </CardContent>
              <List dense sx={{ maxHeight: 640, overflow: 'auto' }}>
                {data.schemas.map((s) => (
                  <Box key={s.schemaName}>
                    <ListItemButton onClick={() => setCollapsed({ ...collapsed, [s.schemaName]: !collapsed[s.schemaName] })}>
                      <ListItemIcon sx={{ minWidth: 28 }}>{collapsed[s.schemaName] ? <ChevronRight size={16} /> : <ChevronDown size={16} />}</ListItemIcon>
                      <ListItemText primary={s.schemaName} slotProps={{ primary: { sx: { fontWeight: 600 } } }} />
                      {canConfigure && rules && (
                        <Tooltip title="Allow every table in this schema">
                          <Switch size="small" checked={ev.schemaAllowed(s.schemaName)} onClick={(e) => e.stopPropagation()} onChange={(e) => setRules(setSchemaAllowed(rules, s.schemaName, e.target.checked))} slotProps={{ input: { 'aria-label': `Allow schema ${s.schemaName}` } }} />
                        </Tooltip>
                      )}
                    </ListItemButton>
                    <Collapse in={!collapsed[s.schemaName]}>
                      {s.tables.filter((x) => matches(s.schemaName, x.tableName)).map((x) => {
                        const key = `${x.schemaName}.${x.tableName}`;
                        const allowed = rules ? ev.tableAllowed(x.schemaName, x.tableName) : x.isAllowed;
                        return (
                          <ListItemButton key={key} selected={selected === key} onClick={() => { setSelected(key); setTab('details'); }} sx={{ pl: 4.5 }}>
                            <ListItemIcon sx={{ minWidth: 26 }}>{x.objectType === 'View' ? <Eye size={15} /> : <Table2 size={15} />}</ListItemIcon>
                            <ListItemText primary={x.tableName} secondary={`${x.allowedColumnCount}/${x.columnCount} cols`} slotProps={{ primary: { sx: { opacity: allowed ? 1 : 0.55 } } }} />
                            {canConfigure && <Chip size="small" label={allowed ? 'AI' : 'off'} color={allowed ? 'success' : 'default'} variant="outlined" />}
                          </ListItemButton>
                        );
                      })}
                    </Collapse>
                  </Box>
                ))}
              </List>
            </Card>
          </Grid>

          <Grid size={{ xs: 12, md: 8, lg: 9 }}>
            <Card>
              <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
                <Tab value="details" label="Table details" />
                <Tab value="graph" label="Relationships" />
              </Tabs>
              <CardContent>
                {tab === 'graph' && <RelationshipGraph schemas={data.schemas} relationships={data.relationships} highlight={selected} />}
                {tab === 'details' && !selected && <EmptyState title="Select a table" description="Choose a table or view to see its columns, keys, indexes and relationships." />}
                {tab === 'details' && selected && <ErrorAlert error={table.error} />}
                {tab === 'details' && t && (
                  <Box>
                    <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ alignItems: { sm: 'center' }, gap: 1, mb: 2 }}>
                      <Box sx={{ flex: 1 }}>
                        <Typography variant="h6">{t.schemaName}.{t.tableName}</Typography>
                        <Typography variant="body2" color="text.secondary">
                          {t.objectType} · ~{formatNumber(t.rowCountEstimate)} rows{t.description ? ` · ${t.description}` : ''}
                        </Typography>
                      </Box>
                      {canConfigure && rules && (
                        <TextField select label="AI access" value={ev.tableRule(t.schemaName, t.tableName)} sx={{ minWidth: 200 }}
                          onChange={(e) => setRules(setTableAccess(rules, t.schemaName, t.tableName, t.objectType === 'View' ? 'View' : 'Table', e.target.value as TableAccess))}>
                          <MenuItem value="inherit">Inherit from schema</MenuItem>
                          <MenuItem value="allow">Allow</MenuItem>
                          <MenuItem value="deny">Deny</MenuItem>
                        </TextField>
                      )}
                    </Stack>
                    <Box sx={{ overflowX: 'auto' }}>
                      <Table size="small">
                        <TableHead>
                          <TableRow>
                            <TableCell>Column</TableCell>
                            <TableCell>Type</TableCell>
                            <TableCell>Nullable</TableCell>
                            <TableCell>References</TableCell>
                            {canConfigure && <TableCell align="center">Permitted</TableCell>}
                          </TableRow>
                        </TableHead>
                        <TableBody>
                          {t.columns.map((c) => {
                            const denied = rules ? ev.columnDenied(t.schemaName, t.tableName, c.columnName) : !c.isAllowed;
                            return (
                              <TableRow key={c.columnName} hover>
                                <TableCell>
                                  <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
                                    {c.isPrimaryKey && <Tooltip title="Primary key"><KeyRound size={14} /></Tooltip>}
                                    <span>{c.columnName}</span>
                                    {c.isIdentity && <Chip size="small" label="identity" variant="outlined" />}
                                    {c.isComputed && <Chip size="small" label="computed" variant="outlined" />}
                                  </Stack>
                                  {c.description && <Typography variant="caption" color="text.secondary">{c.description}</Typography>}
                                </TableCell>
                                <TableCell><code>{c.dataType}</code></TableCell>
                                <TableCell>{c.isNullable ? 'Yes' : 'No'}</TableCell>
                                <TableCell>{c.references ? <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}><Link2 size={13} /><span>{c.references}</span></Stack> : '—'}</TableCell>
                                {canConfigure && rules && (
                                  <TableCell align="center">
                                    <Checkbox size="small" checked={!denied} onChange={(e) => setRules(setColumnDenied(rules, t.schemaName, t.tableName, c.columnName, !e.target.checked))} slotProps={{ input: { 'aria-label': `Permit column ${c.columnName}` } }} />
                                  </TableCell>
                                )}
                              </TableRow>
                            );
                          })}
                        </TableBody>
                      </Table>
                    </Box>
                    {t.indexes.length > 0 && (
                      <Box sx={{ mt: 3 }}>
                        <Typography variant="subtitle2" sx={{ mb: 1 }}>Indexes</Typography>
                        <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
                          {t.indexes.map((i) => <Chip key={i.indexName} size="small" variant="outlined" label={`${i.indexName} (${i.columns})${i.isPrimaryKey ? ' · PK' : i.isUnique ? ' · unique' : ''}`} />)}
                        </Stack>
                      </Box>
                    )}
                    {t.relationships.length > 0 && (
                      <Box sx={{ mt: 3 }}>
                        <Typography variant="subtitle2" sx={{ mb: 1 }}>Relationships</Typography>
                        {t.relationships.map((r) => (
                          <Typography key={r.name + r.fromColumn} variant="body2">{r.fromTable}.{r.fromColumn} → {r.toTable}.{r.toColumn} <Typography component="span" variant="caption" color="text.secondary">({r.name})</Typography></Typography>
                        ))}
                      </Box>
                    )}
                  </Box>
                )}
              </CardContent>
            </Card>
          </Grid>
        </Grid>
      )}
    </Box>
  );
}
