import React, { createContext, useContext, useEffect, useState, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import {
  DailyPlanSubmittedDto,
  ReviewQueueEventDto,
  SyncEventDto,
  WorkItemDto,
} from '../types';
import { useAuth } from './AuthContext';
import { useProject } from './ProjectContext';
import { dispatchCallbackSafely } from './contextHelpers';
import { normalizeSyncEvent } from '../components/sync/syncHelpers';
import { normalizeWorkItemDto } from '../components/workspace/workItemHelpers';

export type SignalRStatus = 'Connecting' | 'Connected' | 'Reconnecting' | 'Disconnected';

interface SignalRContextType {
  status: SignalRStatus;
  lastWorkItemUpdated: WorkItemDto | null;
  lastReviewQueueEvent: ReviewQueueEventDto | null;
  lastSyncEvent: SyncEventDto | null;
  lastDailyPlanSubmitted: DailyPlanSubmittedDto | null;
  syncEvents: SyncEventDto[];
  subscribeToWorkItemUpdates: (callback: (item: WorkItemDto) => void) => () => void;
  subscribeToReviewQueueUpdates: (callback: (event: ReviewQueueEventDto) => void) => () => void;
  subscribeToSyncEvents: (callback: (event: SyncEventDto) => void) => () => void;
  subscribeToDailyPlanSubmitted: (callback: (event: DailyPlanSubmittedDto) => void) => () => void;
}

const SignalRContext = createContext<SignalRContextType | undefined>(undefined);

export const SignalRProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { token } = useAuth();
  const { activeProject } = useProject();
  const [status, setStatus] = useState<SignalRStatus>('Disconnected');
  const [lastWorkItemUpdated, setLastWorkItemUpdated] = useState<WorkItemDto | null>(null);
  const [lastReviewQueueEvent, setLastReviewQueueEvent] = useState<ReviewQueueEventDto | null>(null);
  const [lastSyncEvent, setLastSyncEvent] = useState<SyncEventDto | null>(null);
  const [lastDailyPlanSubmitted, setLastDailyPlanSubmitted] = useState<DailyPlanSubmittedDto | null>(null);
  const [syncEvents, setSyncEvents] = useState<SyncEventDto[]>([]);

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const activeProjectRef = useRef<string | null>(null);

  const workItemCallbacks = useRef<Set<(item: WorkItemDto) => void>>(new Set());
  const reviewQueueCallbacks = useRef<Set<(event: ReviewQueueEventDto) => void>>(new Set());
  const syncCallbacks = useRef<Set<(event: SyncEventDto) => void>>(new Set());
  const dailyPlanCallbacks = useRef<Set<(event: DailyPlanSubmittedDto) => void>>(new Set());

  const subscribeToWorkItemUpdates = useCallback((callback: (item: WorkItemDto) => void) => {
    workItemCallbacks.current.add(callback);
    return () => {
      workItemCallbacks.current.delete(callback);
    };
  }, []);

  const subscribeToReviewQueueUpdates = useCallback((callback: (event: ReviewQueueEventDto) => void) => {
    reviewQueueCallbacks.current.add(callback);
    return () => {
      reviewQueueCallbacks.current.delete(callback);
    };
  }, []);

  const subscribeToSyncEvents = useCallback((callback: (event: SyncEventDto) => void) => {
    syncCallbacks.current.add(callback);
    return () => {
      syncCallbacks.current.delete(callback);
    };
  }, []);

  const subscribeToDailyPlanSubmitted = useCallback((callback: (event: DailyPlanSubmittedDto) => void) => {
    dailyPlanCallbacks.current.add(callback);
    return () => {
      dailyPlanCallbacks.current.delete(callback);
    };
  }, []);

  useEffect(() => {
    if (!token) {
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
        setStatus('Disconnected');
      }
      return;
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/crm', {
        accessTokenFactory: () => localStorage.getItem('auth_token') || '',
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    connectionRef.current = connection;
    setStatus('Connecting');

    connection.onreconnecting(() => {
      setStatus('Reconnecting');
    });

    connection.onreconnected(() => {
      setStatus('Connected');
      if (activeProjectRef.current) {
        connection.invoke('JoinProject', activeProjectRef.current).catch(console.error);
      }
    });

    connection.onclose(() => {
      setStatus('Disconnected');
    });

    connection.on('WorkItemUpdated', (rawItem: WorkItemDto) => {
      const item = normalizeWorkItemDto(rawItem);
      setLastWorkItemUpdated(item);
      dispatchCallbackSafely(workItemCallbacks.current, item, (e) =>
        console.error('Error in WorkItemUpdated callback:', e)
      );
    });

    connection.on('ReviewQueueUpdated', (event: ReviewQueueEventDto) => {
      setLastReviewQueueEvent(event);
      dispatchCallbackSafely(reviewQueueCallbacks.current, event, (e) =>
        console.error('Error in ReviewQueueUpdated callback:', e)
      );
    });

    connection.on('SyncStatusChanged', (rawEvent: SyncEventDto) => {
      const event = normalizeSyncEvent(rawEvent);
      setLastSyncEvent(event);
      setSyncEvents((prev) => [event, ...prev.slice(0, 49)]);
      dispatchCallbackSafely(syncCallbacks.current, event, (e) =>
        console.error('Error in SyncStatusChanged callback:', e)
      );
    });

    connection.on('DailyPlanSubmitted', (event: DailyPlanSubmittedDto) => {
      setLastDailyPlanSubmitted(event);
      dispatchCallbackSafely(dailyPlanCallbacks.current, event, (e) =>
        console.error('Error in DailyPlanSubmitted callback:', e)
      );
    });

    connection
      .start()
      .then(() => {
        setStatus('Connected');
        if (activeProjectRef.current) {
          connection.invoke('JoinProject', activeProjectRef.current).catch(console.error);
        }
      })
      .catch((err) => {
        console.error('SignalR start error:', err);
        setStatus('Disconnected');
      });

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
      }
    };
  }, [token]);

  // Project room management
  useEffect(() => {
    const connection = connectionRef.current;
    const oldProjectId = activeProjectRef.current;
    const newProjectId = activeProject?.id || null;

    activeProjectRef.current = newProjectId;

    if (connection && connection.state === signalR.HubConnectionState.Connected) {
      if (oldProjectId && oldProjectId !== newProjectId) {
        connection.invoke('LeaveProject', oldProjectId).catch(console.error);
      }
      if (newProjectId) {
        connection.invoke('JoinProject', newProjectId).catch(console.error);
      }
    }
  }, [activeProject]);

  return (
    <SignalRContext.Provider
      value={{
        status,
        lastWorkItemUpdated,
        lastReviewQueueEvent,
        lastSyncEvent,
        lastDailyPlanSubmitted,
        syncEvents,
        subscribeToWorkItemUpdates,
        subscribeToReviewQueueUpdates,
        subscribeToSyncEvents,
        subscribeToDailyPlanSubmitted,
      }}
    >
      {children}
    </SignalRContext.Provider>
  );
};

export const useSignalR = (): SignalRContextType => {
  const context = useContext(SignalRContext);
  if (!context) {
    throw new Error('useSignalR must be used within a SignalRProvider');
  }
  return context;
};
