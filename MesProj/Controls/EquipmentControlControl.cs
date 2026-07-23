using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MesProj.Infrastructure;
using MesProj.Models;
using MesProj.Services;

namespace MesProj.Controls
{
    public sealed class EquipmentControlControl : UserControl
    {
        private readonly IFactoryIoService _factoryIoService;
        private readonly ISettingsService _settingsService;
        private readonly IApplicationStateService _stateService;
        private readonly Func<CommunicationOptions> _getOptions;
        private readonly Action<string> _setStatus;
        private readonly DataGridView _grid = new DataGridView();
        private readonly DataGridView _sensorGrid = new DataGridView();
        private readonly DataGridView _sortingGrid = new DataGridView();
        private readonly DataGridView _sortingSensorGrid = new DataGridView();
        private readonly Label _connectionLabel = new Label();
        private readonly Label _lastCommunicationLabel = new Label();
        private readonly Label _optionsLabel = new Label();
        private readonly Label _machiningProgressLabel = new Label();
        private readonly ProgressBar _machiningProgressBar = new ProgressBar();
        private readonly Button _machiningStartButton = new Button();
        private readonly Button _machiningStopButton = new Button();
        private readonly Button _machiningResetButton = new Button();
        private readonly Button _sortingStartButton = new Button();
        private readonly Button _sortingStopButton = new Button();
        private bool _materialReady;
        private bool _automaticMachiningEnabled;
        private bool _startPulseInProgress;
        private bool _startAwaitingBusy;
        private bool _automaticSortingEnabled;
        private DateTime _lastStartPulseAt = DateTime.MinValue;
        private DateTime _lastHandledBusyTimeoutAt = DateTime.MinValue;

        public EquipmentControlControl(IFactoryIoService factoryIoService, ISettingsService settingsService, IApplicationStateService stateService, Func<CommunicationOptions> getOptions, Action<string> setStatus)
        {
            _factoryIoService = factoryIoService;
            _settingsService = settingsService;
            _stateService = stateService;
            _getOptions = getOptions;
            _setStatus = setStatus;

            InitializeLayout();
            _stateService.StateChanged += StateServiceStateChanged;
            Render(_stateService.GetSnapshot());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stateService.StateChanged -= StateServiceStateChanged;
            }

