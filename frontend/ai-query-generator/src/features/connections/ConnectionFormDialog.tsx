import { Alert, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Grid, MenuItem, TextField, Autocomplete } from '@mui/material';
import { useFormik } from 'formik';
import * as yup from 'yup';
import type { Connection, SaveConnectionRequest, SqlAuthenticationType } from '../../api/types';
import { RoleNames } from '../../auth/permissions';
import { ErrorAlert } from '../../components/ErrorAlert';

const AUTH_TYPES: { value: SqlAuthenticationType; label: string }[] = [
  { value: 'SqlPassword', label: 'SQL Server authentication' },
  { value: 'WindowsIntegrated', label: 'Windows integrated (API identity)' },
  { value: 'EntraManagedIdentity', label: 'Microsoft Entra managed identity' },
  { value: 'EntraDefault', label: 'Microsoft Entra default credential' },
];

const schema = (isNew: boolean) => yup.object({
  connectionName: yup.string().trim().required('Required').max(100),
  serverName: yup.string().trim().required('Required').max(256)
    .matches(/^(tcp:)?[A-Za-z0-9.\-_]+(\\[A-Za-z0-9_\-$]+)?(,\d{1,5})?$/, 'Use host, host\\instance or host,port (connection strings are not accepted)'),
  databaseName: yup.string().trim().required('Required').max(128).matches(/^[^;=[\]'"]+$/, 'Invalid characters'),
  authenticationType: yup.string().required(),
  username: yup.string().when('authenticationType', { is: 'SqlPassword', then: (s) => s.trim().required('Required for SQL authentication') }),
  password: yup.string().when('authenticationType', { is: (v: string) => v === 'SqlPassword' && isNew, then: (s) => s.required('Required for SQL authentication') }),
  connectionTimeoutSeconds: yup.number().min(1).max(120).required(),
  commandTimeoutSeconds: yup.number().min(1).max(600).required(),
  maxRows: yup.number().min(1).max(100000).required(),
});

export function ConnectionFormDialog({ open, connection, onClose, onSubmit, error }: {
  open: boolean;
  connection: Connection | null;
  onClose: () => void;
  onSubmit: (body: SaveConnectionRequest) => Promise<void>;
  error?: unknown;
}) {
  const isNew = !connection;
  const formik = useFormik({
    enableReinitialize: true,
    initialValues: {
      connectionName: connection?.connectionName ?? '',
      serverName: connection?.serverName ?? '',
      databaseName: connection?.databaseName ?? '',
      authenticationType: connection?.authenticationType ?? ('SqlPassword' as SqlAuthenticationType),
      username: connection?.username ?? '',
      password: '',
      encryptConnection: connection?.encryptConnection ?? true,
      trustServerCertificate: connection?.trustServerCertificate ?? false,
      connectionTimeoutSeconds: connection?.connectionTimeoutSeconds ?? 15,
      commandTimeoutSeconds: connection?.commandTimeoutSeconds ?? 30,
      maxRows: connection?.maxRows ?? 1000,
      allowedRoles: connection?.allowedRoles ?? ['Developer', 'Analyst'],
    },
    validationSchema: schema(isNew),
    onSubmit: async (v) => {
      await onSubmit({
        ...v,
        username: v.authenticationType === 'SqlPassword' ? v.username.trim() : null,
        // Write-only: an empty password on edit keeps the stored credential.
        password: v.authenticationType === 'SqlPassword' && v.password ? v.password : null,
        connectionTimeoutSeconds: Number(v.connectionTimeoutSeconds),
        commandTimeoutSeconds: Number(v.commandTimeoutSeconds),
        maxRows: Number(v.maxRows),
      });
    },
  });
  const f = formik;
  const field = (name: keyof typeof f.values) => ({
    name,
    value: f.values[name] as string | number,
    onChange: f.handleChange,
    onBlur: f.handleBlur,
    error: !!f.touched[name] && !!f.errors[name],
    helperText: f.touched[name] && (f.errors[name] as string | undefined),
    fullWidth: true,
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
      <form onSubmit={f.handleSubmit} noValidate>
        <DialogTitle>{isNew ? 'Add SQL Server connection' : `Edit ${connection?.connectionName}`}</DialogTitle>
        <DialogContent>
          <ErrorAlert error={error} />
          <Alert severity="info" sx={{ mb: 2 }}>
            Use a dedicated login with SELECT-only permissions on approved objects. Connections using sysadmin, db_owner or
            write permissions fail the least-privilege test and cannot be activated. Changing the server or credentials requires a new test.
          </Alert>
          <Grid container spacing={2}>
            <Grid size={{ xs: 12, sm: 6 }}><TextField label="Connection name" required {...field('connectionName')} /></Grid>
            <Grid size={{ xs: 12, sm: 6 }}><TextField label="Server" required placeholder="sql01.contoso.local,1433" {...field('serverName')} /></Grid>
            <Grid size={{ xs: 12, sm: 6 }}><TextField label="Database" required {...field('databaseName')} /></Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <TextField select label="Authentication" {...field('authenticationType')}>
                {AUTH_TYPES.map((a) => <MenuItem key={a.value} value={a.value}>{a.label}</MenuItem>)}
              </TextField>
            </Grid>
            {f.values.authenticationType === 'SqlPassword' && (
              <>
                <Grid size={{ xs: 12, sm: 6 }}><TextField label="Username" autoComplete="off" {...field('username')} /></Grid>
                <Grid size={{ xs: 12, sm: 6 }}>
                  <TextField
                    label="Password or secret reference"
                    type="password"
                    autoComplete="new-password"
                    {...field('password')}
                    helperText={(f.touched.password && f.errors.password) || (isNew ? 'Encrypted at rest. Or use env:AIQG_SECRET_NAME.' : connection?.hasStoredCredentials ? 'Leave empty to keep the stored credential.' : 'No credential stored.')}
                  />
                </Grid>
              </>
            )}
            <Grid size={{ xs: 12, sm: 4 }}><TextField label="Connection timeout (s)" type="number" {...field('connectionTimeoutSeconds')} /></Grid>
            <Grid size={{ xs: 12, sm: 4 }}><TextField label="Query timeout (s)" type="number" {...field('commandTimeoutSeconds')} /></Grid>
            <Grid size={{ xs: 12, sm: 4 }}><TextField label="Maximum rows" type="number" {...field('maxRows')} /></Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <FormControlLabel control={<Checkbox name="encryptConnection" checked={f.values.encryptConnection} onChange={f.handleChange} />} label="Encrypt connection" />
              <FormControlLabel control={<Checkbox name="trustServerCertificate" checked={f.values.trustServerCertificate} onChange={f.handleChange} />} label="Trust server certificate" />
            </Grid>
            <Grid size={{ xs: 12, sm: 6 }}>
              <Autocomplete
                multiple
                options={RoleNames.filter((r) => r !== 'SuperAdmin' && r !== 'Admin')}
                value={f.values.allowedRoles}
                onChange={(_, v) => void f.setFieldValue('allowedRoles', v)}
                renderInput={(params) => <TextField {...params} label="Roles allowed to query" helperText="Administrators always have access." />}
              />
            </Grid>
          </Grid>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={f.isSubmitting}>{isNew ? 'Create' : 'Save changes'}</Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
