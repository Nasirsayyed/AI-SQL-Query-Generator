import { Alert, AlertTitle, Box, Chip, Stack } from '@mui/material';
import type { SqlValidationResponse } from '../api/types';

export function ValidationPanel({ validation, stale }: { validation: SqlValidationResponse | null; stale: boolean }) {
  if (!validation) return null;
  if (stale)
    return (
      <Alert severity="info" sx={{ mt: 1.5 }}>
        The SQL changed after the last validation. Validate it again before executing.
      </Alert>
    );
  return (
    <Box sx={{ mt: 1.5 }}>
      <Alert severity={validation.isValid ? 'success' : 'error'}>
        <AlertTitle>{validation.isValid ? 'Validated: read-only and authorised' : 'Validation failed'}</AlertTitle>
        {!validation.isValid && validation.message}
        {validation.errors.length > 0 && (
          <ul style={{ margin: '4px 0 0', paddingLeft: 18 }}>
            {validation.errors.map((e, i) => (
              <li key={i}>
                {e.message}
                {e.line ? ` (line ${e.line}, col ${e.column})` : ''}
              </li>
            ))}
          </ul>
        )}
        {validation.isValid && validation.referencedObjects.length > 0 && (
          <Stack direction="row" spacing={0.5} sx={{ mt: 0.5, flexWrap: 'wrap', gap: 0.5 }}>
            {validation.referencedObjects.map((o) => (
              <Chip key={o} size="small" label={o} variant="outlined" />
            ))}
          </Stack>
        )}
      </Alert>
      {validation.warnings.length > 0 && (
        <Alert severity="warning" sx={{ mt: 1 }}>
          {validation.warnings.map((w, i) => (
            <div key={i}>{w.message}</div>
          ))}
        </Alert>
      )}
    </Box>
  );
}