            base.Dispose(disposing);
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var connectionGroup = new GroupBox { Text = "통신 상태", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var connectionLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            connectionLayout.Controls.Add(_connectionLabel);
            connectionLayout.Controls.Add(_optionsLabel);
            connectionLayout.Controls.Add(_lastCommunicationLabel);
            AddActionButton(connectionLayout, "연결", async delegate { await RunCommandAsync(ct => _factoryIoService.ConnectAsync(_getOptions(), ct), "연결 요청 완료"); });
            AddActionButton(connectionLayout, "연결 해제", async delegate { await RunCommandAsync(ct => _factoryIoService.DisconnectAsync(ct), "연결 해제 완료"); });
            connectionGroup.Controls.Add(connectionLayout);

            var operationGroup = new GroupBox { Text = "가공 상태", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var operationLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            _machiningProgressLabel.AutoSize = true;
            _machiningProgressLabel.Margin = new Padding(0, 8, 12, 0);
            _machiningProgressBar.Width = 420;
            _machiningProgressBar.Height = 28;
            operationLayout.Controls.Add(_machiningProgressLabel);
            operationLayout.Controls.Add(_machiningProgressBar);
            _machiningStartButton.Text = "가공 시작";
            _machiningStartButton.Width = 122;
            _machiningStartButton.Height = 36;
            _machiningStartButton.Click += MachiningStartButtonClick;
            operationLayout.Controls.Add(_machiningStartButton);
            _machiningStopButton.Text = "가공 정지";
            _machiningStopButton.Width = 122;
            _machiningStopButton.Height = 36;
            _machiningStopButton.Click += MachiningStopButtonClick;
            operationLayout.Controls.Add(_machiningStopButton);
            _machiningResetButton.Text = "오류 리셋";
            _machiningResetButton.Width = 122;
            _machiningResetButton.Height = 36;
            _machiningResetButton.Click += MachiningResetButtonClick;
            operationLayout.Controls.Add(_machiningResetButton);
            operationGroup.Controls.Add(operationLayout);

            var equipmentGroup = new GroupBox { Text = "가공 도입부 I/O", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var equipmentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            equipmentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            equipmentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var actuatorGroup = new GroupBox { Text = "액추에이터 제어", Dock = DockStyle.Fill };
            var sensorGroup = new GroupBox { Text = "센서 모니터링", Dock = DockStyle.Fill };
            ConfigureGrid();
            ConfigureSensorGrid();
            actuatorGroup.Controls.Add(_grid);
            sensorGroup.Controls.Add(_sensorGrid);
            equipmentLayout.Controls.Add(actuatorGroup, 0, 0);
            equipmentLayout.Controls.Add(sensorGroup, 0, 1);
            equipmentGroup.Controls.Add(equipmentLayout);

            var machiningLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            machiningLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            machiningLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            machiningLayout.Controls.Add(operationGroup, 0, 0);
            machiningLayout.Controls.Add(equipmentGroup, 0, 1);

            var machiningTab = new TabPage("가공 공정") { Padding = new Padding(6) };
            machiningTab.Controls.Add(machiningLayout);

            var sortingEquipmentGroup = new GroupBox { Text = "분류·적재 공정 I/O", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var sortingEquipmentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            sortingEquipmentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            sortingEquipmentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            var sortingActuatorGroup = new GroupBox { Text = "액추에이터 제어", Dock = DockStyle.Fill };
            var sortingSensorGroup = new GroupBox { Text = "센서 모니터링", Dock = DockStyle.Fill };
            ConfigureEquipmentGrid(_sortingGrid);
            ConfigureSensorGrid(_sortingSensorGrid);
            sortingActuatorGroup.Controls.Add(_sortingGrid);
            sortingSensorGroup.Controls.Add(_sortingSensorGrid);
            sortingEquipmentLayout.Controls.Add(sortingActuatorGroup, 0, 0);
            sortingEquipmentLayout.Controls.Add(sortingSensorGroup, 0, 1);
            sortingEquipmentGroup.Controls.Add(sortingEquipmentLayout);

            var sortingOperationGroup = new GroupBox { Text = "Blue Lid 자동 분류", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var sortingOperationLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10) };
            _sortingStartButton.Text = "자동 분류 시작";
            _sortingStartButton.Width = 130;
            _sortingStartButton.Height = 36;
            _sortingStartButton.Click += SortingStartButtonClick;
            _sortingStopButton.Text = "자동 분류 중지";
            _sortingStopButton.Width = 130;
            _sortingStopButton.Height = 36;
            _sortingStopButton.Click += SortingStopButtonClick;
            sortingOperationLayout.Controls.Add(_sortingStartButton);
            sortingOperationLayout.Controls.Add(_sortingStopButton);
            sortingOperationGroup.Controls.Add(sortingOperationLayout);

            var sortingLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            sortingLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            sortingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sortingLayout.Controls.Add(sortingOperationGroup, 0, 0);
            sortingLayout.Controls.Add(sortingEquipmentGroup, 0, 1);

            var sortingTab = new TabPage("분류·적재 공정") { Padding = new Padding(6) };
            sortingTab.Controls.Add(sortingLayout);

            var processTabs = new TabControl { Dock = DockStyle.Fill };
            processTabs.TabPages.Add(machiningTab);
            processTabs.TabPages.Add(sortingTab);

            root.Controls.Add(connectionGroup, 0, 0);
            root.Controls.Add(processTabs, 0, 1);
            Controls.Add(root);
        }

        private void AddActionButton(FlowLayoutPanel panel, string text, EventHandler click)
        {
            var button = new Button { Text = text, Width = 122, Height = 36, Margin = new Padding(0, 0, 8, 8) };
            button.Click += click;
            panel.Controls.Add(button);
        }

        private void ConfigureGrid()
        {
            ConfigureEquipmentGrid(_grid);
        }

        private void ConfigureEquipmentGrid(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "공정 구간", DataPropertyName = "Section" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "설비명", DataPropertyName = "Name" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "현재 명령 상태", DataPropertyName = "CommandStateText" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "I/O 주소", DataPropertyName = "AddressText" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "마지막 변경 시각", DataPropertyName = "LastChangedAt" });
            var on = new DataGridViewButtonColumn { HeaderText = "ON 설정", Text = "ON", UseColumnTextForButtonValue = true };
            var off = new DataGridViewButtonColumn { HeaderText = "OFF 설정", Text = "OFF", UseColumnTextForButtonValue = true };
            grid.Columns.Add(on);
            grid.Columns.Add(off);
            grid.CellContentClick += GridCellContentClick;
        }

        private void ConfigureSensorGrid()
        {
            ConfigureSensorGrid(_sensorGrid);
        }

