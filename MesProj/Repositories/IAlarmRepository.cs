using System;
using System.Collections.Generic;
using MesProj.Models;

namespace MesProj.Repositories
{
    public interface IAlarmRepository
    {
        IReadOnlyList<AlarmRecord> Search(string equipmentName, string severity, DateTime? date, bool onlyUnacknowledged);
        void Add(AlarmRecord alarmRecord);
        void Acknowledge(AlarmRecord alarmRecord);
    }
}
