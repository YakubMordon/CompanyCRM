import React, { useState, useEffect, useCallback, useMemo } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import ToggleButton from '@mui/material/ToggleButton';
import TextField from '@mui/material/TextField';
import InputAdornment from '@mui/material/InputAdornment';
import CircularProgress from '@mui/material/CircularProgress';
import ViewKanbanIcon from '@mui/icons-material/ViewKanban';
import TableRowsIcon from '@mui/icons-material/TableRows';
import AddIcon from '@mui/icons-material/Add';
import SearchIcon from '@mui/icons-material/Search';
import RefreshIcon from '@mui/icons-material/Refresh';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import StopIcon from '@mui/icons-material/Stop';
import AccessTimeIcon from '@mui/icons-material/AccessTime';
import Divider from '@mui/material/Divider';
import Chip from '@mui/material/Chip';
import Paper from '@mui/material/Paper';
import { CreateWorkItemDto, TimeEntryDto, UpdateWorkItemDto, WorkItemDto, WorkItemPriority, WorkItemStatus, WorkItemType } from '../types';
import { workItemsApi } from '../services/workItemsApi';
import { useProject } from '../contexts/ProjectContext';
import { useSignalR } from '../contexts/SignalRContext';
import { KanbanBoard } from '../components/workspace/KanbanBoard';
import { BacklogDataGrid } from '../components/workspace/BacklogDataGrid';
import { WorkItemDialog } from '../components/workspace/WorkItemDialog';
import { WorkItemDrawer } from '../components/workspace/WorkItemDrawer';
import { normalizeWorkItemDto } from '../components/workspace/workItemHelpers';

