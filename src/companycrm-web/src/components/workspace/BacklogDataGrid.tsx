import React from 'react';
import { DataGrid, GridColDef, GridRenderCellParams } from '@mui/x-data-grid';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import StopIcon from '@mui/icons-material/Stop';
import VisibilityIcon from '@mui/icons-material/Visibility';
import Chip from '@mui/material/Chip';
import Avatar from '@mui/material/Avatar';
import { WorkItemDto } from '../../types';
import { StatusChip } from '../common/StatusChip';

interface BacklogDataGridProps {
  workItems: WorkItemDto[];
  onRowClick: (item: WorkItemDto) => void;
  onStartTimer: (item: WorkItemDto) => void;
  onStopTimer: (item: WorkItemDto) => void;
  activeTimerWorkItemId?: string | null;
}

export const BacklogDataGrid: React.FC<BacklogDataGridProps> = ({
  workItems,
  onRowClick,
  onStartTimer,
  onStopTimer,
  activeTimerWorkItemId,
}) => {
  const columns: GridColDef[] = [
    {
      field: 'code',
      headerName: 'Code',
      width: 110,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => (
        <Typography
          variant="body2"
          sx={{ fontWeight: 700, color: 'primary.main', cursor: 'pointer' }}
          onClick={() => onRowClick(params.row)}
        >
          {params.value}
        </Typography>
      ),
    },
    {
      field: 'title',
      headerName: 'Title',
      flex: 1,
      minWidth: 220,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => (
        <Typography
          variant="body2"
          sx={{
            fontWeight: 500,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            cursor: 'pointer',
          }}
          onClick={() => onRowClick(params.row)}
        >
          {params.value}
        </Typography>
      ),
    },
    {
      field: 'type',
      headerName: 'Type',
      width: 105,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => (
        <StatusChip type={params.row.type} size="small" />
      ),
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 130,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => (
        <StatusChip status={params.row.status} size="small" />
      ),
    },
    {
      field: 'priority',
      headerName: 'Priority',
      width: 110,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => (
        <StatusChip priority={params.row.priority} size="small" />
      ),
    },
    {
      field: 'assigneeName',
      headerName: 'Assignee',
      width: 150,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => {
        if (!params.value) {
          return (
            <Typography variant="caption" color="text.secondary">
              Unassigned
            </Typography>
          );
        }
        return (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Avatar sx={{ width: 22, height: 22, fontSize: 11, bgcolor: 'primary.dark' }}>
              {(params.value as string).charAt(0)}
            </Avatar>
            <Typography variant="body2" noWrap>
              {params.value}
            </Typography>
          </Box>
        );
      },
    },
    {
      field: 'reviewerName',
      headerName: 'Reviewer',
      width: 140,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => {
        if (!params.value) return <Typography variant="caption" color="text.secondary">-</Typography>;
        return (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
            <Typography variant="body2" noWrap>
              {params.value}
            </Typography>
            <StatusChip reviewStatus={params.row.reviewStatus} size="small" />
          </Box>
        );
      },
    },
    {
      field: 'storyPoints',
      headerName: 'SP',
      width: 70,
      type: 'number',
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => (
        <Typography variant="body2">{params.value != null ? params.value : '-'}</Typography>
      ),
    },
    {
      field: 'totalLoggedHours',
      headerName: 'Logged (h)',
      width: 100,
      type: 'number',
      valueGetter: (_value, row) =>
        row.totalLoggedMinutes ? (row.totalLoggedMinutes / 60.0).toFixed(1) : '0.0',
    },
    {
      field: 'billableHours',
      headerName: 'Billable (h)',
      width: 105,
      type: 'number',
      valueGetter: (_value, row) =>
        row.billableMinutes ? (row.billableMinutes / 60.0).toFixed(1) : '0.0',
    },
    {
      field: 'externalId',
      headerName: 'ADO Sync',
      width: 125,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => {
        if (!params.value) {
          return (
            <Chip label="Local" size="small" variant="outlined" sx={{ height: 20, fontSize: '0.65rem' }} />
          );
        }
        return (
          <Chip
            label={params.value}
            size="small"
            color="info"
            variant="outlined"
            sx={{ height: 20, fontSize: '0.65rem', fontWeight: 600 }}
          />
        );
      },
    },
    {
      field: 'actions',
      headerName: 'Actions',
      width: 110,
      sortable: false,
      renderCell: (params: GridRenderCellParams<WorkItemDto>) => {
        const isRunning = activeTimerWorkItemId === params.row.id;
        return (
          <Box sx={{ display: 'flex', alignItems: 'center' }}>
            {isRunning ? (
              <Tooltip title="Stop Timer">
                <IconButton
                  size="small"
                  color="error"
                  onClick={(e) => {
                    e.stopPropagation();
                    onStopTimer(params.row);
                  }}
                >
                  <StopIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            ) : (
              <Tooltip title="Start Live Timer">
                <IconButton
                  size="small"
                  color="success"
                  onClick={(e) => {
                    e.stopPropagation();
                    onStartTimer(params.row);
                  }}
                >
                  <PlayArrowIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            )}
            <Tooltip title="View Details">
              <IconButton
                size="small"
                color="primary"
                onClick={(e) => {
                  e.stopPropagation();
                  onRowClick(params.row);
                }}
              >
                <VisibilityIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Box>
        );
      },
    },
  ];

  return (
    <Box sx={{ height: 'calc(100vh - 210px)', width: '100%' }}>
      <DataGrid
        rows={workItems}
        columns={columns}
        pageSizeOptions={[10, 25, 50, 100]}
        initialState={{
          pagination: { paginationModel: { pageSize: 25 } },
        }}
        disableRowSelectionOnClick
        density="standard"
        sx={{
          borderRadius: 2,
          borderColor: 'divider',
          '& .MuiDataGrid-cell': {
            display: 'flex',
            alignItems: 'center',
          },
          '& .MuiDataGrid-columnHeaders': {
            bgcolor: 'action.hover',
            fontWeight: 700,
          },
        }}
      />
    </Box>
  );
};
