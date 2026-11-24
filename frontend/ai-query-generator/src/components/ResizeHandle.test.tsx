import { useState } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ResizeHandle } from './ResizeHandle';

function Harness({ initial = 280, onChange }: { initial?: number; onChange?: (v: number) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <ResizeHandle
      value={value}
      min={140}
      max={600}
      step={20}
      ariaLabel="Resize"
      onChange={(v) => { setValue(v); onChange?.(v); }}
      onReset={() => setValue(280)}
    />
  );
}

describe('ResizeHandle', () => {
  it('is an accessible separator exposing its value range', () => {
    render(<Harness />);
    const handle = screen.getByRole('separator', { name: 'Resize' });
    expect(handle).toHaveAttribute('aria-orientation', 'horizontal');
    expect(handle).toHaveAttribute('aria-valuenow', '280');
    expect(handle).toHaveAttribute('aria-valuemin', '140');
    expect(handle).toHaveAttribute('aria-valuemax', '600');
  });

  it('resizes with the keyboard and clamps to the limits', async () => {
    render(<Harness />);
    const handle = screen.getByRole('separator');
    handle.focus();
    await userEvent.keyboard('{ArrowDown}{ArrowDown}');
    expect(handle).toHaveAttribute('aria-valuenow', '320');
    await userEvent.keyboard('{ArrowUp}');
    expect(handle).toHaveAttribute('aria-valuenow', '300');
    await userEvent.keyboard('{End}');
    expect(handle).toHaveAttribute('aria-valuenow', '600');
    await userEvent.keyboard('{PageDown}');
    expect(handle).toHaveAttribute('aria-valuenow', '600');
    await userEvent.keyboard('{Home}');
    expect(handle).toHaveAttribute('aria-valuenow', '140');
  });

  it('follows pointer drags and resets on double-click', () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);
    const handle = screen.getByRole('separator');
    fireEvent.pointerDown(handle, { button: 0, clientY: 500, pointerId: 1 });
    fireEvent.pointerMove(handle, { clientY: 600, pointerId: 1 });
    expect(handle).toHaveAttribute('aria-valuenow', '380');
    fireEvent.pointerMove(handle, { clientY: 100, pointerId: 1 });
    expect(handle).toHaveAttribute('aria-valuenow', '140');
    fireEvent.pointerUp(handle, { clientY: 100, pointerId: 1 });
    fireEvent.pointerMove(handle, { clientY: 700, pointerId: 1 });
    expect(handle).toHaveAttribute('aria-valuenow', '140'); // no longer dragging
    expect(document.body.style.userSelect).toBe('');

    fireEvent.doubleClick(handle);
    expect(handle).toHaveAttribute('aria-valuenow', '280');
    expect(onChange).toHaveBeenCalled();
  });
});