export const WorkspacePage: React.FC = () => {
  const { activeProject } = useProject();
  const { subscribeToWorkItemUpdates } = useSignalR();

  const [viewMode, setViewMode] = useState<'kanban' | 'backlog'>('kanban');
  const [workItems, setWorkItems] = useState<WorkItemDto[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [searchQuery, setSearchQuery] = useState<string>('');
  const [selectedTypeFilter, setSelectedTypeFilter] = useState<string>('ALL');
  const [selectedPriorityFilter, setSelectedPriorityFilter] = useState<string>('ALL');

  // Dialog & Drawer state
  const [isDialogOpen, setIsDialogOpen] = useState<boolean>(false);
  const [editingItem, setEditingItem] = useState<WorkItemDto | null>(null);
  const [selectedDrawerItem, setSelectedDrawerItem] = useState<WorkItemDto | null>(null);

  // Active timer state
  const [activeTimer, setActiveTimer] = useState<TimeEntryDto | null>(null);

  const fetchActiveTimer = useCallback(async () => {
    try {
      const timer = await workItemsApi.getActiveTimer();
      setActiveTimer(timer);
    } catch {
      setActiveTimer(null);
    }
  }, []);

  const loadWorkItems = useCallback(async () => {
    if (!activeProject) return;
    try {
      setIsLoading(true);
      const res = await workItemsApi.getWorkItems({
        projectId: activeProject.id,
        pageSize: 100,
      });
      setWorkItems(res.items);
    } catch (err) {
      console.error('Failed to load work items:', err);
    } finally {
      setIsLoading(false);
    }
  }, [activeProject]);

  useEffect(() => {
    loadWorkItems();
    fetchActiveTimer();
  }, [loadWorkItems, fetchActiveTimer]);

  // Subscribe to SignalR WorkItemUpdated
  useEffect(() => {
    const unsubscribe = subscribeToWorkItemUpdates((rawUpdatedItem) => {
      const updatedItem = normalizeWorkItemDto(rawUpdatedItem);
      setWorkItems((prev) => {
        const index = prev.findIndex((i) => i.id === updatedItem.id);
        if (index >= 0) {
          const next = [...prev];
          next[index] = updatedItem;
          return next;
        } else if (activeProject && updatedItem.projectId === activeProject.id) {
          return [updatedItem, ...prev];
        }
        return prev;
      });

      setSelectedDrawerItem((prev) => (prev?.id === updatedItem.id ? updatedItem : prev));
      fetchActiveTimer();
    });

    return unsubscribe;
  }, [subscribeToWorkItemUpdates, activeProject, fetchActiveTimer]);

  const handleTransitionStatus = async (
    id: string,
    targetStatus: WorkItemStatus,
    newIndex?: number
  ) => {
    // Optimistic update
    setWorkItems((prev) =>
      prev.map((item) =>
        item.id === id ? { ...item, status: targetStatus, boardPosition: newIndex ?? item.boardPosition } : item
      )
    );

    try {
      const updated = await workItemsApi.transitionStatus(id, {
        status: targetStatus,
        boardPosition: newIndex,
      });
      setWorkItems((prev) => prev.map((item) => (item.id === id ? updated : item)));
    } catch (err) {
      loadWorkItems(); // Rollback on error
      throw err;
    }
  };

  const handleCreateOrUpdate = async (dto: CreateWorkItemDto, id?: string) => {
    if (id) {
      const updateDto: UpdateWorkItemDto = {
        title: dto.title,
        description: dto.description,
        type: dto.type,
        priority: dto.priority,
        assigneeId: dto.assigneeId,
        reviewerId: dto.reviewerId,
        estimatedHours: dto.estimatedHours,
        storyPoints: dto.storyPoints,
        sprintName: dto.sprintName,
      };
      const updated = await workItemsApi.updateWorkItem(id, updateDto);
      setWorkItems((prev) => prev.map((item) => (item.id === id ? updated : item)));
    } else {
      const created = await workItemsApi.createWorkItem(dto);
      setWorkItems((prev) => [created, ...prev]);
    }
  };

  const handleStartTimer = async (item: WorkItemDto) => {
    try {
      await workItemsApi.startTimer(item.id, { isBillable: true });
      fetchActiveTimer();
      const updated = await workItemsApi.getWorkItemById(item.id);
      setWorkItems((prev) => prev.map((i) => (i.id === item.id ? updated : i)));
    } catch (err) {
      console.error('Failed to start timer:', err);
    }
  };

  const handleStopTimer = async (item: WorkItemDto) => {
    try {
      await workItemsApi.stopTimer(item.id, { isBillable: true });
      fetchActiveTimer();
      const updated = await workItemsApi.getWorkItemById(item.id);
      setWorkItems((prev) => prev.map((i) => (i.id === item.id ? updated : i)));
    } catch (err) {
      console.error('Failed to stop timer:', err);
    }
  };

  const filteredItems = useMemo(() => {
    return workItems.filter((item) => {
      if (selectedTypeFilter !== 'ALL' && item.type !== selectedTypeFilter) {
        return false;
      }
      if (selectedPriorityFilter !== 'ALL' && item.priority !== selectedPriorityFilter) {
        return false;
      }
      if (searchQuery.trim()) {
        const q = searchQuery.toLowerCase();
        const matchesQuery =
          item.code.toLowerCase().includes(q) ||
          item.title.toLowerCase().includes(q) ||
          (item.assigneeName && item.assigneeName.toLowerCase().includes(q));
        if (!matchesQuery) return false;
      }
      return true;
    });
  }, [workItems, searchQuery, selectedTypeFilter, selectedPriorityFilter]);

  if (!activeProject) {
    return (
      <Box sx={{ p: 4, textAlign: 'center' }}>
        <Typography variant="h6" color="text.secondary">
          Please select or create an active project to view work items.
        </Typography>
      </Box>
    );
  }

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {/* Top Toolbar */}
      <Box
        sx={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 2,
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="h5" sx={{ fontWeight: 700 }}>
            {activeProject.name}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            ({filteredItems.length} items)
          </Typography>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <TextField
            size="small"
            placeholder="Search code, title, assignee..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            InputProps={{
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            }}
            sx={{ width: { xs: 180, sm: 260 } }}
          />

          <ToggleButtonGroup
            value={viewMode}
            exclusive
            onChange={(_e, val) => val && setViewMode(val)}
            size="small"
          >
            <ToggleButton value="kanban" aria-label="Kanban View">
              <Tooltip title="Kanban Board View">
                <ViewKanbanIcon fontSize="small" />
              </Tooltip>
            </ToggleButton>
            <ToggleButton value="backlog" aria-label="Backlog DataGrid View">
              <Tooltip title="Backlog DataGrid View">
                <TableRowsIcon fontSize="small" />
              </Tooltip>
            </ToggleButton>
          </ToggleButtonGroup>

          <Tooltip title="Refresh work items">
            <IconButton onClick={loadWorkItems} size="small">
              <RefreshIcon fontSize="small" />
            </IconButton>
          </Tooltip>

          <Button
            variant="contained"
            startIcon={<AddIcon />}
            onClick={() => {
              setEditingItem(null);
              setIsDialogOpen(true);
            }}
            sx={{ fontWeight: 600, whiteSpace: 'nowrap' }}
          >
            New Work Item
          </Button>
        </Box>
      </Box>

      {/* Active Timer Banner */}
      {activeTimer && activeTimer.isRunning && (
        <Paper
          variant="outlined"
          sx={{
            p: 1.5,
            px: 2,
            bgcolor: 'primary.main',
            color: 'primary.contrastText',
            borderRadius: 2,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: 1.5,
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
            <AccessTimeIcon />
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              Live timer running for work item {activeTimer.workItemCode || activeTimer.workItemId}
            </Typography>
            <Chip
              label={activeTimer.isBillable ? 'Billable' : 'Non-Billable'}
              size="small"
              sx={{ bgcolor: 'rgba(255,255,255,0.2)', color: 'inherit', fontWeight: 600 }}
            />
          </Box>
          <Button
            size="small"
            variant="contained"
            color="secondary"
            startIcon={<StopIcon />}
            onClick={async () => {
              try {
                await workItemsApi.stopTimer(activeTimer.workItemId, { isBillable: true });
                fetchActiveTimer();
                loadWorkItems();
              } catch (err) {
                console.error('Failed to stop timer:', err);
              }
            }}
            sx={{ fontWeight: 700 }}
          >
            Stop Timer
          </Button>
        </Paper>
      )}

      {/* Filter Row */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Typography variant="caption" sx={{ fontWeight: 700, color: 'text.secondary', mr: 0.5 }}>
          Type:
        </Typography>
        {(['ALL', 'Bug', 'Feature', 'Task', 'Improvement'] as const).map((t) => (
          <Chip
            key={t}
            label={t}
            size="small"
            clickable
            color={selectedTypeFilter === t ? 'primary' : 'default'}
            variant={selectedTypeFilter === t ? 'filled' : 'outlined'}
            onClick={() => setSelectedTypeFilter(t)}
            sx={{ fontWeight: 600 }}
          />
        ))}

        <Divider orientation="vertical" flexItem sx={{ mx: 1 }} />

        <Typography variant="caption" sx={{ fontWeight: 700, color: 'text.secondary', mr: 0.5 }}>
          Priority:
        </Typography>
        {(['ALL', 'Low', 'Medium', 'High', 'Critical'] as const).map((p) => (
          <Chip
            key={p}
            label={p}
            size="small"
            clickable
            color={selectedPriorityFilter === p ? 'primary' : 'default'}
            variant={selectedPriorityFilter === p ? 'filled' : 'outlined'}
            onClick={() => setSelectedPriorityFilter(p)}
            sx={{ fontWeight: 600 }}
          />
        ))}

        {(selectedTypeFilter !== 'ALL' || selectedPriorityFilter !== 'ALL' || searchQuery) && (
          <Button
            size="small"
            variant="text"
            onClick={() => {
              setSelectedTypeFilter('ALL');
              setSelectedPriorityFilter('ALL');
              setSearchQuery('');
            }}
            sx={{ fontSize: '0.75rem', textTransform: 'none', ml: 'auto' }}
          >
            Clear Filters
          </Button>
        )}
      </Box>

      {/* Main Content Area */}
      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 8 }}>
          <CircularProgress />
        </Box>
      ) : viewMode === 'kanban' ? (
        <KanbanBoard
          workItems={filteredItems}
          onTransitionStatus={handleTransitionStatus}
          onCardClick={(item) => setSelectedDrawerItem(item)}
          activeTimerWorkItemId={activeTimer?.isRunning ? activeTimer.workItemId : null}
        />
      ) : (
        <BacklogDataGrid
          workItems={filteredItems}
          onRowClick={(item) => setSelectedDrawerItem(item)}
          onStartTimer={handleStartTimer}
          onStopTimer={handleStopTimer}
          activeTimerWorkItemId={activeTimer?.isRunning ? activeTimer.workItemId : null}
        />
      )}

      {/* Create / Edit Dialog */}
      <WorkItemDialog
        open={isDialogOpen}
        onClose={() => setIsDialogOpen(false)}
        onSubmit={handleCreateOrUpdate}
        projectId={activeProject.id}
        itemToEdit={editingItem}
      />

      {/* Detail Drawer */}
      <WorkItemDrawer
        item={selectedDrawerItem}
        open={Boolean(selectedDrawerItem)}
        onClose={() => setSelectedDrawerItem(null)}
        onItemUpdated={(updated) => {
          setSelectedDrawerItem(updated);
          setWorkItems((prev) => prev.map((i) => (i.id === updated.id ? updated : i)));
        }}
        onEditItem={(item) => {
          setEditingItem(item);
          setIsDialogOpen(true);
        }}
        activeTimer={activeTimer}
        onRefreshActiveTimer={fetchActiveTimer}
      />
    </Box>
  );
};
