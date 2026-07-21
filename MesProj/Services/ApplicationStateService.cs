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

        public event EventHandler<AppStateSnapshot> StateChanged;

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

            lock (_syncRoot)
            {
                _snapshot.Summary.ConnectionState = status.ConnectionState;
                _snapshot.Summary.CurrentQuantity = status.CurrentQuantity;
                _snapshot.Summary.GoodQuantity = status.GoodQuantity;
                _snapshot.Summary.DefectQuantity = status.DefectQuantity;
                _snapshot.Summary.IsRunning = status.IsRunning;
                _snapshot.Summary.IsEmergencyStopped = status.IsEmergencyStopped;
                _snapshot.Summary.OperationMode = status.OperationMode;
                _snapshot.EquipmentStatuses = status.EquipmentStatuses.Select(CloneEquipment).ToList();
                _snapshot.LastCommunicationAt = status.LastCommunicationAt;

                if (_currentWorkOrder != null &&
                    _currentWorkOrder.Status == WorkOrderStatus.Running &&
                    _currentWorkOrder.TargetQuantity > 0 &&
                    status.CurrentQuantity >= _currentWorkOrder.TargetQuantity)
                {
                    CompleteCurrentWorkOrderCore();
                }
            }

            RaiseStateChanged();
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
                _snapshot.Summary.CurrentWorkOrderNo = workOrder.WorkOrderNo;
                _snapshot.Summary.TargetQuantity = workOrder.TargetQuantity;
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
