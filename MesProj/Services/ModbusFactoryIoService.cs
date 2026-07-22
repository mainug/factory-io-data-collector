using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MesProj.Infrastructure;
using MesProj.Models;

namespace MesProj.Services
{
    public sealed class ModbusFactoryIoService : IFactoryIoService
    {
        private const int EntranceBeltStopDelayMilliseconds = 1500;
        private const int ResetRecoveryStabilizationMilliseconds = 5000;
        private readonly ModbusTcpClient _client = new ModbusTcpClient();
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, EquipmentStatus> _statuses = new Dictionary<string, EquipmentStatus>();
        private readonly HashSet<string> _initializedSensors = new HashSet<string>();
        private readonly EquipmentDefinition[] _definitions =
        {
            FactoryIoMap.MachiningEntranceBelt, FactoryIoMap.MachiningProductType, FactoryIoMap.ExitBeltSorter1,
            FactoryIoMap.MachiningStart, FactoryIoMap.MachiningStop, FactoryIoMap.MachiningReset,
            FactoryIoMap.MachiningEntranceSensor, FactoryIoMap.MachiningBusy, FactoryIoMap.MachiningError,
            FactoryIoMap.MachiningOpened, FactoryIoMap.MachiningOutputSensor
        };
        private CancellationTokenSource _pollingCts;
        private Task _pollingTask;
        private int _pollingIntervalMilliseconds = 1000;
        private int _machiningProgress;
        private bool _restartEntranceBeltAfterCycle;
        private bool _machiningDoorClosedAfterStart;
        private DateTime? _openedWhileBusyAt;
        private bool _busyTimeoutRaised;
        private bool _resetRecoveryPending;
        private bool _resetRecoveryWriteSeen;
        public event EventHandler<FactoryStatus> StatusChanged;
        public event EventHandler<string> CommunicationError;
        public event EventHandler<ProcessEvent> ProcessEventOccurred;
        public event EventHandler<AlarmRecord> AlarmOccurred;

        public FactoryConnectionState ConnectionState { get; private set; }
        public bool IsEmergencyStopped { get; private set; }

