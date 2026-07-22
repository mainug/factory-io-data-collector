using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MesProj.Infrastructure;
using MesProj.Models;

namespace MesProj.Services
{
    public sealed class MockFactoryIoService : IFactoryIoService
    {
        private readonly object _syncRoot = new object();
        private readonly Random _random = new Random();
        private readonly Dictionary<int, bool> _coils = new Dictionary<int, bool>();
        private readonly Dictionary<string, EquipmentStatus> _equipment = new Dictionary<string, EquipmentStatus>();
        private CancellationTokenSource _pollingCts;
        private Task _pollingTask;
        private FactoryStatus _status;
        private CommunicationOptions _options;
        private int _tick;

        public event EventHandler<FactoryStatus> StatusChanged;
        public event EventHandler<string> CommunicationError;
        public event EventHandler<ProcessEvent> ProcessEventOccurred;

        public FactoryConnectionState ConnectionState
        {
            get { lock (_syncRoot) { return _status.ConnectionState; } }
        }

        public bool IsEmergencyStopped
        {
            get { lock (_syncRoot) { return _status.IsEmergencyStopped; } }
        }

        public MockFactoryIoService()
        {
            _options = CommunicationOptions.CreateDefault();
            _status = new FactoryStatus
            {
                ConnectionState = FactoryConnectionState.Disconnected,
                LastCommunicationAt = DateTime.MinValue
            };

            foreach (var item in FactoryIoMap.AllEquipment)
            {
                _equipment[item.Key] = FactoryIoMap.CreateStatus(item, EquipmentState.Disconnected);
            }

            _status.EquipmentStatuses = _equipment.Values.ToList();
        }

        public async Task ConnectAsync(CommunicationOptions options, CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            ValidateOptions(options);

            lock (_syncRoot)
            {
                if (_status.ConnectionState == FactoryConnectionState.Connected ||
                    _status.ConnectionState == FactoryConnectionState.Connecting)
                {
                    return;
                }

                _options = options;
                _status.ConnectionState = FactoryConnectionState.Connecting;
            }

            RaiseStatusChanged();
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                _status.ConnectionState = FactoryConnectionState.Connected;
                _status.LastCommunicationAt = DateTime.Now;
                foreach (var equipment in _equipment.Values)
                {
                    equipment.State = EquipmentState.Stopped;
                    equipment.LastChangedAt = DateTime.Now;
                }
            }

            StartPollingLoop();
            RaiseStatusChanged();
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            StopPollingLoop();

            lock (_syncRoot)
            {
                _status.ConnectionState = FactoryConnectionState.Disconnected;
                _status.IsRunning = false;
                foreach (var equipment in _equipment.Values)
                {
                    equipment.State = EquipmentState.Disconnected;
                    equipment.CommandState = false;
                    equipment.FeedbackState = false;
                    equipment.LastChangedAt = DateTime.Now;
                }
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public Task<FactoryStatus> GetFactoryStatusAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                return Task.FromResult(CloneStatus());
            }
        }

