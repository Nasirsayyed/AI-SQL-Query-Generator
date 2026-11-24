import { Alert, Box, Chip, Stack } from '@mui/material';
import { Clock, Database, Rows3, CalendarClock } from 'lucide-react';
import type { QueryResult } from '../api/types';
import { formatDateTime, formatDuration, formatNumber } from '../utils/format';

export function ExecutionInfo({ result }: { result: QueryResult }) {
  return (
    <Box sx={{ mb: 1.5 }}>
      <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1 }}>
        <Chip icon={<Clock size={14} />} label={formatDuration(result.durationMs)} size="small" variant="outlined" />
        <Chip icon={<Rows3 size={14} />} label={`${formatNumber(result.rowCount)} rows`} size="small" variant="outlined" />
        {result.isTruncated && <Chip color="warning" label={`Limited to ${formatNumber(result.maxRows)} rows`} size="small" />}
        <Chip icon={<Database size={14} />} label={result.connectionName} size="small" variant="outlined" />
        <Chip icon={<CalendarClock size={14} />} label={formatDateTime(result.executedAt)} size="small" variant="outlined" />
      </Stack>
      {result.isTruncated && (
        <Alert severity="warning" sx={{ mt: 1 }}>
          Only the first {formatNumber(result.maxRows)} rows were returned. Add filters, TOP or aggregation to see complete results.
        </Alert>
      )}
    </Box>
  );
}
