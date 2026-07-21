using System;
using System.Threading;
using System.Threading.Tasks;
using MesProj.Models;

namespace MesProj.Services
{
    public sealed class ModbusFactoryIoService : IFactoryIoService
    {
        public event EventHandler<FactoryStatus> StatusChanged;
        public event EventHandler<string> CommunicationError;

        public FactoryConnectionState ConnectionState { get; private set; }
        public bool IsEmergencyStopped { get; private set; }

        public Task ConnectAsync(CommunicationOptions options, CancellationToken cancellationToken)
        {
            ConnectionState = FactoryConnectionState.Fault;
            RaiseError("Modbus TCP 통신 라이브러리가 아직 연결되지 않았습니다. Mock 모드를 사용하세요.");
            return Task.FromResult(0);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            ConnectionState = FactoryConnectionState.Disconnected;
            return Task.FromResult(0);
        }

        public Task<FactoryStatus> GetFactoryStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new FactoryStatus { ConnectionState = ConnectionState, IsEmergencyStopped = IsEmergencyStopped });
        }

        public Task<EquipmentStatus> GetEquipmentStatusAsync(string equipmentKey, CancellationToken cancellationToken)
        {
            return Task.FromResult<EquipmentStatus>(null);
        }

        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken)
        {
            RaiseError("TODO: NModbus 등 실제 라이브러리 확정 후 Coil 쓰기 구현 필요");
            return Task.FromResult(0);
        }

        public Task<bool> ReadDiscreteInputAsync(int address, CancellationToken cancellationToken)
        {
            RaiseError("TODO: 실제 Discrete Input 읽기 구현 필요");
            return Task.FromResult(false);
        }

        public Task<int> ReadHoldingRegisterAsync(int address, CancellationToken cancellationToken)
        {
            RaiseError("TODO: 실제 Holding Register 읽기 구현 필요");
            return Task.FromResult(0);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            RaiseError("Modbus 구현 전에는 설비 가동 명령을 전송하지 않습니다.");
            return Task.FromResult(0);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            RaiseError("Modbus 구현 전에는 설비 정지 명령을 전송하지 않습니다.");
            return Task.FromResult(0);
        }

        public Task EmergencyStopAsync(CancellationToken cancellationToken)
        {
            IsEmergencyStopped = true;
            RaiseError("Modbus 구현 전에는 비상 정지 명령을 전송하지 않습니다.");
            return Task.FromResult(0);
        }

        public Task ResetEmergencyStopAsync(CancellationToken cancellationToken)
        {
            IsEmergencyStopped = false;
            RaiseError("Modbus 구현 전에는 비상 정지 해제 명령을 전송하지 않습니다.");
            return Task.FromResult(0);
        }

        public Task SetOperationModeAsync(OperationMode mode, CancellationToken cancellationToken)
        {
            RaiseError("TODO: 실제 운전 모드 Coil/Register 매핑 필요");
            return Task.FromResult(0);
        }

        public void Dispose()
        {
        }

        private void RaiseError(string message)
        {
            var error = CommunicationError;
            if (error != null)
            {
                error(this, message);
            }

            var status = StatusChanged;
            if (status != null)
            {
                status(this, new FactoryStatus { ConnectionState = ConnectionState, IsEmergencyStopped = IsEmergencyStopped });
            }
        }
    }
}
