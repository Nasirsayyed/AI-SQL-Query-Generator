import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert, Autocomplete, Box, Button, Card, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, Stack, Switch, Tab, Table, TableBody,
  TableCell, TableHead, TableRow, Tabs, TextField, Tooltip, Typography,
} from '@mui/material';
import { DataGrid, type GridColDef, type GridPaginationModel } from '@mui/x-data-grid';
import { KeyRound, Pencil, UserPlus } from 'lucide-react';
import { useFormik } from 'formik';
import * as yup from 'yup';
import { usersApi } from '../../api/endpoints';
import type { Role, UserListItem } from '../../api/types';
import { useAuth } from '../../auth/AuthContext';
import { Permissions, RoleNames } from '../../auth/permissions';
import { PageHeader } from '../../components/PageHeader';
import { ErrorAlert } from '../../components/ErrorAlert';
import { useNotify } from '../../components/Notifier';
import { formatDateTime } from '../../utils/format';

const strongPassword = yup.string().required('Required').min(10, 'At least 10 characters')
  .matches(/[A-Z]/, 'Needs an uppercase letter').matches(/[a-z]/, 'Needs a lowercase letter').matches(/\d/, 'Needs a digit').matches(/[^A-Za-z0-9]/, 'Needs a symbol');

function UserDialog({ user, open, onClose, assignableRoles }: { user: UserListItem | null; open: boolean; onClose: () => void; assignableRoles: string[] }) {
  const queryClient = useQueryClient();
  const notify = useNotify();
  const isNew = !user;
  const save = useMutation({
    mutationFn: (v: { fullName: string; email: string; password: string; roles: string[]; isActive: boolean }) =>
      isNew ? usersApi.create({ fullName: v.fullName, email: v.email, password: v.password, roles: v.roles }) : usersApi.update(user!.userId, { fullName: v.fullName, isActive: v.isActive, roles: v.roles }),
    onSuccess: () => { notify(isNew ? 'User created.' : 'User updated.'); void queryClient.invalidateQueries({ queryKey: ['users'] }); onClose(); },
  });
  const formik = useFormik({
    enableReinitialize: true,
    initialValues: { fullName: user?.fullName ?? '', email: user?.email ?? '', password: '', roles: user?.roles ?? ['Analyst'], isActive: user?.isActive ?? true },
    validationSchema: yup.object({
      fullName: yup.string().trim().required('Required').max(150),
      email: yup.string().trim().email('Invalid email').required('Required'),
      password: isNew ? strongPassword : yup.string(),
      roles: yup.array().min(1, 'Select at least one role'),
    }),
    onSubmit: async (v) => { await save.mutateAsync(v).catch(() => undefined); },
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={formik.handleSubmit} noValidate>
        <DialogTitle>{isNew ? 'Register user' : `Edit ${user?.fullName}`}</DialogTitle>
        <DialogContent>
          <ErrorAlert error={save.error} />
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField label="Full name" name="fullName" value={formik.values.fullName} onChange={formik.handleChange} error={formik.touched.fullName && !!formik.errors.fullName} helperText={formik.touched.fullName && formik.errors.fullName} />
            <TextField label="Email" name="email" value={formik.values.email} onChange={formik.handleChange} disabled={!isNew} error={formik.touched.email && !!formik.errors.email} helperText={formik.touched.email && formik.errors.email} />
            {isNew && <TextField label="Initial password" type="password" name="password" autoComplete="new-password" value={formik.values.password} onChange={formik.handleChange} error={formik.touched.password && !!formik.errors.password} helperText={(formik.touched.password && formik.errors.password) || 'Share it securely; ask the user to change it.'} />}
            <Autocomplete multiple options={assignableRoles} value={formik.values.roles} onChange={(_, v) => void formik.setFieldValue('roles', v)}
              renderInput={(p) => <TextField {...p} label="Roles" error={!!formik.errors.roles} helperText={formik.errors.roles as string | undefined} />} />
            {!isNew && <FormControlLabel control={<Switch name="isActive" checked={formik.values.isActive} onChange={formik.handleChange} />} label="Active" />}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={formik.isSubmitting}>Save</Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}

function ResetPasswordDialog({ user, onClose }: { user: UserListItem | null; onClose: () => void }) {
  const notify = useNotify();
  const reset = useMutation({ mutationFn: (pw: string) => usersApi.resetPassword(user!.userId, pw), onSuccess: () => { notify('Password reset; the user\'s sessions were revoked.'); onClose(); } });
  const formik = useFormik({ initialValues: { password: '' }, validationSchema: yup.object({ password: strongPassword }), onSubmit: async (v) => { await reset.mutateAsync(v.password).catch(() => undefined); } });
  return (
    <Dialog open={!!user} onClose={onClose} fullWidth maxWidth="xs">
      <form onSubmit={formik.handleSubmit} noValidate>
        <DialogTitle>Reset password for {user?.fullName}</DialogTitle>
        <DialogContent>
          <ErrorAlert error={reset.error} />
          <TextField fullWidth sx={{ mt: 1 }} label="New password" type="password" name="password" autoComplete="new-password" value={formik.values.password} onChange={formik.handleChange} error={formik.touched.password && !!formik.errors.password} helperText={formik.touched.password && formik.errors.password} />
        </DialogContent>
        <DialogActions><Button onClick={onClose}>Cancel</Button><Button type="submit" variant="contained">Reset</Button></DialogActions>
      </form>
    </Dialog>
  );
}

function RolesTab() {
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const notify = useNotify();
  const roles = useQuery({ queryKey: ['roles'], queryFn: usersApi.roles });
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: usersApi.permissions });
  const [edits, setEdits] = useState<Record<number, string[]>>({});
  const save = useMutation({
    mutationFn: ({ role, perms }: { role: Role; perms: string[] }) => usersApi.updateRolePermissions(role.roleId, perms),
    onSuccess: (r) => { notify(`${r.name} permissions updated. Affected users must sign in again.`); setEdits((e) => { const n = { ...e }; delete n[r.roleId]; return n; }); void queryClient.invalidateQueries({ queryKey: ['roles'] }); },
  });
  const isSuper = hasPermission(Permissions.AdminsManage);
  const editable = (r: Role) => r.name !== 'SuperAdmin' && (isSuper || (r.name !== 'Admin'));

  return (
    <Box sx={{ overflowX: 'auto' }}>
      <ErrorAlert error={roles.error ?? save.error} />
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            <TableCell>Permission</TableCell>
            {(roles.data ?? []).map((r) => (
              <TableCell key={r.roleId} align="center">
                <Typography variant="subtitle2">{r.name}</Typography>
                <Typography variant="caption" color="text.secondary">{r.userCount} users</Typography>
                {edits[r.roleId] && <Box><Button size="small" onClick={() => save.mutate({ role: r, perms: edits[r.roleId] })}>Save</Button></Box>}
              </TableCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {(permissions.data ?? []).map((p) => (
            <TableRow key={p.code} hover>
              <TableCell><Typography variant="body2">{p.description}</Typography><Typography variant="caption" color="text.secondary"><code>{p.code}</code></Typography></TableCell>
              {(roles.data ?? []).map((r) => {
                const current = edits[r.roleId] ?? r.permissions;
                return (
                  <TableCell key={r.roleId} align="center">
                    <Checkbox size="small" checked={current.includes(p.code)} disabled={!editable(r) || (p.code === Permissions.AdminsManage && !isSuper)}
                      onChange={(e) => setEdits({ ...edits, [r.roleId]: e.target.checked ? [...current, p.code] : current.filter((c) => c !== p.code) })}
                      slotProps={{ input: { 'aria-label': `${r.name} ${p.code}` } }} />
                  </TableCell>
                );
              })}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

export function UsersPage() {
  const { hasPermission, user: me } = useAuth();
  const [tab, setTab] = useState<'users' | 'roles'>('users');
  const [pagination, setPagination] = useState<GridPaginationModel>({ page: 0, pageSize: 25 });
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<UserListItem | null | undefined>(undefined);
  const [resetting, setResetting] = useState<UserListItem | null>(null);
  const isSuper = hasPermission(Permissions.AdminsManage);
  const assignable = RoleNames.filter((r) => isSuper || (r !== 'SuperAdmin' && r !== 'Admin'));

  const users = useQuery({
    queryKey: ['users', pagination, search],
    queryFn: () => usersApi.list({ page: pagination.page + 1, pageSize: pagination.pageSize, search: search || undefined }),
    placeholderData: (p) => p,
  });

  const columns: GridColDef<UserListItem>[] = [
    { field: 'fullName', headerName: 'Name', flex: 1, minWidth: 160 },
    { field: 'email', headerName: 'Email', flex: 1.2, minWidth: 200 },
    { field: 'roles', headerName: 'Roles', flex: 1, minWidth: 180, sortable: false, renderCell: (p) => <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', height: '100%' }}>{(p.value as string[]).map((r) => <Chip key={r} size="small" label={r} />)}</Stack> },
    { field: 'isActive', headerName: 'Status', width: 100, renderCell: (p) => <Chip size="small" label={p.value ? 'Active' : 'Inactive'} color={p.value ? 'success' : 'default'} variant="outlined" /> },
    { field: 'lastLoginAt', headerName: 'Last login', width: 150, valueFormatter: (v: string | null) => formatDateTime(v) },
    {
      field: 'actions', headerName: '', width: 100, sortable: false,
      renderCell: (p) => {
        const privileged = p.row.roles.some((r) => r === 'SuperAdmin' || r === 'Admin');
        const allowed = isSuper || !privileged;
        return (
          <Stack direction="row">
            <Tooltip title={allowed ? 'Edit' : 'Only a Super Admin can manage administrators'}><span><IconButton size="small" disabled={!allowed} onClick={() => setEditing(p.row)} aria-label="Edit user"><Pencil size={15} /></IconButton></span></Tooltip>
            <Tooltip title="Reset password"><span><IconButton size="small" disabled={!allowed || p.row.userId === me?.userId} onClick={() => setResetting(p.row)} aria-label="Reset password"><KeyRound size={15} /></IconButton></span></Tooltip>
          </Stack>
        );
      },
    },
  ];

  return (
    <Box>
      <PageHeader title="Users & Roles" subtitle="Admin-controlled registration and role-based access. Permissions are enforced by the API."
        actions={tab === 'users' && <Button variant="contained" startIcon={<UserPlus size={17} />} onClick={() => setEditing(null)}>Register user</Button>} />
      <Card>
        <Tabs value={tab} onChange={(_, v) => setTab(v)} sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}>
          <Tab value="users" label="Users" />
          <Tab value="roles" label="Roles & permissions" />
        </Tabs>
        {tab === 'users' && (
          <Box sx={{ p: 2 }}>
            <ErrorAlert error={users.error} />
            <TextField label="Search users" value={search} onChange={(e) => setSearch(e.target.value)} sx={{ mb: 2, width: { xs: '100%', sm: 320 } }} />
            {!isSuper && <Alert severity="info" sx={{ mb: 2 }}>Administrator accounts can only be managed by a Super Admin.</Alert>}
            <Box sx={{ height: 560 }}>
              <DataGrid rows={users.data?.items ?? []} getRowId={(r) => r.userId} columns={columns} loading={users.isFetching} rowCount={users.data?.totalCount ?? 0}
                paginationMode="server" paginationModel={pagination} onPaginationModelChange={setPagination} pageSizeOptions={[25, 50]} disableRowSelectionOnClick density="compact" />
            </Box>
          </Box>
        )}
        {tab === 'roles' && <Box sx={{ p: 2 }}><RolesTab /></Box>}
      </Card>
      <UserDialog open={editing !== undefined} user={editing ?? null} onClose={() => setEditing(undefined)} assignableRoles={[...assignable]} />
      <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />
    </Box>
  );
}
