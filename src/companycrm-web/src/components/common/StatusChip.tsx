import React from 'react';
import Chip from '@mui/material/Chip';
import { ReviewStatus, WorkItemPriority, WorkItemStatus, WorkItemType } from '../../types';
import LockIcon from '@mui/icons-material/Lock';
import BugReportIcon from '@mui/icons-material/BugReport';
import AssignmentIcon from '@mui/icons-material/Assignment';
import MenuBookIcon from '@mui/icons-material/MenuBook';

interface StatusChipProps {
  status?: WorkItemStatus;
  reviewStatus?: ReviewStatus;
  priority?: WorkItemPriority;
  type?: WorkItemType;
  size?: 'small' | 'medium';
}

export const StatusChip: React.FC<StatusChipProps> = ({
  status,
  reviewStatus,
  priority,
  type,
  size = 'small',
}) => {
  if (status) {
    let color: 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' =
      'default';
    let icon: React.ReactElement | undefined = undefined;

    switch (status) {
      case 'Draft':
        color = 'default';
        break;
      case 'PendingReview':
        color = 'warning';
        icon = <LockIcon fontSize="small" />;
        break;
      case 'ClarificationRequested':
        color = 'warning';
        icon = <LockIcon fontSize="small" />;
        break;
      case 'Denied':
        color = 'error';
        icon = <LockIcon fontSize="small" />;
        break;
      case 'Backlog':
        color = 'info';
        break;
      case 'Todo':
        color = 'primary';
        break;
      case 'InProgress':
        color = 'warning';
        break;
      case 'InVerify':
        color = 'secondary';
        break;
      case 'Done':
        color = 'success';
        break;
    }

    return (
      <Chip
        label={status}
        color={color}
        size={size}
        icon={icon}
        sx={{ fontWeight: 600, fontSize: size === 'small' ? '0.75rem' : '0.85rem' }}
      />
    );
  }

  if (reviewStatus) {
    let color: 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' =
      'default';
    switch (reviewStatus) {
      case 'Pending':
        color = 'warning';
        break;
      case 'Approved':
        color = 'success';
        break;
      case 'ClarificationNeeded':
        color = 'warning';
        break;
      case 'Denied':
        color = 'error';
        break;
      case 'NotRequired':
        color = 'default';
        break;
    }
    return (
      <Chip
        label={reviewStatus}
        color={color}
        size={size}
        sx={{ fontWeight: 600, fontSize: size === 'small' ? '0.75rem' : '0.85rem' }}
      />
    );
  }

  if (priority) {
    let color: 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' =
      'default';
    switch (priority) {
      case 'Low':
        color = 'default';
        break;
      case 'Medium':
        color = 'info';
        break;
      case 'High':
        color = 'warning';
        break;
      case 'Critical':
        color = 'error';
        break;
    }
    return (
      <Chip
        label={priority}
        color={color}
        size={size}
        variant="outlined"
        sx={{ fontWeight: 600, fontSize: size === 'small' ? '0.75rem' : '0.85rem' }}
      />
    );
  }

  if (type) {
    let icon = <AssignmentIcon fontSize="small" />;
    let color: 'default' | 'primary' | 'secondary' | 'error' | 'info' | 'success' | 'warning' =
      'primary';

    if (type === 'Bug') {
      icon = <BugReportIcon fontSize="small" />;
      color = 'error';
    } else if (type === 'Story') {
      icon = <MenuBookIcon fontSize="small" />;
      color = 'secondary';
    }

    return (
      <Chip
        icon={icon}
        label={type}
        color={color}
        size={size}
        sx={{ fontWeight: 600, fontSize: size === 'small' ? '0.75rem' : '0.85rem' }}
      />
    );
  }

  return null;
};
