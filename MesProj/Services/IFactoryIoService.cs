using System;
using System.Threading;
using System.Threading.Tasks;
using MesProj.Models;

namespace MesProj.Services
{
    public interface IFactoryIoService : IDisposable
    {
        event EventHandler<FactoryStatus> StatusChanged;
        event EventHandler<string> CommunicationError;
        event EventHandler<ProcessEvent> ProcessEventOccurred;
        event EventHandler<AlarmRecord> AlarmOccurred;

        FactoryConnectionState ConnectionState { get; }
        bool IsEmergencyStopped { get; }

        Task ConnectAsync(CommunicationOptions options, CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
        Task<FactoryStatus> GetFactoryStatusAsync(CancellationToken cancellationToken);
        Task<EquipmentStatus> GetEquipmentStatusAsync(string equipmentKey, CancellationToken cancellationToken);
        Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken);
        Task<bool> ReadDiscreteInputAsync(int address, CancellationToken cancellationToken);
        Task<int> ReadHoldingRegisterAsync(int address, CancellationToken cancellationToken);
        Task<int> ReadInputRegisterAsync(int address, CancellationToken cancellationToken);
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
        Task EmergencyStopAsync(CancellationToken cancellationToken);
        Task ResetEmergencyStopAsync(CancellationToken cancellationToken);
        Task SetOperationModeAsync(OperationMode mode, CancellationToken cancellationToken);
    }
}
