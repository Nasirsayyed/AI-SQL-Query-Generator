import { useRef, type KeyboardEvent, type PointerEvent } from 'react';
import { Box, Tooltip } from '@mui/material';

export interface ResizeHandleProps {
  /** Current size (px) of the panel above the handle. */
  value: number;
  min: number;
  max: number;
  onChange: (value: number) => void;
  /** Called on double-click; typically restores the default size. */
  onReset?: () => void;
  /** Pixels moved per arrow-key press. */
  step?: number;
  ariaLabel: string;
}

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, Math.round(v)));

/**
 * Horizontal splitter. Drag (mouse, touch or pen) to resize the panel above it; Arrow Up/Down, Page Up/Down,
 * Home and End do the same from the keyboard; double-click resets.
 */
export function ResizeHandle({ value, min, max, onChange, onReset, step = 24, ariaLabel }: ResizeHandleProps) {
  const drag = useRef<{ startY: number; startValue: number } | null>(null);

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0) return;
    e.preventDefault();
    drag.current = { startY: e.clientY, startValue: value };
    e.currentTarget.setPointerCapture?.(e.pointerId);
    document.body.style.userSelect = 'none';
    document.body.style.cursor = 'row-resize';
  };

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    if (!drag.current) return;
    onChange(clamp(drag.current.startValue + (e.clientY - drag.current.startY), min, max));
  };

  const endDrag = (e: PointerEvent<HTMLDivElement>) => {
    if (!drag.current) return;
    drag.current = null;
    if (e.currentTarget.hasPointerCapture?.(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
    document.body.style.userSelect = '';
    document.body.style.cursor = '';
  };

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const next = {
      ArrowUp: value - step,
      ArrowDown: value + step,
      PageUp: value - step * 4,
      PageDown: value + step * 4,
      Home: min,
      End: max,
    }[e.key];
    if (next === undefined) return;
    e.preventDefault();
    onChange(clamp(next, min, max));
  };

  return (
    <Tooltip title="Drag to resize · double-click to reset" placement="top" enterDelay={600}>
      <Box
        role="separator"
        aria-orientation="horizontal"
        aria-label={ariaLabel}
        aria-valuenow={value}
        aria-valuemin={min}
        aria-valuemax={max}
        tabIndex={0}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        onKeyDown={onKeyDown}
        onDoubleClick={onReset}
        sx={{
          height: 14,
          my: 0.5,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'row-resize',
          touchAction: 'none',
          borderRadius: 1,
          outline: 'none',
          '&::before': { content: '""', position: 'absolute', left: 0, right: 0, height: '1px', bgcolor: 'divider' },
          position: 'relative',
          '&:hover .grip, &:focus-visible .grip, &:active .grip': { bgcolor: 'primary.main', width: 72 },
          '&:focus-visible': { boxShadow: (t) => `0 0 0 2px ${t.palette.primary.main}` },
        }}
      >
        <Box className="grip" sx={{ position: 'relative', width: 48, height: 5, borderRadius: 3, bgcolor: 'text.disabled', transition: 'all 120ms' }} />
      </Box>
    </Tooltip>
  );
}
