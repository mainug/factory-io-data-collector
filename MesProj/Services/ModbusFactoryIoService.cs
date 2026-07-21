using System;
using System.Threading;
using System.Threading.Tasks;
using MesProj.Infrastructure;
using MesProj.Models;

namespace MesProj.Services
{
    public sealed class ModbusFactoryIoService : IFactoryIoService
    {
        private readonly ModbusTcpClient _client = new ModbusTcpClient();
        private readonly object _syncRoot = new object();
        private readonly EquipmentStatus _pilotEquipment = FactoryIoMap.CreateStatus(FactoryIoMap.BlueBaseBelt1Pilot, EquipmentState.Disconnected);
        private CancellationTokenSource _pollingCts;
        private Task _pollingTask;
        private int _pollingIntervalMilliseconds = 1000;
        public event EventHandler<FactoryStatus> StatusChanged;
        public event EventHandler<string> CommunicationError;

        public FactoryConnectionState ConnectionState { get; private set; }
        public bool IsEmergencyStopped { get; private set; }

        public async Task ConnectAsync(CommunicationOptions options, CancellationToken cancellationToken)
        {
            if (options == null) throw new ArgumentNullException("options");
            ConnectionState = FactoryConnectionState.Connecting;
            RaiseStatus();
            try
            {
                await _client.ConnectAsync(options.IpAddress, options.Port, options.DeviceId, options.ConnectionTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                _pollingIntervalMilliseconds = Math.Max(300, options.PollingIntervalMilliseconds);
                ConnectionState = FactoryConnectionState.Connected;
                lock (_syncRoot)
                {
                    _pilotEquipment.State = EquipmentState.Stopped;
                    _pilotEquipment.LastChangedAt = DateTime.Now;
                }
                StartPolling();
                RaiseStatus();
            }
            catch
            {
                ConnectionState = FactoryConnectionState.Fault;
                RaiseStatus();
                throw;
            }
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            StopPolling();
            _client.Disconnect();
            ConnectionState = FactoryConnectionState.Disconnected;
            lock (_syncRoot)
            {
                _pilotEquipment.State = EquipmentState.Disconnected;
                _pilotEquipment.CommandState = false;
                _pilotEquipment.FeedbackState = false;
                _pilotEquipment.LastChangedAt = DateTime.Now;
            }
            RaiseStatus();
            return Task.FromResult(0);
        }

        public Task<FactoryStatus> GetFactoryStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateStatus());
        }

        public Task<EquipmentStatus> GetEquipmentStatusAsync(string equipmentKey, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                return Task.FromResult(equipmentKey == _pilotEquipment.Key ? CloneEquipment(_pilotEquipment) : null);
            }
        }

        public async Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken)
        {
            ValidateAddress(address);
            if (address != FactoryIoMap.BlueBaseBelt1Pilot.OutputAddress)
                throw new InvalidOperationException("검증되지 않은 Coil 주소는 제어할 수 없습니다: " + address);
            await _client.WriteSingleCoilAsync((ushort)address, value, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _pilotEquipment.CommandState = value;
                _pilotEquipment.State = value ? EquipmentState.Running : EquipmentState.Stopped;
                _pilotEquipment.LastChangedAt = DateTime.Now;
            }
            RaiseStatus();
        }

        public async Task<bool> ReadDiscreteInputAsync(int address, CancellationToken cancellationToken)
        {
            ValidateAddress(address);
            return await _client.ReadDiscreteInputAsync((ushort)address, cancellationToken).ConfigureAwait(false);
        }

        public async Task<int> ReadHoldingRegisterAsync(int address, CancellationToken cancellationToken)
        {
            ValidateAddress(address);
            return await _client.ReadHoldingRegisterAsync((ushort)address, cancellationToken).ConfigureAwait(false);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("실제 Modbus 모드에서는 전체 가동이 비활성화되어 있습니다. 검증된 개별 설비만 제어하세요.");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("전체 정지 주소가 아직 정의되지 않았습니다. 검증된 개별 설비를 정지하세요.");
        }

        public Task EmergencyStopAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("비상정지는 PLC 안전회로 태그가 확정된 후 연결해야 합니다.");
        }

        public Task ResetEmergencyStopAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException("비상정지 해제 태그가 아직 정의되지 않았습니다.");
        }

        public Task SetOperationModeAsync(OperationMode mode, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("자동/수동 모드 태그가 아직 정의되지 않았습니다.");
        }

        public void Dispose()
        {
            StopPolling();
            _client.Dispose();
        }

        private void StartPolling()
        {
            StopPolling();
            _pollingCts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollAsync(_pollingCts.Token));
        }

        private void StopPolling()
        {
            if (_pollingCts != null)
            {
                _pollingCts.Cancel();
                _pollingCts.Dispose();
                _pollingCts = null;
            }
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var sensor = await _client.ReadDiscreteInputAsync((ushort)FactoryIoMap.BlueBaseBelt1Pilot.FeedbackInputAddress, cancellationToken).ConfigureAwait(false);
                    lock (_syncRoot)
                    {
                        if (_pilotEquipment.FeedbackState != sensor) _pilotEquipment.LastChangedAt = DateTime.Now;
                        _pilotEquipment.FeedbackState = sensor;
                    }
                    RaiseStatus();
                    await Task.Delay(_pollingIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ConnectionState = FactoryConnectionState.Fault;
                    RaiseCommunicationError("Modbus Polling 실패: " + ex.Message);
                    RaiseStatus();
                    return;
                }
            }
        }

        private static void ValidateAddress(int address)
        {
            if (address < 0 || address > ushort.MaxValue) throw new ArgumentOutOfRangeException("address");
        }

        private void RaiseStatus()
        {
            var status = StatusChanged;
            if (status != null)
            {
                status(this, CreateStatus());
            }
        }

        private FactoryStatus CreateStatus()
        {
            lock (_syncRoot)
            {
                return new FactoryStatus
                {
                    ConnectionState = ConnectionState,
                    IsEmergencyStopped = IsEmergencyStopped,
                    LastCommunicationAt = ConnectionState == FactoryConnectionState.Connected ? DateTime.Now : DateTime.MinValue,
                    EquipmentStatuses = new System.Collections.Generic.List<EquipmentStatus> { CloneEquipment(_pilotEquipment) }
                };
            }
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

        private void RaiseCommunicationError(string message)
        {
            var handler = CommunicationError;
            if (handler != null) handler(this, message);
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
