using System;
using System.Collections.Generic;

namespace MesProj.Models
{
    public enum EquipmentState
    {
        Running,
        Stopped,
        Fault,
        Disconnected
    }

    public enum FactoryConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Fault
    }

    public enum OperationMode
    {
        Auto,
        Manual
    }

    public enum CommunicationMode
    {
        Mock,
        ModbusTcp
    }

    public enum WorkOrderStatus
    {
        Created,
        Running,
        Completed,
        Canceled
    }

    public sealed class EquipmentStatus
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public EquipmentState State { get; set; }
        public bool CommandState { get; set; }
        public bool FeedbackState { get; set; }
        public int OutputAddress { get; set; }
        public DateTime LastChangedAt { get; set; }
    }

    public sealed class FactoryStatus
    {
        public FactoryConnectionState ConnectionState { get; set; }
        public DateTime LastCommunicationAt { get; set; }
        public bool IsRunning { get; set; }
        public bool IsEmergencyStopped { get; set; }
        public OperationMode OperationMode { get; set; }
        public int CurrentQuantity { get; set; }
        public int GoodQuantity { get; set; }
        public int DefectQuantity { get; set; }
        public List<EquipmentStatus> EquipmentStatuses { get; set; }

        public FactoryStatus()
        {
            EquipmentStatuses = new List<EquipmentStatus>();
            OperationMode = OperationMode.Auto;
        }
    }

    public sealed class ProductionSummary
    {
        public FactoryConnectionState ConnectionState { get; set; }
        public string CurrentWorkOrderNo { get; set; }
        public int TargetQuantity { get; set; }
        public int CurrentQuantity { get; set; }
        public int GoodQuantity { get; set; }
        public int DefectQuantity { get; set; }
        public bool IsRunning { get; set; }
        public bool IsEmergencyStopped { get; set; }
        public OperationMode OperationMode { get; set; }

        public int AchievementRate
        {
            get
            {
                if (TargetQuantity <= 0)
                {
                    return 0;
                }

                return Math.Min(100, (int)Math.Round(CurrentQuantity * 100.0 / TargetQuantity));
            }
        }
    }

    public sealed class WorkOrder
    {
        public string WorkOrderNo { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public int TargetQuantity { get; set; }
        public DateTime PlannedStartDate { get; set; }
        public DateTime PlannedEndDate { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public WorkOrderStatus Status { get; set; }
    }

    public sealed class ProductionResult
    {
        public string WorkOrderNo { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public int TargetQuantity { get; set; }
        public int GoodQuantity { get; set; }
        public int DefectQuantity { get; set; }
        public int TotalQuantity { get { return GoodQuantity + DefectQuantity; } }
        public int AchievementRate
        {
            get
            {
                if (TargetQuantity <= 0)
                {
                    return 0;
                }

                return Math.Min(100, (int)Math.Round(TotalQuantity * 100.0 / TargetQuantity));
            }
        }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public WorkOrderStatus Status { get; set; }
    }

    public sealed class AlarmRecord
    {
        public DateTime OccurredAt { get; set; }
        public string EquipmentName { get; set; }
        public string AlarmCode { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
        public bool IsAcknowledged { get; set; }
    }

    public sealed class CommunicationOptions
    {
        public CommunicationMode Mode { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public byte DeviceId { get; set; }
        public int PollingIntervalMilliseconds { get; set; }
        public int ConnectionTimeoutMilliseconds { get; set; }
        public bool AutoReconnect { get; set; }

        public static CommunicationOptions CreateDefault()
        {
            return new CommunicationOptions
            {
                Mode = CommunicationMode.Mock,
                IpAddress = "127.0.0.1",
                Port = 502,
                DeviceId = 1,
                PollingIntervalMilliseconds = 1000,
                ConnectionTimeoutMilliseconds = 3000,
                AutoReconnect = true
            };
        }
    }

    public sealed class AppStateSnapshot
    {
        public ProductionSummary Summary { get; set; }
        public List<EquipmentStatus> EquipmentStatuses { get; set; }
        public List<AlarmRecord> RecentAlarms { get; set; }
        public DateTime LastCommunicationAt { get; set; }

        public AppStateSnapshot()
        {
            Summary = new ProductionSummary();
            EquipmentStatuses = new List<EquipmentStatus>();
            RecentAlarms = new List<AlarmRecord>();
        }
    }

    public sealed class TelemetrySample
    {
        public DateTime Timestamp { get; set; }
        public bool IsRunning { get; set; }
        public bool IsEmergencyStopped { get; set; }
        public int TotalQuantity { get; set; }
        public int GoodQuantity { get; set; }
        public int DefectQuantity { get; set; }
        public int FaultEquipmentCount { get; set; }
    }

    public sealed class TelemetryAnalysis
    {
        public int SampleCount { get; set; }
        public double RunRatePercent { get; set; }
        public double YieldRatePercent { get; set; }
        public double UnitsPerMinute { get; set; }
        public int FaultSampleCount { get; set; }
        public DateTime? FirstSampleAt { get; set; }
        public DateTime? LastSampleAt { get; set; }
    }
}
