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
        private readonly Label _connectionLabel = new Label();
        private readonly Label _lastCommunicationLabel = new Label();
        private readonly Label _optionsLabel = new Label();
        private readonly Label _machiningProgressLabel = new Label();
        private readonly ProgressBar _machiningProgressBar = new ProgressBar();
        private readonly Button _machiningStartButton = new Button();
        private bool _materialReady;

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
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
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

            root.Controls.Add(connectionGroup, 0, 0);
            root.Controls.Add(operationGroup, 0, 1);
            root.Controls.Add(equipmentGroup, 0, 2);
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
            _grid.Dock = DockStyle.Fill;
            _grid.AutoGenerateColumns = false;
            _grid.AllowUserToAddRows = false;
            _grid.ReadOnly = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "공정 구간", DataPropertyName = "Section" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "설비명", DataPropertyName = "Name" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "현재 명령 상태", DataPropertyName = "CommandStateText" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "I/O 주소", DataPropertyName = "AddressText" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "마지막 변경 시각", DataPropertyName = "LastChangedAt" });
            var on = new DataGridViewButtonColumn { HeaderText = "ON 설정", Text = "ON", UseColumnTextForButtonValue = true };
            var off = new DataGridViewButtonColumn { HeaderText = "OFF 설정", Text = "OFF", UseColumnTextForButtonValue = true };
            _grid.Columns.Add(on);
            _grid.Columns.Add(off);
            _grid.CellContentClick += GridCellContentClick;
        }

        private void ConfigureSensorGrid()
        {
            _sensorGrid.Dock = DockStyle.Fill;
            _sensorGrid.AutoGenerateColumns = false;
            _sensorGrid.AllowUserToAddRows = false;
            _sensorGrid.ReadOnly = true;
            _sensorGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _sensorGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _sensorGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "센서명", DataPropertyName = "Name" });
            _sensorGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "현재 상태", DataPropertyName = "StateText" });
            _sensorGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "I/O 주소", DataPropertyName = "AddressText" });
            _sensorGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "마지막 변경 시각", DataPropertyName = "LastChangedAt" });
        }

        private async void GridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || (e.ColumnIndex != 5 && e.ColumnIndex != 6))
            {
                return;
            }

            var row = _grid.Rows[e.RowIndex].DataBoundItem as EquipmentRow;
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
            var snapshot = _stateService.GetSnapshot();
            var busy = snapshot.EquipmentStatuses.FirstOrDefault(x => x.Key == "MachiningBusy");
            var error = snapshot.EquipmentStatuses.FirstOrDefault(x => x.Key == "MachiningError");

            if (snapshot.Summary.ConnectionState != FactoryConnectionState.Connected)
            {
                _setStatus("Factory I/O 연결 후 가공을 시작할 수 있습니다.");
                return;
            }

            if (!_materialReady)
            {
                _setStatus("가공 입구를 통과한 원소재가 없습니다.");
                return;
            }

            if ((busy != null && busy.FeedbackState) || (error != null && error.FeedbackState))
            {
                _setStatus("가공기가 동작 중이거나 오류 상태입니다.");
                return;
            }

            await RunCommandAsync(async ct =>
            {
                await _factoryIoService.WriteCoilAsync(2, true, ct);
                try
                {
                    await Task.Delay(200, ct);
                }
                finally
                {
                    await _factoryIoService.WriteCoilAsync(2, false, CancellationToken.None);
                }
            }, "가공 시작 신호 전송 완료");
            _materialReady = false;
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
            }
            if (entranceReady)
            {
                _materialReady = true;
            }
            var machiningBusy = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningBusy" && x.FeedbackState);
            var machiningError = snapshot.EquipmentStatuses.Any(x => x.Key == "MachiningError" && x.FeedbackState);
            _machiningStartButton.Enabled = snapshot.Summary.ConnectionState == FactoryConnectionState.Connected
                && _materialReady && !machiningBusy && !machiningError;
            var equipmentRows = snapshot.EquipmentStatuses
                .Where(x => x.OutputAddress >= 0 && !x.IsPulseOutput)
                .Select(x => new EquipmentRow(x))
                .ToList();
            var sensorRows = snapshot.EquipmentStatuses
                .Where(x => x.InputAddress >= 0)
                .Select(x => new SensorRow(x))
                .ToList();
            BindPreservingScroll(_grid, equipmentRows);
            BindPreservingScroll(_sensorGrid, sensorRows);
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
                Section = status.OutputAddress == 0 ? "원소재 이송" : "가공 종류 설정";
                CommandState = status.CommandState;
                FeedbackState = status.FeedbackState;
                OutputAddress = status.OutputAddress;
                LastChangedAt = status.LastChangedAt.ToString("HH:mm:ss");
            }
        }
    }
}