        public ModbusFactoryIoService()
        {
            foreach (var definition in _definitions)
                _statuses[definition.Key] = FactoryIoMap.CreateStatus(definition, EquipmentState.Disconnected);
        }

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
                    _initializedSensors.Clear();
                    _restartEntranceBeltAfterCycle = false;
                    _machiningDoorClosedAfterStart = false;
                    _openedWhileBusyAt = null;
                    _busyTimeoutRaised = false;
                    _resetRecoveryPending = false;
                    _resetRecoveryWriteSeen = false;
                    foreach (var status in _statuses.Values)
                    {
                        status.State = EquipmentState.Stopped;
                        status.LastChangedAt = DateTime.Now;
                    }
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
                foreach (var status in _statuses.Values)
                {
                    status.State = EquipmentState.Disconnected;
                    status.CommandState = false;
                    status.FeedbackState = false;
                    status.LastChangedAt = DateTime.Now;
                }
                _restartEntranceBeltAfterCycle = false;
                _machiningDoorClosedAfterStart = false;
                _openedWhileBusyAt = null;
                _busyTimeoutRaised = false;
                _resetRecoveryPending = false;
                _resetRecoveryWriteSeen = false;
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
                EquipmentStatus status;
                return Task.FromResult(_statuses.TryGetValue(equipmentKey, out status) ? CloneEquipment(status) : null);
            }
        }

        public async Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken)
        {
            ValidateAddress(address);
            var definition = _definitions.FirstOrDefault(x => x.OutputAddress == address);
            if (definition == null || address < 0)
                throw new InvalidOperationException("검증되지 않은 Coil 주소는 제어할 수 없습니다: " + address);
            if (address == FactoryIoMap.MachiningEntranceBelt.OutputAddress && value)
            {
                lock (_syncRoot)
                {
                    if (_statuses[FactoryIoMap.MachiningEntranceSensor.Key].FeedbackState)
                        throw new InvalidOperationException("입구 센서에 소재가 감지되어 Entrance belt를 가동할 수 없습니다.");
                }
            }
            if (address == FactoryIoMap.MachiningReset.OutputAddress && value)
            {
                await _client.WriteSingleCoilAsync(
                    (ushort)FactoryIoMap.MachiningEntranceBelt.OutputAddress,
                    false,
                    cancellationToken).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    var belt = _statuses[FactoryIoMap.MachiningEntranceBelt.Key];
                    belt.CommandState = false;
                    belt.State = EquipmentState.Stopped;
                    belt.LastChangedAt = DateTime.Now;
                }
                AppLogger.Info("AUTO WRITE Coil 0 OFF (Reset recovery interlock)");
            }

            await _client.WriteSingleCoilAsync((ushort)address, value, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                var status = _statuses[definition.Key];
                status.CommandState = value;
                status.State = value ? EquipmentState.Running : EquipmentState.Stopped;
                status.LastChangedAt = DateTime.Now;
                if (address == FactoryIoMap.MachiningStart.OutputAddress && value)
                {
                    _restartEntranceBeltAfterCycle = true;
                    _machiningDoorClosedAfterStart = false;
                }
                if ((address == FactoryIoMap.MachiningStop.OutputAddress || address == FactoryIoMap.MachiningReset.OutputAddress) && value)
                {
                    _restartEntranceBeltAfterCycle = false;
                    _machiningDoorClosedAfterStart = false;
                }
                if (address == FactoryIoMap.MachiningReset.OutputAddress && value)
                {
                    _resetRecoveryPending = true;
                    _resetRecoveryWriteSeen = false;
                }
            }
            AppLogger.Info(string.Format("MODBUS WRITE Coil {0} {1} ({2})", address, value ? "ON" : "OFF", definition.Key));
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

        public async Task<int> ReadInputRegisterAsync(int address, CancellationToken cancellationToken)
        {
            ValidateAddress(address);
            return await _client.ReadInputRegisterAsync((ushort)address, cancellationToken).ConfigureAwait(false);
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
                    foreach (var definition in _definitions.Where(x => x.FeedbackInputAddress >= 0))
                    {
                        var sensor = await _client.ReadDiscreteInputAsync((ushort)definition.FeedbackInputAddress, cancellationToken).ConfigureAwait(false);
                        var risingEdge = false;
                        var fallingEdge = false;
                        lock (_syncRoot)
                        {
                            var status = _statuses[definition.Key];
                            risingEdge = _initializedSensors.Contains(definition.Key) && !status.FeedbackState && sensor;
                            fallingEdge = _initializedSensors.Contains(definition.Key) && status.FeedbackState && !sensor;
                            if (status.FeedbackState != sensor) status.LastChangedAt = DateTime.Now;
                            status.FeedbackState = sensor;
                            status.State = sensor ? EquipmentState.Running : EquipmentState.Stopped;
                            _initializedSensors.Add(definition.Key);
                        }
                        if (risingEdge) RaiseProcessEvent(definition);
                        if (risingEdge || fallingEdge)
                            AppLogger.Info(string.Format("MODBUS INPUT Input {0} {1} ({2})",
                                definition.FeedbackInputAddress, sensor ? "ON" : "OFF", definition.Key));

                        if (definition.Key == FactoryIoMap.MachiningOpened.Key)
                        {
                            lock (_syncRoot)
                            {
                                if (_restartEntranceBeltAfterCycle && !sensor)
                                    _machiningDoorClosedAfterStart = true;
                                if (risingEdge && _statuses[FactoryIoMap.MachiningBusy.Key].FeedbackState)
                                    _openedWhileBusyAt = DateTime.Now;
                            }
                        }

                        if (definition.Key == FactoryIoMap.MachiningOutputSensor.Key && risingEdge)
                        {
                            var recoveryOutput = false;
                            lock (_syncRoot)
                            {
                                recoveryOutput = _resetRecoveryPending;
                                if (recoveryOutput) _resetRecoveryWriteSeen = true;
                            }
                            if (recoveryOutput)
                                RaiseProcessEvent(definition, "Aborted");
                        }

                        if (definition.Key == FactoryIoMap.MachiningBusy.Key && fallingEdge)
                        {
                            var entranceOccupied = false;
                            var beltIsRunning = false;
                            var restartRequested = false;
                            var doorCycleSeen = false;
                            var recoveryCompleted = false;
                            var recoveryWriteSeen = false;
                            lock (_syncRoot)
                            {
                                entranceOccupied = _statuses[FactoryIoMap.MachiningEntranceSensor.Key].FeedbackState;
                                beltIsRunning = _statuses[FactoryIoMap.MachiningEntranceBelt.Key].CommandState;
                                restartRequested = _restartEntranceBeltAfterCycle;
                                doorCycleSeen = _machiningDoorClosedAfterStart;
                                recoveryCompleted = _resetRecoveryPending;
                                recoveryWriteSeen = _resetRecoveryWriteSeen;
                                _openedWhileBusyAt = null;
                                _busyTimeoutRaised = false;
                            }

                            if (recoveryCompleted)
                            {
                                AppLogger.Info("MACHINING RECOVERY Busy OFF, stabilization started, WriteSensor=" + recoveryWriteSeen);
                                await Task.Delay(ResetRecoveryStabilizationMilliseconds, cancellationToken).ConfigureAwait(false);
                                lock (_syncRoot)
                                {
                                    _resetRecoveryPending = false;
                                    _resetRecoveryWriteSeen = false;
                                }
                                AppLogger.Info("MACHINING RECOVERY COMPLETED after 5 second stabilization");
                            }

                            if (restartRequested && doorCycleSeen)
                            {
                                if (!entranceOccupied && !beltIsRunning)
                                {
                                    await _client.WriteSingleCoilAsync(
                                        (ushort)FactoryIoMap.MachiningEntranceBelt.OutputAddress,
                                        true,
                                        cancellationToken).ConfigureAwait(false);
                                    AppLogger.Info("AUTO WRITE Coil 0 ON (Busy OFF after machining cycle)");
                                }
                                lock (_syncRoot)
                                {
                                    if (!entranceOccupied && !beltIsRunning)
                                    {
                                        var belt = _statuses[FactoryIoMap.MachiningEntranceBelt.Key];
                                        belt.CommandState = true;
                                        belt.State = EquipmentState.Running;
                                        belt.LastChangedAt = DateTime.Now;
                                    }
                                    _restartEntranceBeltAfterCycle = false;
                                    _machiningDoorClosedAfterStart = false;
                                }
                            }
                        }

                        if (definition.Key == FactoryIoMap.MachiningEntranceSensor.Key && sensor)
                        {
                            var beltIsRunning = false;
                            lock (_syncRoot)
                            {
                                beltIsRunning = _statuses[FactoryIoMap.MachiningEntranceBelt.Key].CommandState;
                            }

                            if (beltIsRunning)
                            {
                                await Task.Delay(EntranceBeltStopDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                                await _client.WriteSingleCoilAsync(
                                    (ushort)FactoryIoMap.MachiningEntranceBelt.OutputAddress,
                                    false,
                                    cancellationToken).ConfigureAwait(false);
                                AppLogger.Info("AUTO WRITE Coil 0 OFF (Entrance sensor delay elapsed)");
                                lock (_syncRoot)
                                {
                                    var belt = _statuses[FactoryIoMap.MachiningEntranceBelt.Key];
                                    belt.CommandState = false;
                                    belt.State = EquipmentState.Stopped;
                                    belt.LastChangedAt = DateTime.Now;
                                }
                            }
                        }
                    }
                    CheckMachiningBusyTimeout();
                    _machiningProgress = await _client.ReadInputRegisterAsync(0, cancellationToken).ConfigureAwait(false);
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
                    MachiningProgress = _machiningProgress,
                    EquipmentStatuses = _statuses.Values.Select(CloneEquipment).ToList()
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
                InputAddress = source.InputAddress,
                IsPulseOutput = source.IsPulseOutput,
                LastChangedAt = source.LastChangedAt
            };
        }

        private void RaiseCommunicationError(string message)
        {
            var handler = CommunicationError;
            if (handler != null) handler(this, message);
        }

        private void RaiseProcessEvent(EquipmentDefinition definition)
        {
            RaiseProcessEvent(definition, "RisingEdge");
        }

        private void RaiseProcessEvent(EquipmentDefinition definition, string eventType)
        {
            var handler = ProcessEventOccurred;
            if (handler == null) return;
            handler(this, new ProcessEvent
            {
                Timestamp = DateTime.Now,
                Stage = GetStage(definition.Key),
                SensorKey = definition.Key,
                SensorName = definition.Name,
                InputAddress = definition.FeedbackInputAddress,
                EventType = eventType
            });
        }

        private void CheckMachiningBusyTimeout()
        {
            AlarmRecord alarm = null;
            lock (_syncRoot)
            {
                var busy = _statuses[FactoryIoMap.MachiningBusy.Key].FeedbackState;
                var opened = _statuses[FactoryIoMap.MachiningOpened.Key].FeedbackState;
                if (!busy)
                {
                    _openedWhileBusyAt = null;
                    _busyTimeoutRaised = false;
                    return;
                }

                if (!opened || !_openedWhileBusyAt.HasValue || _busyTimeoutRaised ||
                    DateTime.Now - _openedWhileBusyAt.Value < TimeSpan.FromSeconds(30))
                    return;

                _busyTimeoutRaised = true;
                alarm = new AlarmRecord
                {
                    OccurredAt = DateTime.Now,
                    EquipmentName = "Machining Center",
                    AlarmCode = "MACHINING_BUSY_TIMEOUT",
                    Message = "가공기 개방 후 30초 동안 Busy가 해제되지 않았습니다. 자동 가공을 중지하고 Reset 복구가 필요합니다.",
                    Severity = "Warning",
                    IsAcknowledged = false
                };
            }

            AppLogger.Info("ALARM MACHINING_BUSY_TIMEOUT");
            var handler = AlarmOccurred;
            if (handler != null) handler(this, alarm);
        }

        private static string GetStage(string key)
        {
            if (key == "MachiningEntranceSensor") return "가공 입구";
            if (key == "MachiningBusy") return "가공 시작";
            if (key == "MachiningError") return "가공 오류";
            if (key == "MachiningOpened") return "가공기 개방";
            if (key == "MachiningOutputSensor") return "가공품 배출";
            return "기타";
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
