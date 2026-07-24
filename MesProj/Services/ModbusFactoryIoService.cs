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
        private const int SorterExitTimeoutMilliseconds = 5000;
        private const int SorterDischargeOverrunMilliseconds = 1500;
        private const int BlueLidBeltOverrunMilliseconds = 8500;
        private const int GreenLidBeltOverrunMilliseconds = 8500;
        private const int GreenLidPackingSettleMilliseconds = 500;
        private const int GreenLidPackingDropMilliseconds = 900;
        private const int GreenLidPackingReleaseMilliseconds = 900;
        private const int GreenLidSetPointHomeValue = 0;
        private const int GreenLidXPickValue = 890;
        private const int GreenLidXPlaceValue = 100;
        private const int GreenLidZPickValue = 1000;
        private const int GreenLidSetPointMoveDelayMilliseconds = 900;
        private const int GreenLidPositionTolerance = 10;
        private const int GreenLidPositionTimeoutMilliseconds = 5000;
        private readonly ModbusTcpClient _client = new ModbusTcpClient();
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, EquipmentStatus> _statuses = new Dictionary<string, EquipmentStatus>();
        private readonly HashSet<string> _initializedSensors = new HashSet<string>();
        private readonly EquipmentDefinition[] _definitions =
        {
            FactoryIoMap.MachiningEntranceBelt, FactoryIoMap.MachiningProductType, FactoryIoMap.ExitBeltSorter1,
            FactoryIoMap.MachiningStart, FactoryIoMap.MachiningStop, FactoryIoMap.MachiningReset,
            FactoryIoMap.Sorter1ForwardAndPower, FactoryIoMap.Sorter1BlueLid,
            FactoryIoMap.Sorter1GreenLid, FactoryIoMap.BlueLidBelt1, FactoryIoMap.GreenLidBelt1,
            FactoryIoMap.GreenLidBelt2, FactoryIoMap.GreenLidRoller1, FactoryIoMap.RightPositioner4Raise,
            FactoryIoMap.GreenLidGrab, FactoryIoMap.GreenLidPositionerClamp,
            FactoryIoMap.GreenLidRoller2, FactoryIoMap.GreenLidRoller3, FactoryIoMap.GreenLidRoller4,
            FactoryIoMap.GreenLidRoller5, FactoryIoMap.StackerCraneGreenLidLeft,
            FactoryIoMap.MachiningEntranceSensor, FactoryIoMap.MachiningBusy, FactoryIoMap.MachiningError,
            FactoryIoMap.MachiningOpened, FactoryIoMap.MachiningOutputSensor,
            FactoryIoMap.ReadSensorSorter1, FactoryIoMap.BlueLidCamera, FactoryIoMap.GreenLidCamera,
            FactoryIoMap.RightPositioner4Limit, FactoryIoMap.GreenLidStopRollerSensor,
            FactoryIoMap.GreenLidGrabSensor, FactoryIoMap.GreenLidPositionerSensor,
            FactoryIoMap.GreenLidPositionerClampSensor,
            FactoryIoMap.GreenLidXMovingSensor, FactoryIoMap.GreenLidZMovingSensor
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
        private LidRouteColor _activeLidRouteColor = LidRouteColor.None;
        private GreenLidPackingState _greenLidPackingState = GreenLidPackingState.Idle;
        private bool _greenLidStopSensorReleasePending;
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
                    ResetGreenLidPackingState();
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
                ResetGreenLidPackingState();
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

        public async Task SetLidAutoSortingEnabledAsync(bool enabled, CancellationToken cancellationToken)
        {
            if (ConnectionState != FactoryConnectionState.Connected)
                throw new InvalidOperationException("Factory I/O 연결 후 자동 분류를 시작할 수 있습니다.");

            if (enabled)
            {
                await ResetGreenLidSetPointsAsync(cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.ExitBeltSorter1, true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.Sorter1ForwardAndPower, true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, true, cancellationToken).ConfigureAwait(false);
                await SetGreenLidRoller1IfStopSensorClearAsync(cancellationToken).ConfigureAwait(false);
                await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _blueLidAutoSortingEnabled = true;
                    ResetBlueLidRouteState();
                    ResetGreenLidPackingState();
                }
            }
            else
            {
                lock (_syncRoot)
                {
                    _blueLidAutoSortingEnabled = false;
                    ResetBlueLidRouteState();
                }
                await StopSorterOutputsAsync(cancellationToken).ConfigureAwait(false);
            }

            AppLogger.Info("LID AUTO SORTING " + (enabled ? "ENABLED" : "DISABLED"));
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

                        if (sensor && !risingEdge && definition.Key == FactoryIoMap.BlueLidCamera.Key && ShouldRefreshLidRoute(LidRouteColor.Blue))
                            await SelectLidRouteAsync(LidRouteColor.Blue, cancellationToken).ConfigureAwait(false);

                        if (sensor && !risingEdge && definition.Key == FactoryIoMap.GreenLidCamera.Key && ShouldRefreshLidRoute(LidRouteColor.Green))
                            await SelectLidRouteAsync(LidRouteColor.Green, cancellationToken).ConfigureAwait(false);

                        if (risingEdge || fallingEdge)
                            await HandleGreenLidPackingSensorEdgeAsync(definition, risingEdge, fallingEdge, cancellationToken).ConfigureAwait(false);

                        if (definition.Key == FactoryIoMap.GreenLidStopRollerSensor.Key && sensor && !IsGreenLidStopSensorReleasePending())
                            await StopGreenLidRoller1ForStopSensorAsync(cancellationToken).ConfigureAwait(false);

                        if (definition.Key == FactoryIoMap.GreenLidPositionerSensor.Key && sensor && !risingEdge)
                            await ClampGreenLidPositionerAsync(cancellationToken).ConfigureAwait(false);

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
                    _machiningProgress = await _client.ReadInputRegisterAsync(
                        FactoryIoMap.MachiningProgressRegister,
                        cancellationToken).ConfigureAwait(false);
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
                AppLogger.Info("LID ROUTE Write Sensor detected. Camera inputs select sorter direction.");
                return;
            }

            if (definition.Key == FactoryIoMap.BlueLidCamera.Key && risingEdge)
            {
                await SelectLidRouteAsync(LidRouteColor.Blue, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidCamera.Key && risingEdge)
            {
                await SelectLidRouteAsync(LidRouteColor.Green, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (definition.Key != FactoryIoMap.ReadSensorSorter1.Key) return;

            if (risingEdge)
            {
                var routeActive = false;
                lock (_syncRoot)
                {
                    routeActive = _blueLidRouteState == BlueLidRouteState.Routing;
                    if (routeActive)
                        _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(SorterExitTimeoutMilliseconds);
                }
                if (routeActive) AppLogger.Info("LID ROUTE Sorter 1 sensor occupied, exit timeout refreshed");
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

                lock (_syncRoot)
                {
                    _blueLidRouteState = BlueLidRouteState.ClearingSorter;
                    _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(SorterDischargeOverrunMilliseconds);
                }
                AppLogger.Info("LID ROUTE entry sensor cleared, sorter discharge overrun started");
            }
        }

        private async Task SelectLidRouteAsync(LidRouteColor color, CancellationToken cancellationToken)
        {
            var sortingEnabled = false;
            lock (_syncRoot)
            {
                sortingEnabled = _blueLidAutoSortingEnabled && !_resetRecoveryPending;
            }
            if (!sortingEnabled)
            {
                AppLogger.Info(color + " LID ROUTE ignored camera signal while auto sorting is disabled");
                return;
            }

            await SetSorterOutputAsync(FactoryIoMap.Sorter1ForwardAndPower, true, cancellationToken).ConfigureAwait(false);
            if (color == LidRouteColor.Blue)
            {
                await SetGreenLidSorterOutputAsync(false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt1, false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, true, cancellationToken).ConfigureAwait(false);
                AppLogger.Info("BLUE LID ROUTE prepared Coil 7 ON, Coil 8 OFF, Coil 9 ON, Coil 10 OFF");
            }
            else
            {
                await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, false, cancellationToken).ConfigureAwait(false);
                await SetGreenLidSorterOutputAsync(true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt1, true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, true, cancellationToken).ConfigureAwait(false);
                await TryWriteRegisterOutputAsync(FactoryIoMap.GreenLidXSetPointRegister, GreenLidXPickValue, cancellationToken).ConfigureAwait(false);
                AppLogger.Info("GREEN LID ROUTE prepared Coil 8 ON, Coil 10/11 ON, Coil 6 ON, Coil 7 OFF, Coil 9 OFF, Holding Reg 0 = 890");
            }

            lock (_syncRoot)
            {
                _activeLidRouteColor = color;
                _blueLidRouteState = BlueLidRouteState.Routing;
                _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(SorterExitTimeoutMilliseconds);
            }
        }

        private bool ShouldRefreshLidRoute(LidRouteColor color)
        {
            lock (_syncRoot)
            {
                if (!_blueLidAutoSortingEnabled || _resetRecoveryPending) return false;
                if (_blueLidRouteState == BlueLidRouteState.Idle || _activeLidRouteColor != color) return true;
                if (color == LidRouteColor.Green)
                {
                    if (!_statuses[FactoryIoMap.Sorter1ForwardAndPower.Key].CommandState) return true;
                    return !_statuses[FactoryIoMap.Sorter1GreenLid.Key].CommandState ||
                        !_statuses[FactoryIoMap.GreenLidBelt1.Key].CommandState ||
                        !_statuses[FactoryIoMap.GreenLidBelt2.Key].CommandState ||
                        _statuses[FactoryIoMap.Sorter1BlueLid.Key].CommandState ||
                        _statuses[FactoryIoMap.BlueLidBelt1.Key].CommandState;
                }

                if (color == LidRouteColor.Blue)
                {
                    if (!_statuses[FactoryIoMap.Sorter1ForwardAndPower.Key].CommandState) return true;
                    return !_statuses[FactoryIoMap.Sorter1BlueLid.Key].CommandState ||
                        !_statuses[FactoryIoMap.BlueLidBelt1.Key].CommandState ||
                        _statuses[FactoryIoMap.Sorter1GreenLid.Key].CommandState ||
                        _statuses[FactoryIoMap.GreenLidBelt1.Key].CommandState;
                }

                return false;
            }
        }

        private async Task CheckBlueLidRouteTimersAsync(CancellationToken cancellationToken)
        {
            BlueLidRouteState expiredState;
            LidRouteColor expiredColor;
            lock (_syncRoot)
            {
                if (_blueLidRouteState == BlueLidRouteState.Idle || DateTime.UtcNow < _blueLidRouteDeadlineUtc)
                    return;
                expiredState = _blueLidRouteState;
                expiredColor = _activeLidRouteColor;
                ResetBlueLidRouteState();
            }

            if (expiredState == BlueLidRouteState.ClearingBlueLidBelt)
            {
                if (expiredColor == LidRouteColor.Green)
                {
                    await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt1, false, cancellationToken).ConfigureAwait(false);
                    await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, true, cancellationToken).ConfigureAwait(false);
                    await SetGreenLidRoller1IfStopSensorClearAsync(cancellationToken).ConfigureAwait(false);
                    await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
                    lock (_syncRoot)
                    {
                        if (_greenLidPackingState == GreenLidPackingState.Idle)
                            _greenLidPackingState = GreenLidPackingState.WaitingForLid;
                    }
                    AppLogger.Info("GREEN LID ROUTE completed Coil 10 OFF");
                    return;
                }

                await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, false, cancellationToken).ConfigureAwait(false);
                AppLogger.Info("BLUE LID ROUTE completed Coil 9 OFF");
                return;
            }

            if (expiredState == BlueLidRouteState.ClearingSorter)
            {
                if (expiredColor == LidRouteColor.Green)
                {
                    await SetGreenLidSorterOutputAsync(false, cancellationToken).ConfigureAwait(false);
                    lock (_syncRoot)
                    {
                        _activeLidRouteColor = LidRouteColor.Green;
                        _blueLidRouteState = BlueLidRouteState.ClearingBlueLidBelt;
                        _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(GreenLidBeltOverrunMilliseconds);
                    }
                    AppLogger.Info("GREEN LID ROUTE sorter discharge completed, Coil 10 overrun started");
                    return;
                }

                await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, false, cancellationToken).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _activeLidRouteColor = LidRouteColor.Blue;
                    _blueLidRouteState = BlueLidRouteState.ClearingBlueLidBelt;
                    _blueLidRouteDeadlineUtc = DateTime.UtcNow.AddMilliseconds(BlueLidBeltOverrunMilliseconds);
                }
                AppLogger.Info("BLUE LID ROUTE sorter discharge completed, Coil 9 overrun started");
                return;
            }

            if (expiredColor == LidRouteColor.Green)
            {
                await SetGreenLidSorterOutputAsync(false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt1, false, cancellationToken).ConfigureAwait(false);
            }
            else if (expiredColor == LidRouteColor.Blue)
            {
                await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, false, cancellationToken).ConfigureAwait(false);
            }

            if (_blueLidAutoSortingEnabled)
            {
                await SetSorterOutputAsync(FactoryIoMap.ExitBeltSorter1, true, cancellationToken).ConfigureAwait(false);
            }
            RaiseSorterAlarm(
                "SORTER_1_EXIT_TIMEOUT",
                false
                    ? "Blue Lid가 제한시간 안에 Sorter 1에 도착하지 않았습니다."
                    : "Blue Lid가 제한시간 안에 Sorter 1을 통과하지 못했습니다.");
        }

        private async Task HandleGreenLidPackingSensorEdgeAsync(
            EquipmentDefinition definition,
            bool risingEdge,
            bool fallingEdge,
            CancellationToken cancellationToken)
        {
            if (definition.Key == FactoryIoMap.GreenLidStopRollerSensor.Key && fallingEdge)
            {
                lock (_syncRoot)
                {
                    _greenLidStopSensorReleasePending = false;
                }
                AppLogger.Info("GREEN LID STOP SENSOR cleared, next box stop is armed");
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidPositionerClampSensor.Key && fallingEdge)
            {
                await StartGreenLidPickerMoveAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (!risingEdge) return;

            if (definition.Key == FactoryIoMap.GreenLidStopRollerSensor.Key)
            {
                if (IsGreenLidStopSensorReleasePending())
                    return;

                await StopGreenLidRoller1ForStopSensorAsync(cancellationToken).ConfigureAwait(false);

                var canDrop = false;
                lock (_syncRoot)
                {
                    canDrop = _greenLidPackingState == GreenLidPackingState.WaitingForBox;
                    if (canDrop) _greenLidPackingState = GreenLidPackingState.Dropping;
                }
                if (canDrop)
                {
                    await DropGreenLidIntoBoxAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var canWaitPositioner = false;
                lock (_syncRoot)
                {
                    canWaitPositioner = _blueLidAutoSortingEnabled &&
                        _greenLidPackingState == GreenLidPackingState.WaitingForLid;
                    if (canWaitPositioner) _greenLidPackingState = GreenLidPackingState.WaitingForPositioner;
                }
                if (!canWaitPositioner) return;

                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, false, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, false, cancellationToken).ConfigureAwait(false);
                await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
                AppLogger.Info("GREEN LID PACKING lid stopped, waiting for positioner sensor");
                if (IsSensorActive(FactoryIoMap.GreenLidPositionerSensor))
                    await ClampGreenLidPositionerAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidPositionerSensor.Key)
            {
                await ClampGreenLidPositionerAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidPositionerClampSensor.Key)
            {
                await ReleaseGreenLidPositionerClampAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (definition.Key == FactoryIoMap.GreenLidGrabSensor.Key)
            {
                var canReleaseFlow = false;
                lock (_syncRoot)
                {
                    if (_greenLidPackingState == GreenLidPackingState.Grabbing)
                    {
                        _greenLidPackingState = GreenLidPackingState.WaitingForBox;
                        canReleaseFlow = true;
                    }
                }
                if (canReleaseFlow)
                {
                    await CompleteGreenLidSetPointGrabCycleAsync(cancellationToken).ConfigureAwait(false);
                }
                AppLogger.Info("GREEN LID PACKING grab sensor detected lid");
                return;
            }

        }

        private async Task DropGreenLidIntoBoxAsync(CancellationToken cancellationToken)
        {
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, false, cancellationToken).ConfigureAwait(false);
            await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PACKING box arrived at stop roller sensor, Coil 15 OFF, Coil 16-19 ON");
            await Task.Delay(GreenLidPackingDropMilliseconds, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidGrab, false, cancellationToken).ConfigureAwait(false);
            ArmGreenLidBoxRelease();
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, true, cancellationToken).ConfigureAwait(false);
            await Task.Delay(GreenLidPackingReleaseMilliseconds, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.RightPositioner4Raise, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidPositionerClamp, false, cancellationToken).ConfigureAwait(false);
            await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, true, cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                _greenLidPackingState = GreenLidPackingState.WaitingForLid;
            }
            AppLogger.Info("GREEN LID PACKING completed, Coil 13 OFF, Coil 12 OFF, Coil 14 OFF, Coil 15 ON, Coil 16-19 ON, Coil 11 ON");
        }

        private async Task MoveGreenLidPickerToLidAsync(CancellationToken cancellationToken)
        {
            await WriteRegisterOutputAsync(FactoryIoMap.GreenLidXSetPointRegister, GreenLidXPickValue, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PICKER moving X to lid, Holding Reg 0 = 890");
            await WaitInputRegisterNearOrMovementStoppedAsync(
                FactoryIoMap.GreenLidXPositionRegister,
                GreenLidXPickValue,
                FactoryIoMap.GreenLidXMovingSensor,
                cancellationToken).ConfigureAwait(false);
            await MoveGreenLidPickerZToLidAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ClampGreenLidPositionerAsync(CancellationToken cancellationToken)
        {
            var canClamp = false;
            lock (_syncRoot)
            {
                canClamp = _blueLidAutoSortingEnabled &&
                    !_statuses[FactoryIoMap.GreenLidPositionerClamp.Key].CommandState &&
                    (_greenLidPackingState == GreenLidPackingState.Idle ||
                     _greenLidPackingState == GreenLidPackingState.WaitingForLid ||
                     _greenLidPackingState == GreenLidPackingState.WaitingForPositioner ||
                     _greenLidPackingState == GreenLidPackingState.Clamping);
                if (canClamp) _greenLidPackingState = GreenLidPackingState.Clamping;
            }
            if (!canClamp) return;

            await Task.Delay(GreenLidPackingSettleMilliseconds, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidPositionerClamp, true, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PACKING positioner sensor detected, Coil 14 ON");
            if (IsSensorActive(FactoryIoMap.GreenLidPositionerClampSensor))
                await ReleaseGreenLidPositionerClampAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task ReleaseGreenLidPositionerClampAsync(CancellationToken cancellationToken)
        {
            var canRelease = false;
            lock (_syncRoot)
            {
                canRelease = _greenLidPackingState == GreenLidPackingState.Clamping &&
                    _statuses[FactoryIoMap.GreenLidPositionerClamp.Key].CommandState;
                if (canRelease) _greenLidPackingState = GreenLidPackingState.ReleasingClamp;
            }
            if (!canRelease) return;

            await SetSorterOutputAsync(FactoryIoMap.GreenLidPositionerClamp, false, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PACKING clamp sensor detected, Coil 14 OFF, waiting for clamp release");
            await Task.Delay(GreenLidPackingSettleMilliseconds, cancellationToken).ConfigureAwait(false);
            if (!IsSensorActive(FactoryIoMap.GreenLidPositionerClampSensor))
                await StartGreenLidPickerMoveAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task StartGreenLidPickerMoveAsync(CancellationToken cancellationToken)
        {
            var canMoveToPick = false;
            lock (_syncRoot)
            {
                canMoveToPick = _greenLidPackingState == GreenLidPackingState.ReleasingClamp;
                if (canMoveToPick) _greenLidPackingState = GreenLidPackingState.MovingXToLid;
            }
            if (!canMoveToPick) return;

            AppLogger.Info("GREEN LID PACKING clamp sensor detected, moving picker X");
            await MoveGreenLidPickerToLidAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task MoveGreenLidPickerZToLidAsync(CancellationToken cancellationToken)
        {
            var canMoveZ = false;
            lock (_syncRoot)
            {
                canMoveZ = _greenLidPackingState == GreenLidPackingState.MovingXToLid;
                if (canMoveZ) _greenLidPackingState = GreenLidPackingState.MovingZToLid;
            }
            if (!canMoveZ) return;

            await WriteRegisterOutputAsync(FactoryIoMap.GreenLidZSetPointRegister, GreenLidZPickValue, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PICKER X position reached, Holding Reg 1 = 1000");
            await WaitInputRegisterNearOrMovementStoppedAsync(
                FactoryIoMap.GreenLidZPositionRegister,
                GreenLidZPickValue,
                FactoryIoMap.GreenLidZMovingSensor,
                cancellationToken).ConfigureAwait(false);
            await StartGreenLidGrabAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task StartGreenLidGrabAsync(CancellationToken cancellationToken)
        {
            var canGrab = false;
            lock (_syncRoot)
            {
                if (_greenLidPackingState == GreenLidPackingState.MovingZToLid)
                {
                    _greenLidPackingState = GreenLidPackingState.Grabbing;
                    canGrab = true;
                }
            }
            if (!canGrab) return;

            await Task.Delay(GreenLidPackingSettleMilliseconds, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidGrab, true, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PICKER Z position reached, Coil 13 ON");
        }

        private async Task CompleteGreenLidSetPointGrabCycleAsync(CancellationToken cancellationToken)
        {
            try
            {
                await WriteRegisterOutputAsync(FactoryIoMap.GreenLidZSetPointRegister, GreenLidSetPointHomeValue, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GreenLidSetPointMoveDelayMilliseconds, cancellationToken).ConfigureAwait(false);

                await WriteRegisterOutputAsync(FactoryIoMap.GreenLidXSetPointRegister, GreenLidXPlaceValue, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GreenLidSetPointMoveDelayMilliseconds, cancellationToken).ConfigureAwait(false);

                await WriteRegisterOutputAsync(FactoryIoMap.GreenLidZSetPointRegister, GreenLidZPickValue, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GreenLidSetPointMoveDelayMilliseconds, cancellationToken).ConfigureAwait(false);

                await SetSorterOutputAsync(FactoryIoMap.GreenLidGrab, false, cancellationToken).ConfigureAwait(false);
                ArmGreenLidBoxRelease();
                await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, true, cancellationToken).ConfigureAwait(false);
                await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
                await SetSorterOutputAsync(FactoryIoMap.GreenLidPositionerClamp, false, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GreenLidPackingReleaseMilliseconds, cancellationToken).ConfigureAwait(false);

                await WriteRegisterOutputAsync(FactoryIoMap.GreenLidZSetPointRegister, GreenLidSetPointHomeValue, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GreenLidSetPointMoveDelayMilliseconds, cancellationToken).ConfigureAwait(false);

                await WriteRegisterOutputAsync(FactoryIoMap.GreenLidXSetPointRegister, GreenLidSetPointHomeValue, cancellationToken).ConfigureAwait(false);
                await Task.Delay(GreenLidSetPointMoveDelayMilliseconds, cancellationToken).ConfigureAwait(false);

                await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, true, cancellationToken).ConfigureAwait(false);
                await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _greenLidPackingState = GreenLidPackingState.WaitingForLid;
                }
                AppLogger.Info("GREEN LID SETPOINT GRAB completed Holding Reg 0/1 cycle");
            }
            catch
            {
                lock (_syncRoot)
                {
                    _greenLidPackingState = GreenLidPackingState.WaitingForLid;
                }
                throw;
            }
        }

        private async Task WriteRegisterOutputAsync(int address, int value, CancellationToken cancellationToken)
        {
            await _client.WriteSingleRegisterAsync((ushort)address, (ushort)value, cancellationToken).ConfigureAwait(false);
            AppLogger.Info(string.Format("MODBUS WRITE Holding Reg {0} {1}", address, value));
        }

        private async Task TryWriteRegisterOutputAsync(int address, int value, CancellationToken cancellationToken)
        {
            try
            {
                await WriteRegisterOutputAsync(address, value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Error(string.Format(
                    "MODBUS WRITE Holding Reg {0} {1} failed. Sorter route continues.",
                    address,
                    value), ex);
            }
        }

        private async Task WaitInputRegisterNearAsync(int address, int target, CancellationToken cancellationToken)
        {
            var deadlineUtc = DateTime.UtcNow.AddMilliseconds(GreenLidPositionTimeoutMilliseconds);
            while (DateTime.UtcNow <= deadlineUtc)
            {
                var value = await _client.ReadInputRegisterAsync((ushort)address, cancellationToken).ConfigureAwait(false);
                if (Math.Abs(value - target) <= GreenLidPositionTolerance) return;
                await Task.Delay(SorterPollingMaximumMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException(string.Format("Green Lid Input Reg {0} did not reach {1}.", address, target));
        }

        private async Task WaitInputRegisterNearOrMovementStoppedAsync(
            int registerAddress,
            int target,
            EquipmentDefinition movingSensor,
            CancellationToken cancellationToken)
        {
            var deadlineUtc = DateTime.UtcNow.AddMilliseconds(GreenLidPositionTimeoutMilliseconds);
            var movementWasSeen = false;
            var stoppedSamples = 0;

            while (DateTime.UtcNow <= deadlineUtc)
            {
                var value = await _client.ReadInputRegisterAsync((ushort)registerAddress, cancellationToken).ConfigureAwait(false);
                if (Math.Abs(value - target) <= GreenLidPositionTolerance) return;

                if (movingSensor != null && movingSensor.FeedbackInputAddress >= 0)
                {
                    var moving = await _client.ReadDiscreteInputAsync((ushort)movingSensor.FeedbackInputAddress, cancellationToken).ConfigureAwait(false);
                    if (moving)
                    {
                        movementWasSeen = true;
                        stoppedSamples = 0;
                    }
                    else if (movementWasSeen)
                    {
                        stoppedSamples++;
                        if (stoppedSamples >= 2)
                        {
                            AppLogger.Info(string.Format(
                                "GREEN LID PICKER movement stopped by {0}; Input Reg {1}={2}, target={3}",
                                movingSensor.Name,
                                registerAddress,
                                value,
                                target));
                            return;
                        }
                    }
                }

                await Task.Delay(SorterPollingMaximumMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException(string.Format("Green Lid Input Reg {0} did not reach {1}.", registerAddress, target));
        }

        private async Task SetGreenLidAlwaysRollersAsync(bool value, CancellationToken cancellationToken)
        {
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller2, value, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller3, value, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller4, value, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller5, value, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetGreenLidSorterOutputAsync(bool value, CancellationToken cancellationToken)
        {
            await SetSorterOutputAsync(FactoryIoMap.Sorter1GreenLid, value, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetGreenLidRoller1IfStopSensorClearAsync(CancellationToken cancellationToken)
        {
            var stopSensorActive = false;
            lock (_syncRoot)
            {
                stopSensorActive = _statuses[FactoryIoMap.GreenLidStopRollerSensor.Key].FeedbackState;
            }
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, !stopSensorActive, cancellationToken).ConfigureAwait(false);
        }

        private bool IsSensorActive(EquipmentDefinition definition)
        {
            lock (_syncRoot)
            {
                EquipmentStatus status;
                return _statuses.TryGetValue(definition.Key, out status) && status.FeedbackState;
            }
        }

        private bool IsGreenLidStopSensorReleasePending()
        {
            lock (_syncRoot)
            {
                return _greenLidStopSensorReleasePending;
            }
        }

        private void ArmGreenLidBoxRelease()
        {
            lock (_syncRoot)
            {
                _greenLidStopSensorReleasePending = true;
            }
            AppLogger.Info("GREEN LID BOX release armed, Coil 15 ON until stop sensor clears");
        }

        private async Task StopGreenLidRoller1ForStopSensorAsync(CancellationToken cancellationToken)
        {
            var shouldStop = false;
            lock (_syncRoot)
            {
                shouldStop = _blueLidAutoSortingEnabled &&
                    _statuses[FactoryIoMap.GreenLidRoller1.Key].CommandState;
            }
            if (!shouldStop) return;

            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, false, cancellationToken).ConfigureAwait(false);
            await SetGreenLidAlwaysRollersAsync(true, cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID STOP SENSOR active, Coil 15 OFF, Coil 16-19 ON");
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
            await SetSorterOutputAsync(FactoryIoMap.ExitBeltSorter1, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.Sorter1ForwardAndPower, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.Sorter1BlueLid, false, cancellationToken).ConfigureAwait(false);
            await SetGreenLidSorterOutputAsync(false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.BlueLidBelt1, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt1, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidBelt2, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidRoller1, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.RightPositioner4Raise, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidGrab, false, cancellationToken).ConfigureAwait(false);
            await SetSorterOutputAsync(FactoryIoMap.GreenLidPositionerClamp, false, cancellationToken).ConfigureAwait(false);
            await SetGreenLidAlwaysRollersAsync(false, cancellationToken).ConfigureAwait(false);
            await ResetGreenLidSetPointsAsync(cancellationToken).ConfigureAwait(false);
            lock (_syncRoot)
            {
                ResetBlueLidRouteState();
                ResetGreenLidPackingState();
            }
        }

        private async Task ResetGreenLidSetPointsAsync(CancellationToken cancellationToken)
        {
            await WriteRegisterOutputAsync(
                FactoryIoMap.GreenLidXSetPointRegister,
                GreenLidSetPointHomeValue,
                cancellationToken).ConfigureAwait(false);
            await WriteRegisterOutputAsync(
                FactoryIoMap.GreenLidZSetPointRegister,
                GreenLidSetPointHomeValue,
                cancellationToken).ConfigureAwait(false);
            AppLogger.Info("GREEN LID PICKER set points reset Holding Reg 0/1 = 0");
        }

        private void ResetBlueLidRouteState()
        {
            _blueLidRouteState = BlueLidRouteState.Idle;
            _blueLidRouteDeadlineUtc = DateTime.MinValue;
            _activeLidRouteColor = LidRouteColor.None;
        }

        private void ResetGreenLidPackingState()
        {
            _greenLidPackingState = GreenLidPackingState.Idle;
            _greenLidStopSensorReleasePending = false;
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
            Routing,
            ClearingSorter,
            ClearingBlueLidBelt
        }

        private enum LidRouteColor
        {
            None,
            Blue,
            Green
        }

        private enum GreenLidPackingState
        {
            Idle,
            WaitingForLid,
            MovingXToLid,
            MovingZToLid,
            WaitingForPositioner,
            Clamping,
            ReleasingClamp,
            Grabbing,
            WaitingForBox,
            Dropping
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
