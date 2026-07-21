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
        private readonly Label _connectionLabel = new Label();
        private readonly Label _lastCommunicationLabel = new Label();
        private readonly Label _optionsLabel = new Label();

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

            var operationGroup = new GroupBox { Text = "운전 제어", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var operationLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            AddActionButton(operationLayout, "전체 가동", async delegate { await RunCommandAsync(ct => _factoryIoService.StartAsync(ct), "전체 가동"); });
            AddActionButton(operationLayout, "전체 정지", async delegate { await RunCommandAsync(ct => _factoryIoService.StopAsync(ct), "전체 정지"); });
            AddActionButton(operationLayout, "비상 정지", async delegate { await RunCommandAsync(ct => _factoryIoService.EmergencyStopAsync(ct), "비상 정지"); });
            AddActionButton(operationLayout, "비상 정지 해제", async delegate { await RunCommandAsync(ct => _factoryIoService.ResetEmergencyStopAsync(ct), "비상 정지 해제"); });
            AddActionButton(operationLayout, "자동 모드", async delegate { await RunCommandAsync(ct => _factoryIoService.SetOperationModeAsync(OperationMode.Auto, ct), "자동 모드"); });
            AddActionButton(operationLayout, "수동 모드", async delegate { await RunCommandAsync(ct => _factoryIoService.SetOperationModeAsync(OperationMode.Manual, ct), "수동 모드"); });
            operationGroup.Controls.Add(operationLayout);

            var equipmentGroup = new GroupBox { Text = "개별 설비 제어", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            ConfigureGrid();
            equipmentGroup.Controls.Add(_grid);

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
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "설비명", DataPropertyName = "Name" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "현재 명령 상태", DataPropertyName = "CommandStateText" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "실제 피드백 상태", DataPropertyName = "FeedbackStateText" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "I/O 주소", DataPropertyName = "AddressText" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "마지막 변경 시각", DataPropertyName = "LastChangedAt" });
            var on = new DataGridViewButtonColumn { HeaderText = "ON/Start", Text = "Start", UseColumnTextForButtonValue = true };
            var off = new DataGridViewButtonColumn { HeaderText = "OFF/Stop", Text = "Stop", UseColumnTextForButtonValue = true };
            _grid.Columns.Add(on);
            _grid.Columns.Add(off);
            _grid.CellContentClick += GridCellContentClick;
        }

        private async void GridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
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
            await RunCommandAsync(ct => _factoryIoService.WriteCoilAsync(row.OutputAddress, value, ct), row.Name + (value ? " Start" : " Stop"));
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
            _grid.DataSource = snapshot.EquipmentStatuses
                .Where(x => x.OutputAddress >= 0)
                .Select(x => new EquipmentRow(x))
                .ToList();
        }

        private sealed class EquipmentRow
        {
            public string Name { get; private set; }
            public bool CommandState { get; private set; }
            public bool FeedbackState { get; private set; }
            public int OutputAddress { get; private set; }
            public string CommandStateText { get { return CommandState ? "ON" : "OFF"; } }
            public string FeedbackStateText { get { return FeedbackState ? "ON" : "OFF"; } }
            public string AddressText { get { return OutputAddress.ToString(); } }
            public string LastChangedAt { get; private set; }

            public EquipmentRow(EquipmentStatus status)
            {
                Name = status.Name;
                CommandState = status.CommandState;
                FeedbackState = status.FeedbackState;
                OutputAddress = status.OutputAddress;
                LastChangedAt = status.LastChangedAt.ToString("HH:mm:ss");
            }
        }
    }
}
