import React from 'react';
import Dialog, { DialogProps } from '@mui/material/Dialog';
import DialogContent, { DialogContentProps } from '@mui/material/DialogContent';

export interface FormDialogProps extends DialogProps {
  dialogContentProps?: DialogContentProps;
}

export const FormDialog: React.FC<FormDialogProps> = ({
  children,
  dialogContentProps,
  sx,
  ...props
}) => {
  return (
    <Dialog
      {...props}
      sx={{
        '& .MuiDialogContent-root': {
          pt: 3, // Provide ample top padding so floating labels on first TextFields don't get clipped
        },
        ...sx,
      }}
    >
      {children}
    </Dialog>
  );
};

export default FormDialog;