        public Task<EquipmentStatus> GetEquipmentStatusAsync(string equipmentKey, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                if (!_equipment.ContainsKey(equipmentKey))
                {
                    return Task.FromResult<EquipmentStatus>(null);
                }

                return Task.FromResult(CloneEquipment(_equipment[equipmentKey]));
            }
        }

        public Task WriteCoilAsync(int address, bool value, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                if (_status.IsEmergencyStopped && value)
                {
                    throw new InvalidOperationException("비상 정지 상태에서는 가동 명령을 실행할 수 없습니다.");
                }

                _coils[address] = value;
                var target = _equipment.Values.FirstOrDefault(x => x.OutputAddress == address);
                if (target != null)
                {
                    target.CommandState = value;
                    target.FeedbackState = value && _status.ConnectionState == FactoryConnectionState.Connected;
                    target.State = target.FeedbackState ? EquipmentState.Running : EquipmentState.Stopped;
                    target.LastChangedAt = DateTime.Now;
                }
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public Task<bool> ReadDiscreteInputAsync(int address, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                return Task.FromResult(_status.IsRunning && address == FactoryIoMap.AtExitSensorInput);
            }
        }

        public Task<int> ReadHoldingRegisterAsync(int address, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                if (address == FactoryIoMap.VisionRegister)
                {
                    return Task.FromResult(_random.Next(0, 2));
                }

                return Task.FromResult(0);
            }
        }

        public Task<int> ReadInputRegisterAsync(int address, CancellationToken cancellationToken)
        {
            return ReadHoldingRegisterAsync(address, cancellationToken);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                if (_status.IsEmergencyStopped)
                {
                    throw new InvalidOperationException("비상 정지 상태에서는 전체 가동을 실행할 수 없습니다.");
                }

                _status.IsRunning = true;
                foreach (var equipment in _equipment.Values.Where(x => x.OutputAddress >= 0))
                {
                    equipment.CommandState = true;
                    equipment.FeedbackState = true;
                    equipment.State = EquipmentState.Running;
                    equipment.LastChangedAt = DateTime.Now;
                    _coils[equipment.OutputAddress] = true;
                }

                var vision = _equipment[FactoryIoMap.VisionSensor.Key];
                vision.State = EquipmentState.Running;
                vision.FeedbackState = true;
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                _status.IsRunning = false;
                foreach (var equipment in _equipment.Values)
                {
                    equipment.CommandState = false;
                    equipment.FeedbackState = false;
                    equipment.State = EquipmentState.Stopped;
                    equipment.LastChangedAt = DateTime.Now;
                }

                _coils.Clear();
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public Task EmergencyStopAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                _status.IsEmergencyStopped = true;
                _status.IsRunning = false;
                foreach (var equipment in _equipment.Values)
                {
                    equipment.CommandState = false;
                    equipment.FeedbackState = false;
                    equipment.State = EquipmentState.Stopped;
                    equipment.LastChangedAt = DateTime.Now;
                }
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public Task ResetEmergencyStopAsync(CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                _status.IsEmergencyStopped = false;
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public Task SetOperationModeAsync(OperationMode mode, CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                EnsureConnected();
                _status.OperationMode = mode;
            }

            RaiseStatusChanged();
            return Task.FromResult(0);
        }

        public void Dispose()
        {
            StopPollingLoop();
            if (_pollingCts != null)
            {
                _pollingCts.Dispose();
                _pollingCts = null;
            }
        }

        private void StartPollingLoop()
        {
            StopPollingLoop();
            _pollingCts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollAsync(_pollingCts.Token));
        }

        private void StopPollingLoop()
        {
            if (_pollingCts != null)
            {
                _pollingCts.Cancel();
            }
        }

        private async Task PollAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Math.Max(300, _options.PollingIntervalMilliseconds), cancellationToken).ConfigureAwait(false);
                    Tick();
                    RaiseStatusChanged();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Mock polling failed.", ex);
                    RaiseCommunicationError(ex.Message);
                }
            }
        }

        private void Tick()
        {
            lock (_syncRoot)
            {
                if (_status.ConnectionState != FactoryConnectionState.Connected)
                {
                    return;
                }

                _tick++;
                _status.LastCommunicationAt = DateTime.Now;

                if (_status.IsRunning && !_status.IsEmergencyStopped)
                {
                    _status.CurrentQuantity++;
                    if (_random.Next(0, 20) == 0)
                    {
                        _status.DefectQuantity++;
                    }
                    else
                    {
                        _status.GoodQuantity++;
                    }
                }

                if (_tick % 17 == 0 && _equipment.Count > 0)
                {
                    var item = _equipment.Values.ElementAt(_random.Next(_equipment.Count));
                    item.State = EquipmentState.Fault;
                    item.FeedbackState = false;
                    item.LastChangedAt = DateTime.Now;
                    RaiseCommunicationError(string.Format("[{0}] Mock 알람: 설비 상태 확인 필요", item.Name));
                }
                else if (_status.IsRunning)
                {
                    foreach (var equipment in _equipment.Values.Where(x => x.State == EquipmentState.Fault))
                    {
                        equipment.State = EquipmentState.Running;
                        equipment.FeedbackState = equipment.CommandState;
                    }
                }
            }
        }

        private void ValidateOptions(CommunicationOptions options)
        {
            System.Net.IPAddress parsedIp;
            if (!System.Net.IPAddress.TryParse(options.IpAddress, out parsedIp))
            {
                throw new ArgumentException("IP 주소가 올바르지 않습니다.");
            }

            if (options.Port <= 0 || options.Port > 65535)
            {
                throw new ArgumentException("Port는 1부터 65535 사이여야 합니다.");
            }
        }

        private void EnsureConnected()
        {
            if (_status.ConnectionState != FactoryConnectionState.Connected)
            {
                throw new InvalidOperationException("Factory I/O가 연결되어 있지 않습니다.");
            }
        }

        private void RaiseStatusChanged()
        {
            var handler = StatusChanged;
            if (handler != null)
            {
                handler(this, CloneStatus());
            }
        }

        private void RaiseCommunicationError(string message)
        {
            var handler = CommunicationError;
            if (handler != null)
            {
                handler(this, message);
            }
        }

        private FactoryStatus CloneStatus()
        {
            lock (_syncRoot)
            {
                return new FactoryStatus
                {
                    ConnectionState = _status.ConnectionState,
                    LastCommunicationAt = _status.LastCommunicationAt,
                    IsRunning = _status.IsRunning,
                    IsEmergencyStopped = _status.IsEmergencyStopped,
                    OperationMode = _status.OperationMode,
                    CurrentQuantity = _status.CurrentQuantity,
                    GoodQuantity = _status.GoodQuantity,
                    DefectQuantity = _status.DefectQuantity,
                    EquipmentStatuses = _equipment.Values.Select(CloneEquipment).ToList()
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
    }
}
