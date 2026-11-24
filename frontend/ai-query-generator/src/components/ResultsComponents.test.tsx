import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders, sampleResult } from '../test/utils';
import { ResultsGrid } from './ResultsGrid';
import { ResultChart } from './ResultChart';
import { ExecutionInfo } from './ExecutionInfo';

vi.mock('./EChart', () => ({ EChart: ({ ariaLabel }: { ariaLabel: string }) => <div role="img" aria-label={ariaLabel} /> }));

describe('ResultsGrid', () => {
  it('renders headers with data types, rows, nulls and export buttons', () => {
    renderWithProviders(<ResultsGrid result={sampleResult} title="Top" canExport />);
    const grid = screen.getByRole('grid');
    expect(within(grid).getByText('CustomerName')).toBeInTheDocument();
    expect(within(grid).getByText('decimal')).toBeInTheDocument();
    expect(within(grid).getByText('Liam Johnson')).toBeInTheDocument();
    expect(within(grid).getByText('NULL')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /export csv/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /export excel/i })).toBeInTheDocument();
  });

  it('hides export for roles without the permission', () => {
    renderWithProviders(<ResultsGrid result={sampleResult} title="Top" canExport={false} />);
    expect(screen.queryByRole('button', { name: /export csv/i })).not.toBeInTheDocument();
  });

  it('shows an empty state for zero rows', () => {
    renderWithProviders(<ResultsGrid result={{ ...sampleResult, rows: [], rowCount: 0 }} title="Top" canExport />);
    expect(screen.getByText('No rows returned')).toBeInTheDocument();
  });
});

describe('ExecutionInfo', () => {
  it('flags limited results', () => {
    renderWithProviders(<ExecutionInfo result={{ ...sampleResult, isTruncated: true, maxRows: 1000 }} />);
    expect(screen.getByText(/Limited to 1,000 rows/)).toBeInTheDocument();
    expect(screen.getByText('42 ms')).toBeInTheDocument();
  });
});

describe('ResultChart', () => {
  it('uses a valid AI suggestion and lets the user change the chart type', async () => {
    renderWithProviders(<ResultChart result={sampleResult} title="Top" suggestion={{ recommended: true, chartType: 'bar', xAxis: 'CustomerName', yAxis: 'TotalSales', aggregation: 'none', source: 'ai' }} />);
    expect(screen.getByRole('img', { name: 'bar chart of TotalSales by CustomerName' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('combobox', { name: /chart type/i }));
    await userEvent.click(screen.getByRole('option', { name: 'Pie' }));
    expect(screen.getByRole('img', { name: 'pie chart of TotalSales by CustomerName' })).toBeInTheDocument();
  });

  it('warns when the y-axis is not numeric', async () => {
    renderWithProviders(<ResultChart result={sampleResult} title="Top" />);
    await userEvent.click(screen.getByRole('combobox', { name: /y-axis/i }));
    await userEvent.click(screen.getByRole('option', { name: 'CustomerName' }));
    expect(screen.getByText(/Y-axis must be numeric/)).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });
});
