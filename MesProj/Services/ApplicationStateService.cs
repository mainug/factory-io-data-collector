using System;
using System.Collections.Generic;
using System.Linq;
using MesProj.Infrastructure;
using MesProj.Models;

namespace MesProj.Services
{
    public sealed class ApplicationStateService : IApplicationStateService
    {
        private readonly object _syncRoot = new object();
        private readonly List<AlarmRecord> _alarms = new List<AlarmRecord>();
        private AppStateSnapshot _snapshot = new AppStateSnapshot();
        private WorkOrder _currentWorkOrder;
        private int _lastFactoryQuantity;
        private int _lastFactoryGoodQuantity;
        private int _lastFactoryDefectQuantity;
        private int _workOrderStartQuantity;
        private int _workOrderStartGoodQuantity;
        private int _workOrderStartDefectQuantity;

        public event EventHandler<AppStateSnapshot> StateChanged;
        public event EventHandler TargetQuantityReached;

        public AppStateSnapshot GetSnapshot()
        {
            lock (_syncRoot)
            {
                return CloneSnapshot(_snapshot);
            }
        }

        public void ApplyFactoryStatus(FactoryStatus status)
        {
            if (status == null)
            {
                return;
            }

            var targetReached = false;
            lock (_syncRoot)
            {
                _snapshot.Summary.ConnectionState = status.ConnectionState;
                _lastFactoryQuantity = status.CurrentQuantity;
                _lastFactoryGoodQuantity = status.GoodQuantity;
                _lastFactoryDefectQuantity = status.DefectQuantity;
                if (_currentWorkOrder == null)
                {
                    _snapshot.Summary.CurrentQuantity = status.CurrentQuantity;
                    _snapshot.Summary.GoodQuantity = status.GoodQuantity;
                    _snapshot.Summary.DefectQuantity = status.DefectQuantity;
                }
                else
                {
                    _snapshot.Summary.CurrentQuantity = Math.Max(0, status.CurrentQuantity - _workOrderStartQuantity);
                    _snapshot.Summary.GoodQuantity = Math.Max(0, status.GoodQuantity - _workOrderStartGoodQuantity);
                    _snapshot.Summary.DefectQuantity = Math.Max(0, status.DefectQuantity - _workOrderStartDefectQuantity);
                }
                _snapshot.Summary.IsRunning = status.IsRunning;
                _snapshot.Summary.IsEmergencyStopped = status.IsEmergencyStopped;
                _snapshot.Summary.OperationMode = status.OperationMode;
                _snapshot.EquipmentStatuses = status.EquipmentStatuses.Select(CloneEquipment).ToList();
                _snapshot.LastCommunicationAt = status.LastCommunicationAt;

                if (_currentWorkOrder != null &&
                    _currentWorkOrder.Status == WorkOrderStatus.Running &&
                    _currentWorkOrder.TargetQuantity > 0 &&
                    _snapshot.Summary.CurrentQuantity >= _currentWorkOrder.TargetQuantity)
                {
                    CompleteCurrentWorkOrderCore();
                    targetReached = true;
                }
            }

            RaiseStateChanged();
            if (targetReached)
            {
                var handler = TargetQuantityReached;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        public void SetCurrentWorkOrder(WorkOrder workOrder)
        {
            if (workOrder == null)
            {
                return;
            }

            lock (_syncRoot)
            {
                _currentWorkOrder = workOrder;
                _workOrderStartQuantity = _lastFactoryQuantity;
                _workOrderStartGoodQuantity = _lastFactoryGoodQuantity;
                _workOrderStartDefectQuantity = _lastFactoryDefectQuantity;
                _snapshot.Summary.CurrentWorkOrderNo = workOrder.WorkOrderNo;
                _snapshot.Summary.TargetQuantity = workOrder.TargetQuantity;
                _snapshot.Summary.CurrentQuantity = 0;
                _snapshot.Summary.GoodQuantity = 0;
                _snapshot.Summary.DefectQuantity = 0;
            }

            RaiseStateChanged();
        }

        public void CompleteCurrentWorkOrder()
        {
            lock (_syncRoot)
            {
                CompleteCurrentWorkOrderCore();
            }

            RaiseStateChanged();
        }

        public void AddAlarm(AlarmRecord alarmRecord)
        {
            if (alarmRecord == null)
            {
                return;
            }

            lock (_syncRoot)
            {
                _alarms.Insert(0, alarmRecord);
                _snapshot.RecentAlarms = _alarms.Take(5).Select(CloneAlarm).ToList();
            }

            RaiseStateChanged();
        }

        public void AcknowledgeAlarm(AlarmRecord alarmRecord)
        {
            if (alarmRecord == null)
            {
                return;
            }

            lock (_syncRoot)
            {
                var found = _alarms.FirstOrDefault(x =>
                    x.OccurredAt == alarmRecord.OccurredAt &&
                    x.AlarmCode == alarmRecord.AlarmCode &&
                    x.EquipmentName == alarmRecord.EquipmentName);
                if (found != null)
                {
                    found.IsAcknowledged = true;
                }

                _snapshot.RecentAlarms = _alarms.Take(5).Select(CloneAlarm).ToList();
            }

            RaiseStateChanged();
        }

        public IReadOnlyList<AlarmRecord> GetRecentAlarms(int count)
        {
            lock (_syncRoot)
            {
                return _alarms.Take(count).Select(CloneAlarm).ToList();
            }
        }

        private void CompleteCurrentWorkOrderCore()
        {
            if (_currentWorkOrder == null)
            {
                return;
            }

            _currentWorkOrder.Status = WorkOrderStatus.Completed;
            _currentWorkOrder.EndedAt = DateTime.Now;
            AppLogger.Info("Work order completed: " + _currentWorkOrder.WorkOrderNo);
        }

        private void RaiseStateChanged()
        {
            var handler = StateChanged;
            if (handler != null)
            {
                handler(this, GetSnapshot());
            }
        }

        private static AppStateSnapshot CloneSnapshot(AppStateSnapshot source)
        {
            return new AppStateSnapshot
            {
                Summary = new ProductionSummary
                {
                    ConnectionState = source.Summary.ConnectionState,
                    CurrentWorkOrderNo = source.Summary.CurrentWorkOrderNo,
                    TargetQuantity = source.Summary.TargetQuantity,
                    CurrentQuantity = source.Summary.CurrentQuantity,
                    GoodQuantity = source.Summary.GoodQuantity,
                    DefectQuantity = source.Summary.DefectQuantity,
                    IsRunning = source.Summary.IsRunning,
                    IsEmergencyStopped = source.Summary.IsEmergencyStopped,
                    OperationMode = source.Summary.OperationMode
                },
                EquipmentStatuses = source.EquipmentStatuses.Select(CloneEquipment).ToList(),
                RecentAlarms = source.RecentAlarms.Select(CloneAlarm).ToList(),
                LastCommunicationAt = source.LastCommunicationAt
            };
        }

        private static EquipmentStatus CloneEquipment(EquipmentStatus source)
        {
            return new EquipmentStatus
            {
                Key = source.Key,
                Name = source.Name,
                State = source.State,
                CommandState = source.CommandState,
                FeedbackState = source.FeedbackState,
                OutputAddress = source.OutputAddress,
                LastChangedAt = source.LastChangedAt
            };
        }

        private static AlarmRecord CloneAlarm(AlarmRecord source)
        {
            return new AlarmRecord
            {
                OccurredAt = source.OccurredAt,
                EquipmentName = source.EquipmentName,
                AlarmCode = source.AlarmCode,
                Message = source.Message,
                Severity = source.Severity,
                IsAcknowledged = source.IsAcknowledged
            };
        }
    }
}
