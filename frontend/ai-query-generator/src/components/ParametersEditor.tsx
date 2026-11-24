import { Box, IconButton, MenuItem, TextField, Tooltip, Typography } from '@mui/material';
import { Plus, Trash2 } from 'lucide-react';
import type { QueryParameter, SqlParameterType } from '../api/types';

const TYPES: SqlParameterType[] = ['String', 'Int', 'BigInt', 'Decimal', 'Bool', 'Date', 'DateTime', 'Guid'];

export function ParametersEditor({ parameters, onChange, readOnly }: { parameters: QueryParameter[]; onChange: (p: QueryParameter[]) => void; readOnly?: boolean }) {
  const update = (index: number, patch: Partial<QueryParameter>) => onChange(parameters.map((p, i) => (i === index ? { ...p, ...patch } : p)));

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle2" sx={{ flex: 1 }}>
          Parameters
        </Typography>
        {!readOnly && (
          <Tooltip title="Add parameter">
            <IconButton size="small" onClick={() => onChange([...parameters, { name: `Param${parameters.length + 1}`, type: 'String', value: '' }])} aria-label="Add parameter">
              <Plus size={16} />
            </IconButton>
          </Tooltip>
        )}
      </Box>
      {parameters.length === 0 && (
        <Typography variant="body2" color="text.secondary">
          No parameters. Reference values as @Name in the SQL.
        </Typography>
      )}
      {parameters.map((p, i) => (
        <Box key={i} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: '1fr 120px 1.5fr auto' }, gap: 1, mb: 1, alignItems: 'center' }}>
          <TextField label="Name" value={p.name} onChange={(e) => update(i, { name: e.target.value.replace(/[^\w@]/g, '') })} disabled={readOnly} />
          <TextField select label="Type" value={p.type} onChange={(e) => update(i, { type: e.target.value as SqlParameterType })} disabled={readOnly}>
            {TYPES.map((t) => (
              <MenuItem key={t} value={t}>
                {t}
              </MenuItem>
            ))}
          </TextField>
          <TextField
            label="Value"
            value={p.value ?? ''}
            onChange={(e) => update(i, { value: e.target.value })}
            helperText={p.description ?? undefined}
            type={p.type === 'Date' ? 'date' : p.type === 'DateTime' ? 'datetime-local' : 'text'}
            slotProps={{ inputLabel: { shrink: true } }}
          />
          {!readOnly && (
            <IconButton size="small" onClick={() => onChange(parameters.filter((_, j) => j !== i))} aria-label={`Remove parameter ${p.name}`}>
              <Trash2 size={16} />
            </IconButton>
          )}
        </Box>
      ))}
    </Box>
  );
}
