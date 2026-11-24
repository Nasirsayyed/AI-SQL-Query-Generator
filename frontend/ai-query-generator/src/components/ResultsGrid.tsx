import { useMemo } from 'react';
import { Box, Button, Stack, Tooltip, Typography } from '@mui/material';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { FileSpreadsheet, FileText } from 'lucide-react';
import type { CellValue, QueryResult } from '../api/types';
import { exportCsv, exportExcel } from '../utils/export';
import { isDateColumn, isNumericColumn } from '../utils/chart';
import { EmptyState } from './EmptyState';
import { useNotify } from './Notifier';

const ROW_NUMBER_FIELD = '__row';

function display(value: CellValue, dataType: string): string {
  if (value === null) return 'NULL';
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  if (typeof value === 'string' && /^datetime/i.test(dataType)) return value.replace('T', ' ').replace(/\.0+$/, '');
  if (typeof value === 'number' && !Number.isInteger(value)) return value.toLocaleString(undefined, { maximumFractionDigits: 6 });
  return String(value);
}

/** Result grid with sorting, filtering, quick search, column visibility/resizing, pagination and CSV/Excel export. */
export function ResultsGrid({ result, title, canExport }: { result: QueryResult; title: string; canExport: boolean }) {
  const notify = useNotify();

  const columns = useMemo<GridColDef[]>(() => [
    { field: ROW_NUMBER_FIELD, headerName: '#', width: 64, sortable: false, filterable: false, disableColumnMenu: true, align: 'right', headerAlign: 'right' },
    ...result.columns.map((c, i): GridColDef => {
      const numeric = isNumericColumn(c);
      return {
        field: `c${i}`,
        headerName: c.name,
        description: `${c.name} (${c.dataType})`,
        renderHeader: () => (
          <Box sx={{ lineHeight: 1.2 }}>
            <Typography variant="body2" sx={{ fontWeight: 600 }}>{c.name}</Typography>
            <Typography variant="caption" color="text.secondary">{c.dataType}</Typography>
          </Box>
        ),
        minWidth: 110,
        flex: 1,
        type: numeric ? 'number' : 'string',
        align: numeric ? 'right' : 'left',
        headerAlign: numeric ? 'right' : 'left',
        valueGetter: (value: CellValue) => (numeric && typeof value === 'string' ? Number(value) : value),
        renderCell: (params) => {
          const raw = result.rows[params.row[ROW_NUMBER_FIELD] - 1]?.[i] ?? null;
          const text = display(raw, c.dataType);
          return (
            <Tooltip title={text.length > 60 ? text : ''} placement="bottom-start">
              <span style={{ opacity: raw === null ? 0.5 : 1, fontStyle: raw === null ? 'italic' : 'normal', fontVariantNumeric: numeric || isDateColumn(c) ? 'tabular-nums' : undefined }}>
                {text}
              </span>
            </Tooltip>
          );
        },
      };
    }),
  ], [result]);

  const rows = useMemo(
    () => result.rows.map((r, idx) => Object.fromEntries([[ 'id', idx ], [ROW_NUMBER_FIELD, idx + 1], ...r.map((v, i) => [`c${i}`, v])])),
    [result],
  );

  if (result.rowCount === 0) return <EmptyState title="No rows returned" description="The query ran successfully but matched no data." />;

  const doExport = async (kind: 'csv' | 'xlsx') => {
    try {
      if (kind === 'csv') exportCsv(title, result.columns, result.rows);
      else await exportExcel(title, result.columns, result.rows);
    } catch {
      notify('Export failed. Please try again.', 'error');
    }
  };

  return (
    <Box>
      {canExport && (
        <Stack direction="row" spacing={1} sx={{ mb: 1, justifyContent: 'flex-end' }}>
          <Button size="small" startIcon={<FileText size={16} />} onClick={() => void doExport('csv')}>Export CSV</Button>
          <Button size="small" startIcon={<FileSpreadsheet size={16} />} onClick={() => void doExport('xlsx')}>Export Excel</Button>
        </Stack>
      )}
      <Box sx={{ height: 520, width: '100%' }}>
        <DataGrid
          rows={rows}
          columns={columns}
          density="compact"
          columnHeaderHeight={52}
          pageSizeOptions={[25, 50, 100]}
          initialState={{ pagination: { paginationModel: { pageSize: 25 } } }}
          showToolbar
          slotProps={{ toolbar: { showQuickFilter: true, csvOptions: { disableToolbarButton: true }, printOptions: { disableToolbarButton: true } } }}
          disableRowSelectionOnClick
          label="Query results"
          disableVirtualization={import.meta.env.MODE === 'test'}
          getCellClassName={() => 'aiqg-cell'}
          sx={{ '& .aiqg-cell': { userSelect: 'text' } }}
        />
      </Box>
    </Box>
  );
}
