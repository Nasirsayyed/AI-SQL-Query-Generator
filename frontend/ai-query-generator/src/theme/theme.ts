import { createTheme, type PaletteMode } from '@mui/material/styles';

export function buildTheme(mode: PaletteMode) {
  const dark = mode === 'dark';
  return createTheme({
    palette: {
      mode,
      primary: { main: dark ? '#5aa9ff' : '#0f6cbd' },
      secondary: { main: dark ? '#b794f6' : '#6b4fbb' },
      background: dark ? { default: '#0f1218', paper: '#171b23' } : { default: '#f4f6f9', paper: '#ffffff' },
      success: { main: dark ? '#4cc38a' : '#1b7f4b' },
      warning: { main: dark ? '#f2b84b' : '#b26a00' },
      error: { main: dark ? '#ff6b6b' : '#c62828' },
      divider: dark ? 'rgba(255,255,255,0.10)' : 'rgba(15,23,42,0.10)',
    },
    shape: { borderRadius: 8 },
    typography: {
      fontFamily: '"Segoe UI", "Inter", system-ui, -apple-system, Roboto, sans-serif',
      h4: { fontWeight: 600, fontSize: '1.6rem' },
      h5: { fontWeight: 600, fontSize: '1.3rem' },
      h6: { fontWeight: 600, fontSize: '1.05rem' },
      button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
      MuiPaper: { defaultProps: { elevation: 0 }, styleOverrides: { root: { backgroundImage: 'none' } } },
      MuiCard: { styleOverrides: { root: ({ theme }) => ({ border: `1px solid ${theme.palette.divider}` }) } },
      MuiButton: { defaultProps: { disableElevation: true } },
      MuiTooltip: { defaultProps: { arrow: true } },
      MuiTextField: { defaultProps: { size: 'small' } },
      MuiSelect: { defaultProps: { size: 'small' } },
    },
  });
}
