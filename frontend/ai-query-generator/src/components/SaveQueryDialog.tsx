import { Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Stack, TextField, Autocomplete } from '@mui/material';
import { useFormik } from 'formik';
import * as yup from 'yup';
import { useQuery } from '@tanstack/react-query';
import { savedQueriesApi } from '../api/endpoints';
import { ErrorAlert } from './ErrorAlert';

export interface SaveQueryValues {
  queryTitle: string;
  description: string;
  category: string;
  isShared: boolean;
}

const schema = yup.object({
  queryTitle: yup.string().trim().required('Title is required').max(200),
  description: yup.string().max(2000),
  category: yup.string().max(100),
  isShared: yup.boolean(),
});

export function SaveQueryDialog({ open, initialTitle, canShare, onClose, onSave, error }: {
  open: boolean;
  initialTitle: string;
  canShare: boolean;
  onClose: () => void;
  onSave: (values: SaveQueryValues) => Promise<void>;
  error?: unknown;
}) {
  const { data: categories = [] } = useQuery({ queryKey: ['saved', 'categories'], queryFn: savedQueriesApi.categories, enabled: open });
  const formik = useFormik<SaveQueryValues>({
    initialValues: { queryTitle: initialTitle, description: '', category: '', isShared: false },
    enableReinitialize: true,
    validationSchema: schema,
    onSubmit: onSave,
  });

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <form onSubmit={formik.handleSubmit} noValidate>
        <DialogTitle>Save query</DialogTitle>
        <DialogContent>
          <ErrorAlert error={error} />
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField
              label="Title"
              name="queryTitle"
              value={formik.values.queryTitle}
              onChange={formik.handleChange}
              onBlur={formik.handleBlur}
              error={formik.touched.queryTitle && !!formik.errors.queryTitle}
              helperText={formik.touched.queryTitle && formik.errors.queryTitle}
              required
              autoFocus
            />
            <TextField label="Description" name="description" value={formik.values.description} onChange={formik.handleChange} multiline minRows={2} />
            <Autocomplete
              freeSolo
              options={categories}
              inputValue={formik.values.category}
              onInputChange={(_, v) => void formik.setFieldValue('category', v)}
              renderInput={(params) => <TextField {...params} label="Category" />}
            />
            {canShare && (
              <FormControlLabel control={<Checkbox name="isShared" checked={formik.values.isShared} onChange={formik.handleChange} />} label="Share with users who can view shared reports" />
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={formik.isSubmitting}>Save</Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
