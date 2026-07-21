using System.Collections.Generic;
using MesProj.Models;

namespace MesProj.Repositories
{
    public interface IWorkOrderRepository
    {
        IReadOnlyList<WorkOrder> GetAll();
        void Add(WorkOrder workOrder);
        void Update(WorkOrder workOrder);
    }
}
