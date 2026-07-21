using System;
using System.Collections.Generic;
using MesProj.Models;

namespace MesProj.Repositories
{
    public interface IProductionResultRepository
    {
        IReadOnlyList<ProductionResult> Search(DateTime? date, string itemCode, string workOrderNo);
        void SaveFromWorkOrder(WorkOrder workOrder, int goodQuantity, int defectQuantity);
    }
}
