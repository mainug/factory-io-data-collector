using System;
using System.Collections.Generic;
using MesProj.Models;

namespace MesProj.Services
{
    public interface IApplicationStateService
    {
        event EventHandler<AppStateSnapshot> StateChanged;
        event EventHandler TargetQuantityReached;
        AppStateSnapshot GetSnapshot();
        void ApplyFactoryStatus(FactoryStatus status);
        void SetCurrentWorkOrder(WorkOrder workOrder);
        void CompleteCurrentWorkOrder();
        void AddAlarm(AlarmRecord alarmRecord);
        void AcknowledgeAlarm(AlarmRecord alarmRecord);
        IReadOnlyList<AlarmRecord> GetRecentAlarms(int count);
    }
}