        private static void ConfigureSensorGrid(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "센서명", DataPropertyName = "Name" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "현재 상태", DataPropertyName = "StateText" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "I/O 주소", DataPropertyName = "AddressText" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "마지막 변경 시각", DataPropertyName = "LastChangedAt" });
        }

        private async void GridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || (e.ColumnIndex != 5 && e.ColumnIndex != 6))
            {
                return;
            }

            var sourceGrid = sender as DataGridView;
            var row = sourceGrid == null ? null : sourceGrid.Rows[e.RowIndex].DataBoundItem as EquipmentRow;
            if (row == null || row.OutputAddress < 0)
            {
                _setStatus("해당 설비는 출력 Coil 주소가 없어 직접 제어하지 않습니다.");
                return;
            }

            var value = e.ColumnIndex == 5;
            var action = row.OutputAddress == 1
                ? (value ? "Lid 선택" : "Base 선택")
                : (value ? "ON" : "OFF");
            await RunCommandAsync(ct => _factoryIoService.WriteCoilAsync(row.OutputAddress, value, ct), row.Name + " " + action);
        }

        private async void MachiningStartButtonClick(object sender, EventArgs e)
        {
            if (_automaticMachiningEnabled)
            {
                _automaticMachiningEnabled = false;
                _startAwaitingBusy = false;
                _machiningStartButton.Text = "자동 가공 시작";
                _setStatus("자동 가공 모드가 중지되었습니다.");
                return;
            }

            var snapshot = _stateService.GetSnapshot();
            if (snapshot.Summary.ConnectionState != FactoryConnectionState.Connected)
            {
                _setStatus("Factory I/O 연결 후 가공을 시작할 수 있습니다.");
                return;
            }

            _automaticMachiningEnabled = true;
            _machiningStartButton.Text = "자동 가공 중지";
            _setStatus(_materialReady
                ? "자동 가공 모드가 시작되었습니다."
                : "자동 가공 모드가 시작되었습니다. 원소재를 기다리는 중입니다.");
            var entranceOccupied = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningEntranceSensor" && x.FeedbackState);
            var entranceBeltRunning = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningEntranceBelt" && x.CommandState);
            if (!entranceOccupied && !entranceBeltRunning)
            {
                try
                {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        await _factoryIoService.WriteCoilAsync(
                            FactoryIoMap.MachiningEntranceBelt.OutputAddress,
                            true,
                            cts.Token);
                    }
                    _setStatus("자동 가공 모드가 시작되었습니다. 원소재를 기다리는 중입니다.");
                }
                catch (Exception ex)
                {
                    _automaticMachiningEnabled = false;
                    _machiningStartButton.Text = "자동 가공 시작";
                    AppLogger.Error("Automatic entrance belt start failed.", ex);
                    _setStatus(ex.Message);
                    return;
                }
            }
            await TryStartMachiningAsync(snapshot);
        }

        private async Task TryStartMachiningAsync(AppStateSnapshot snapshot)
        {
            var machiningBusy = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningBusy" && x.FeedbackState);
            var machiningError = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningError" && x.FeedbackState);
            var machiningOpened = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningOpened" && x.FeedbackState);
            if (!_automaticMachiningEnabled || _startPulseInProgress || !_materialReady || machiningBusy || machiningError || !machiningOpened)
                return;
            if (_startAwaitingBusy && DateTime.Now - _lastStartPulseAt < TimeSpan.FromSeconds(3))
                return;

            _startPulseInProgress = true;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await _factoryIoService.WriteCoilAsync(2, true, cts.Token);
                    _startAwaitingBusy = true;
                    _lastStartPulseAt = DateTime.Now;
                    try
                    {
                        await Task.Delay(200, cts.Token);
                    }
                    finally
                    {
                        await _factoryIoService.WriteCoilAsync(2, false, CancellationToken.None);
                    }
                }
                _setStatus("자동 가공 시작 신호 전송 완료");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Automatic machining start failed.", ex);
                _setStatus(ex.Message);
            }
            finally
            {
                _startPulseInProgress = false;
            }
        }

        private async void SortingStartButtonClick(object sender, EventArgs e)
        {
            await SetAutomaticSortingAsync(true);
        }

        private async void SortingStopButtonClick(object sender, EventArgs e)
        {
            await SetAutomaticSortingAsync(false);
        }

        private async Task SetAutomaticSortingAsync(bool enabled)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await _factoryIoService.SetBlueLidAutoSortingEnabledAsync(enabled, cts.Token);
                }

                _automaticSortingEnabled = enabled;
                _setStatus(enabled
                    ? "Blue Lid 자동 분류를 시작했습니다."
                    : "Blue Lid 자동 분류를 중지하고 Sorter 출력을 안전 해제했습니다.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Automatic sorting mode change failed.", ex);
                _setStatus(ex.Message);
            }

            UpdateSortingButtons(_stateService.GetSnapshot().Summary.ConnectionState);
        }

        private async void MachiningStopButtonClick(object sender, EventArgs e)
        {
            _automaticMachiningEnabled = false;
            _startAwaitingBusy = false;
            _machiningStartButton.Text = "자동 가공 시작";
            await SendPulseAsync(FactoryIoMap.MachiningStop.OutputAddress, "가공 정지 신호 전송 완료");
        }

        private async void MachiningResetButtonClick(object sender, EventArgs e)
        {
            _automaticMachiningEnabled = false;
            _materialReady = false;
            _startAwaitingBusy = false;
            _machiningStartButton.Text = "자동 가공 시작";
            await SendPulseAsync(FactoryIoMap.MachiningReset.OutputAddress, "가공기 리셋 신호 전송 완료");
        }

        private async Task SendPulseAsync(int address, string successMessage)
        {
            await RunCommandAsync(async ct =>
            {
                await _factoryIoService.WriteCoilAsync(address, true, ct);
                try
                {
                    await Task.Delay(200, ct);
                }
                finally
                {
                    await _factoryIoService.WriteCoilAsync(address, false, CancellationToken.None);
                }
            }, successMessage);
        }

        private async void BeginAutomaticMachiningStart(AppStateSnapshot snapshot)
        {
            await TryStartMachiningAsync(snapshot);
        }

        private async Task RunCommandAsync(Func<CancellationToken, Task> command, string successMessage)
        {
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await command(cts.Token).ConfigureAwait(true);
                }

                _setStatus(successMessage);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Equipment command failed.", ex);
                _setStatus(ex.Message);
            }
        }

        private void StateServiceStateChanged(object sender, AppStateSnapshot snapshot)
        {
            this.SafeInvoke(delegate { Render(snapshot); });
        }

        private void Render(AppStateSnapshot snapshot)
        {
            var busyTimeout = snapshot.RecentAlarms
                .Where(x => x.AlarmCode == "MACHINING_BUSY_TIMEOUT")
                .OrderByDescending(x => x.OccurredAt)
                .FirstOrDefault();
            if (busyTimeout != null && busyTimeout.OccurredAt > _lastHandledBusyTimeoutAt)
            {
                _lastHandledBusyTimeoutAt = busyTimeout.OccurredAt;
                _automaticMachiningEnabled = false;
                _startAwaitingBusy = false;
                _setStatus(busyTimeout.Message);
            }
            var options = _getOptions();
            _connectionLabel.Text = "연결 상태: " + snapshot.Summary.ConnectionState;
            _connectionLabel.AutoSize = true;
            _connectionLabel.Margin = new Padding(0, 8, 22, 0);
            _optionsLabel.Text = string.Format("IP: {0}   Port: {1}   Device ID: {2}", options.IpAddress, options.Port, options.DeviceId);
            _optionsLabel.AutoSize = true;
            _optionsLabel.Margin = new Padding(0, 8, 22, 0);
            _lastCommunicationLabel.Text = "마지막 통신: " + (snapshot.LastCommunicationAt == DateTime.MinValue ? "-" : snapshot.LastCommunicationAt.ToString("HH:mm:ss"));
            _lastCommunicationLabel.AutoSize = true;
            _lastCommunicationLabel.Margin = new Padding(0, 8, 22, 0);
            var progress = Math.Max(0, Math.Min(100, snapshot.MachiningProgress));
            _machiningProgressLabel.Text = "Machining Center 진행률: " + progress + "%";
            _machiningProgressBar.Value = progress;
            var entranceReady = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningEntranceSensor" && x.FeedbackState);
            if (snapshot.Summary.ConnectionState != FactoryConnectionState.Connected)
            {
                _materialReady = false;
                _automaticMachiningEnabled = false;
                _startAwaitingBusy = false;
                _automaticSortingEnabled = false;
            }
            if (entranceReady)
            {
                _materialReady = true;
            }
            var machiningBusy = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningBusy" && x.FeedbackState);
            var machiningError = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningError" && x.FeedbackState);
            if (_startAwaitingBusy && machiningBusy)
            {
                _startAwaitingBusy = false;
                _materialReady = false;
            }
            _machiningStartButton.Text = _automaticMachiningEnabled ? "자동 가공 중지" : "자동 가공 시작";
            _machiningStartButton.Enabled = snapshot.Summary.ConnectionState == FactoryConnectionState.Connected;
            _machiningStopButton.Enabled = snapshot.Summary.ConnectionState == FactoryConnectionState.Connected;
            _machiningResetButton.Enabled = snapshot.Summary.ConnectionState == FactoryConnectionState.Connected;
            UpdateSortingButtons(snapshot.Summary.ConnectionState);
            if (_automaticMachiningEnabled && _materialReady && !machiningBusy && !machiningError)
            {
                BeginAutomaticMachiningStart(snapshot);
            }
            var machiningEquipmentRows = snapshot.EquipmentStatuses
                .Where(x => x.OutputAddress >= 0 && x.OutputAddress <= 1 && !x.IsPulseOutput)
                .Select(x => new EquipmentRow(x))
                .ToList();
            var machiningSensorRows = snapshot.EquipmentStatuses
                .Where(x => x.InputAddress >= 0 && x.InputAddress <= 3)
                .Select(x => new SensorRow(x))
                .ToList();
            var sortingEquipmentRows = snapshot.EquipmentStatuses
                .Where(x => x.OutputAddress >= 5 && x.OutputAddress <= 9 && !x.IsPulseOutput)
                .Select(x => new EquipmentRow(x))
                .ToList();
            var sortingSensorRows = snapshot.EquipmentStatuses
                .Where(x => x.InputAddress >= 4 && x.InputAddress <= 7)
                .Select(x => new SensorRow(x))
                .ToList();
            BindPreservingScroll(_grid, machiningEquipmentRows);
            BindPreservingScroll(_sensorGrid, machiningSensorRows);
            BindPreservingScroll(_sortingGrid, sortingEquipmentRows);
            BindPreservingScroll(_sortingSensorGrid, sortingSensorRows);
        }

        private void UpdateSortingButtons(FactoryConnectionState connectionState)
        {
            var connected = connectionState == FactoryConnectionState.Connected;
            _sortingStartButton.Enabled = connected && !_automaticSortingEnabled;
            _sortingStopButton.Enabled = connected && _automaticSortingEnabled;
        }

        private static void BindPreservingScroll(DataGridView grid, object dataSource)
        {
            var firstRow = grid.FirstDisplayedScrollingRowIndex;
            var horizontalOffset = grid.HorizontalScrollingOffset;
            grid.DataSource = dataSource;

            if (firstRow >= 0 && firstRow < grid.RowCount)
            {
                grid.FirstDisplayedScrollingRowIndex = firstRow;
            }

            if (horizontalOffset > 0)
            {
                grid.HorizontalScrollingOffset = horizontalOffset;
            }
        }

        private sealed class SensorRow
        {
            public string Name { get; private set; }
            public string Section { get; private set; }
            public string StateText { get; private set; }
            public string AddressText { get; private set; }
            public string LastChangedAt { get; private set; }

            public SensorRow(EquipmentStatus status)
            {
                Name = status.Name;
                Section = status.Name.IndexOf("Roller", StringComparison.OrdinalIgnoreCase) >= 0 ? "박스 이송" : "가공품 공급";
                StateText = status.FeedbackState ? "ON" : "OFF";
                AddressText = "Input " + status.InputAddress;
                LastChangedAt = status.LastChangedAt.ToString("HH:mm:ss");
            }
        }

        private sealed class EquipmentRow
        {
            public string Section { get; private set; }
            public string Name { get; private set; }
            public bool CommandState { get; private set; }
            public bool FeedbackState { get; private set; }
            public int OutputAddress { get; private set; }
            public string CommandStateText
            {
                get
                {
                    if (OutputAddress == 1)
                    {
                        return CommandState ? "Lid" : "Base";
                    }

                    return CommandState ? "ON" : "OFF";
                }
            }
            public string FeedbackStateText { get { return FeedbackState ? "ON" : "OFF"; } }
            public string AddressText { get { return "Coil " + OutputAddress; } }
            public string LastChangedAt { get; private set; }

            public EquipmentRow(EquipmentStatus status)
            {
                Name = status.Name;
                Section = GetSection(status);
                CommandState = status.CommandState;
                FeedbackState = status.FeedbackState;
                OutputAddress = status.OutputAddress;
                LastChangedAt = status.LastChangedAt.ToString("HH:mm:ss");
            }

            private static string GetSection(EquipmentStatus status)
            {
                switch (status.Key)
                {
                    case "MachiningEntranceBelt":
                        return "원소재 이송";
                    case "MachiningType":
                        return "가공 종류 설정";
                    case "ExitBeltSorter1":
                        return "분류기 진입";
                    case "Sorter1ForwardAndPower":
                        return "분류기 구동";
                    case "Sorter1BlueLid":
                    case "Sorter1GreenLid":
                        return "경로 분류";
                    case "BlueLidBelt1":
                        return "Blue Lid 이송";
                    default:
                        return "설비 제어";
                }
            }
        }
    }
}
