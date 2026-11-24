import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';
import { CssBaseline, ThemeProvider, useMediaQuery, type PaletteMode } from '@mui/material';
import { buildTheme } from './theme';

const STORAGE_KEY = 'aiqg.colorMode';
const ColorModeContext = createContext<{ mode: PaletteMode; toggle: () => void }>({ mode: 'light', toggle: () => undefined });

function readStored(): PaletteMode | null {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    return v === 'dark' || v === 'light' ? v : null;
  } catch {
    return null;
  }
}

export function ColorModeProvider({ children }: { children: ReactNode }) {
  const prefersDark = useMediaQuery('(prefers-color-scheme: dark)');
  const [stored, setStored] = useState<PaletteMode | null>(readStored);
  const mode: PaletteMode = stored ?? (prefersDark ? 'dark' : 'light');
  const theme = useMemo(() => buildTheme(mode), [mode]);

  const value = useMemo(
    () => ({
      mode,
      toggle: () => {
        const next = mode === 'dark' ? 'light' : 'dark';
        setStored(next);
        try {
          localStorage.setItem(STORAGE_KEY, next);
        } catch {
          /* storage unavailable */
        }
      },
    }),
    [mode],
  );

  return (
    <ColorModeContext.Provider value={value}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        {children}
      </ThemeProvider>
    </ColorModeContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export const useColorMode = () => useContext(ColorModeContext);
