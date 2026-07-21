using System;
using System.Collections.Generic;
using System.Linq;
using MesProj.Models;

namespace MesProj.Repositories
{
    public sealed class MockWorkOrderRepository : IWorkOrderRepository
    {
        private readonly List<WorkOrder> _items = new List<WorkOrder>();

        public MockWorkOrderRepository()
        {
            _items.Add(new WorkOrder
            {
                WorkOrderNo = "WO-20260720-001",
                ItemCode = "ITEM-A",
                ItemName = "샘플 제품 A",
                TargetQuantity = 120,
                PlannedStartDate = DateTime.Today,
                PlannedEndDate = DateTime.Today.AddDays(1),
                Status = WorkOrderStatus.Created
            });
        }

        public IReadOnlyList<WorkOrder> GetAll()
        {
            return _items.Select(Clone).ToList();
        }

        public void Add(WorkOrder workOrder)
        {
            _items.Add(Clone(workOrder));
        }

        public void Update(WorkOrder workOrder)
        {
            var index = _items.FindIndex(x => x.WorkOrderNo == workOrder.WorkOrderNo);
            if (index >= 0)
            {
                _items[index] = Clone(workOrder);
            }
        }

        private static WorkOrder Clone(WorkOrder source)
        {
            return new WorkOrder
            {
                WorkOrderNo = source.WorkOrderNo,
                ItemCode = source.ItemCode,
                ItemName = source.ItemName,
                TargetQuantity = source.TargetQuantity,
                PlannedStartDate = source.PlannedStartDate,
                PlannedEndDate = source.PlannedEndDate,
                StartedAt = source.StartedAt,
                EndedAt = source.EndedAt,
                Status = source.Status
            };
        }
    }

    public sealed class MockProductionResultRepository : IProductionResultRepository
    {
        private readonly List<ProductionResult> _items = new List<ProductionResult>();

        public IReadOnlyList<ProductionResult> Search(DateTime? date, string itemCode, string workOrderNo)
        {
            IEnumerable<ProductionResult> query = _items;

            if (date.HasValue)
            {
                query = query.Where(x => x.StartedAt.HasValue && x.StartedAt.Value.Date == date.Value.Date);
            }

            if (!string.IsNullOrWhiteSpace(itemCode))
            {
                query = query.Where(x => x.ItemCode.IndexOf(itemCode, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(workOrderNo))
            {
                query = query.Where(x => x.WorkOrderNo.IndexOf(workOrderNo, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            return query.Select(Clone).ToList();
        }

        public void SaveFromWorkOrder(WorkOrder workOrder, int goodQuantity, int defectQuantity)
        {
            var existing = _items.FirstOrDefault(x => x.WorkOrderNo == workOrder.WorkOrderNo);
            var result = new ProductionResult
            {
                WorkOrderNo = workOrder.WorkOrderNo,
                ItemCode = workOrder.ItemCode,
                ItemName = workOrder.ItemName,
                TargetQuantity = workOrder.TargetQuantity,
                GoodQuantity = goodQuantity,
                DefectQuantity = defectQuantity,
                StartedAt = workOrder.StartedAt,
                EndedAt = workOrder.EndedAt,
                Status = workOrder.Status
            };

            if (existing == null)
            {
                _items.Add(result);
            }
            else
            {
                var index = _items.IndexOf(existing);
                _items[index] = result;
            }
        }

        private static ProductionResult Clone(ProductionResult source)
        {
            return new ProductionResult
            {
                WorkOrderNo = source.WorkOrderNo,
                ItemCode = source.ItemCode,
                ItemName = source.ItemName,
                TargetQuantity = source.TargetQuantity,
                GoodQuantity = source.GoodQuantity,
                DefectQuantity = source.DefectQuantity,
                StartedAt = source.StartedAt,
                EndedAt = source.EndedAt,
                Status = source.Status
            };
        }
    }

    public sealed class MockAlarmRepository : IAlarmRepository
    {
        private readonly List<AlarmRecord> _items = new List<AlarmRecord>();

        public IReadOnlyList<AlarmRecord> Search(string equipmentName, string severity, DateTime? date, bool onlyUnacknowledged)
        {
            IEnumerable<AlarmRecord> query = _items;

            if (!string.IsNullOrWhiteSpace(equipmentName))
            {
                query = query.Where(x => x.EquipmentName.IndexOf(equipmentName, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.IsNullOrWhiteSpace(severity) && severity != "전체")
            {
                query = query.Where(x => x.Severity == severity);
            }

            if (date.HasValue)
            {
                query = query.Where(x => x.OccurredAt.Date == date.Value.Date);
            }

            if (onlyUnacknowledged)
            {
                query = query.Where(x => !x.IsAcknowledged);
            }

            return query.OrderByDescending(x => x.OccurredAt).Select(Clone).ToList();
        }

        public void Add(AlarmRecord alarmRecord)
        {
            _items.Add(Clone(alarmRecord));
        }

        public void Acknowledge(AlarmRecord alarmRecord)
        {
            var found = _items.FirstOrDefault(x =>
                x.OccurredAt == alarmRecord.OccurredAt &&
                x.AlarmCode == alarmRecord.AlarmCode &&
                x.EquipmentName == alarmRecord.EquipmentName);
            if (found != null)
            {
                found.IsAcknowledged = true;
            }
        }

        private static AlarmRecord Clone(AlarmRecord source)
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
