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
        private const int SorterPollingMaximumMilliseconds = 100;
        private const int BlueLidIdentificationTimeoutMilliseconds = 3000;
        private const int SorterArrivalTimeoutMilliseconds = 10000;
        private const int SorterExitTimeoutMilliseconds = 5000;
        private const int BlueLidBeltOverrunMilliseconds = 750;
        private readonly ModbusTcpClient _client = new ModbusTcpClient();
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, EquipmentStatus> _statuses = new Dictionary<string, EquipmentStatus>();
        private readonly HashSet<string> _initializedSensors = new HashSet<string>();
        private readonly EquipmentDefinition[] _definitions =
        {
            FactoryIoMap.MachiningEntranceBelt, FactoryIoMap.MachiningProductType, FactoryIoMap.ExitBeltSorter1,
            FactoryIoMap.MachiningStart, FactoryIoMap.MachiningStop, FactoryIoMap.MachiningReset,
            FactoryIoMap.Sorter1ForwardAndPower, FactoryIoMap.Sorter1BlueLid,
            FactoryIoMap.Sorter1GreenLid, FactoryIoMap.BlueLidBelt1,
            FactoryIoMap.MachiningEntranceSensor, FactoryIoMap.MachiningBusy, FactoryIoMap.MachiningError,
            FactoryIoMap.MachiningOpened, FactoryIoMap.MachiningOutputSensor,
            FactoryIoMap.ReadSensorSorter1, FactoryIoMap.BlueLidCamera, FactoryIoMap.GreenLidCamera
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
        private bool _blueLidAutoSortingEnabled;
        private BlueLidRouteState _blueLidRouteState = BlueLidRouteState.Idle;
        private DateTime _blueLidRouteDeadlineUtc = DateTime.MinValue;
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
                _pollingIntervalMilliseconds = Math.Max(50, Math.Min(SorterPollingMaximumMilliseconds, options.PollingIntervalMilliseconds));
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
                    _blueLidAutoSortingEnabled = false;
                    ResetBlueLidRouteState();
                    foreach (var status in _statuses.Values)
                    {
                        status.State = EquipmentState.Stopped;
                        status.LastChangedAt = DateTime.Now;
                    }
                }
                await StopSorterOutputsAsync(cancellationToken).ConfigureAwait(false);
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

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            StopPolling();
            if (_client.IsConnected)
            {
                try
                {
                    await StopSorterOutputsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Sorter safe stop during disconnect failed.", ex);
                }
            }
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
                _blueLidAutoSortingEnabled = false;
                ResetBlueLidRouteState();
            }
            RaiseStatus();
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
            if (value && address == FactoryIoMap.Sorter1BlueLid.OutputAddress)
            {
                lock (_syncRoot)
                {
                    if (_statuses[FactoryIoMap.Sorter1GreenLid.Key].CommandState)
                        throw new InvalidOperationException("Sorter 1 Blue Lid와 Green Lid는 동시에 ON할 수 없습니다.");
                }
            }
            if (value && address == FactoryIoMap.Sorter1GreenLid.OutputAddress)
            {
                lock (_syncRoot)
                {
                    if (_statuses[FactoryIoMap.Sorter1BlueLid.Key].CommandState)
                        throw new InvalidOperationException("Sorter 1 Green Lid와 Blue Lid는 동시에 ON할 수 없습니다.");
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

        public async Task SetBlueLidAutoSortingEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            if (ConnectionState != FactoryConnectionState.Connected)
                throw new InvalidOperationException("Factory I/O 연결 후 자동 분류를 시작할 수 있습니다.");

            lock (_syncRoot)
            {
                _blueLidAutoSortingEnabled = enabled;
                if (!enabled) ResetBlueLidRouteState();
            }

            if (!enabled)
                await StopSorterOutputsAsync(cancellationToken).ConfigureAwait(false);

            AppLogger.Info("BLUE LID AUTO SORTING " + (enabled ? "ENABLED" : "DISABLED"));
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

                        if (risingEdge || fallingEdge)
                            await HandleBlueLidSensorEdgeAsync(definition, risingEdge, fallingEdge, cancellationToken).ConfigureAwait(false);

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
                    await CheckBlueLidRouteTimersAsync(cancellationToken).ConfigureAwait(false);
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

        private async Task HandleBlueLidSensorEdgeAsync(
            EquipmentDefinition definition,
            bool risingEdge,
            bool fallingEdge,
            CancellationToken cancellationToken)
        {
            if (definition.Key == FactoryIoMap.MachiningOutputSensor.Key && risingEdge)
            {
                var routeCanStart = false;
                lock (_syncRoot)
                {
                    routeCanStart = _blueLidAutoSortingEnabled && !_resetRecoveryPending &&
                        _blueLidRouteState == BlueLidRouteState.Idle;
                    if (routeCanStart)
                    {
                        _blueLidRouteState = BlueLidRouteState.AwaitingIdentification;
                        _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(BlueLidIdentificationTimeoutMilliseconds);
                    }
                }
                AppLogger.Info(routeCanStart
                    ? "BLUE LID ROUTE awaiting camera identification"
                    : "BLUE LID ROUTE ignored Write Sensor while route is active or reset recovery is pending");
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidCamera.Key && risingEdge)
            {
                var canceled = false;
                lock (_syncRoot)
                {
                    canceled = _blueLidRouteState == BlueLidRouteState.AwaitingIdentification;
                    if (canceled) ResetBlueLidRouteState();
                }
                if (canceled) AppLogger.Info("BLUE LID ROUTE canceled by Green Lid Camera");
                return;
            }

            if (definition.Key == FactoryIoMap.BlueLidCamera.Key && risingEdge)
            {
                var prepareRoute = false;
                lock (_syncRoot)
                {
                    prepareRoute = _blueLidRouteState == BlueLidRouteState.AwaitingIdentification &&
                        DateTime.UtcNow <= _blueLidRouteDeadlineUtc;
                }
                if (!prepareRoute)
                {
                    AppLogger.Info("BLUE LID ROUTE ignored camera signal without Write Sensor latch");
                    return;
                }

                await SetSorterOutputAsync(FactoryIoMap.Sorter1GreenLid, false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, true, cancellationToken).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _blueLidRouteState = BlueLidRouteState.WaitingForSorter;
                    _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(SorterArrivalTimeoutMilliseconds);
                }
                AppLogger.Info("BLUE LID ROUTE prepared Coil 7 ON, Coil 8 OFF, Coil 9 ON");
                return;
            }

            if (definition.Key != FactoryIoMap.ReadSensorSorter1.Key) return;

            if (risingEdge)
            {
                var startSorter = false;
                lock (_syncRoot)
                {
                    startSorter = _blueLidRouteState == BlueLidRouteState.WaitingForSorter;
                }
                if (!startSorter) return;

                await SetSorterOutputAsync(FactoryIoMap.Sorter1ForwardAndPower, true, cancellationToken).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _blueLidRouteState = BlueLidRouteState.Routing;
                    _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(SorterExitTimeoutMilliseconds);
                }
                AppLogger.Info("BLUE LID ROUTE started Coil 6 ON");
                return;
            }

            if (fallingEdge)
            {
                var finishSorter = false;
                lock (_syncRoot)
                {
                    finishSorter = _blueLidRouteState == BlueLidRouteState.Routing;
                }
                if (!finishSorter) return;

                await SetSorterOutputAsync(FactoryIoMap.Sorter1ForwardAndPower, false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, false, cancellationToken).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _blueLidRouteState = BlueLidRouteState.ClearingBlueLidBelt;
                    _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(BlueLidBeltOverrunMilliseconds);
                }
                AppLogger.Info("BLUE LID ROUTE sorter cleared, Coil 9 overrun started");
            }
        }

        private async Task CheckBlueLidRouteTimersAsync(CancellationToken cancellationToken)
        {
            BlueLidRouteState expiredState;
            lock (_syncRoot)
            {
                if (_blueLidRouteState == BlueLidRouteState.Idle || DateTime.UtcNow < _blueLidRouteDeadlineUtc)
                    return;
                expiredState = _blueLidRouteState;
                ResetBlueLidRouteState();
            }

            if (expiredState == BlueLidRouteState.AwaitingIdentification)
            {
                AppLogger.Info("BLUE LID ROUTE identification window expired");
                return;
            }

            if (expiredState == BlueLidRouteState.ClearingBlueLidBelt)
            {
                await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, false, cancellationToken).ConfigureAwait(false);
                AppLogger.Info("BLUE LID ROUTE completed Coil 9 OFF");
                return;
            }

            await StopSorterOutputsAsync(cancellationToken).ConfigureAwait(false);
            RaiseSorterAlarm(
                expiredState == BlueLidRouteState.WaitingForSorter ? "SORTER_1_ARRIVAL_TIMEOUT" : "SORTER_1_EXIT_TIMEOUT",
                expiredState == BlueLidRouteState.WaitingForSorter
                    ? "Blue Lid가 제한시간 안에 Sorter 1에 도착하지 않았습니다."
                    : "Blue Lid가 제한시간 안에 Sorter 1을 통과하지 못했습니다.");
        }

        private async Task SetSorterOutputAsync(
            EquipmentDefinition definition,
            bool value,
            CancellationToken cancellationToken)
        {
            await _client.WriteSingleCoilAsync((ushort)definition.OutputAddress, value, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                var status = _statuses[definition.Key];
                status.CommandState = value;
                status.State = value ? EquipmentState.Running : EquipmentState.Stopped;
                status.LastChangedAt = DateTime.Now;
            }
            AppLogger.Info(string.Format("AUTO WRITE Coil {0} {1} ({2})",
                definition.OutputAddress, value ? "ON" : "OFF", definition.Key));
        }

        private async Task StopSorterOutputsAsync(CancellationToken cancellationToken)
        {
            await SetSorterOutputAsync(FactoryIoMap.Sorter1ForwardAndPower, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.Sorter1GreenLid, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, false, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                ResetBlueLidRouteState();
            }
        }

        private void ResetBlueLidRouteState()
        {
            _blueLidRouteState = BlueLidRouteState.Idle;
            _blueLidRouteDeadlineUtc = DateTime.MinValue;
        }

        private void RaiseSorterAlarm(string code, string message)
        {
            var alarm = new AlarmRecord
            {
                OccurredAt = DateTime.Now,
                EquipmentName = "Sorter 1",
                AlarmCode = code,
                Message = message,
                Severity = "Warning",
                IsAcknowledged = false
            };
            AppLogger.Info("ALARM " + code);
            var handler = AlarmOccurred;
            if (handler != null) handler(this, alarm);
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
            if (key == "BlueLidCamera") return "Blue Lid 판별";
            if (key == "GreenLidCamera") return "Green Lid 판별";
            if (key == "ReadSensorSorter1") return "Sorter 1 도착";
            return "기타";
        }

        private enum BlueLidRouteState
        {
            Idle,
            AwaitingIdentification,
            WaitingForSorter,
            Routing,
            ClearingBlueLidBelt
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
