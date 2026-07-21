using System;
using System.Drawing;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MesProj.Infrastructure;
using MesProj.Models;
using MesProj.Services;

namespace MesProj.Controls
{
    public sealed class CommunicationSettingsControl : UserControl
    {
        private readonly ISettingsService _settingsService;
        private readonly Func<CommunicationOptions> _getOptions;
        private readonly Action<CommunicationOptions> _setOptions;
        private readonly Action<string> _setStatus;
        private readonly ComboBox _mode = new ComboBox();
        private readonly TextBox _ip = new TextBox();
        private readonly NumericUpDown _port = new NumericUpDown();
        private readonly NumericUpDown _deviceId = new NumericUpDown();
        private readonly NumericUpDown _polling = new NumericUpDown();
        private readonly NumericUpDown _timeout = new NumericUpDown();
        private readonly CheckBox _autoReconnect = new CheckBox();

        public CommunicationSettingsControl(ISettingsService settingsService, Func<CommunicationOptions> getOptions, Action<CommunicationOptions> setOptions, Action<string> setStatus)
        {
            _settingsService = settingsService;
            _getOptions = getOptions;
            _setOptions = setOptions;
            _setStatus = setStatus;
            InitializeLayout();
            LoadOptions(_getOptions());
        }

        private void InitializeLayout()
        {
            var group = new GroupBox { Text = "통신 설정", Dock = DockStyle.Top, Height = 310, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(18) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Items.AddRange(Enum.GetNames(typeof(CommunicationMode)));
            _port.Minimum = 1;
            _port.Maximum = 65535;
            _deviceId.Minimum = 1;
            _deviceId.Maximum = 247;
            _polling.Minimum = 300;
            _polling.Maximum = 60000;
            _timeout.Minimum = 500;
            _timeout.Maximum = 60000;
            _autoReconnect.Text = "자동 재연결";
            AddRow(layout, "통신 모드", _mode, 0);
            AddRow(layout, "IP 주소", _ip, 1);
            AddRow(layout, "Port", _port, 2);
            AddRow(layout, "Device ID", _deviceId, 3);
            AddRow(layout, "Polling Interval(ms)", _polling, 4);
            AddRow(layout, "Connection Timeout(ms)", _timeout, 5);
            AddRow(layout, "자동 재연결 여부", _autoReconnect, 6);
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill };
            AddButton(commands, "설정 저장", Save);
            AddButton(commands, "연결 테스트", async delegate { await TestConnectionAsync(); });
            AddButton(commands, "기본값 복원", RestoreDefaults);
            layout.Controls.Add(commands, 1, 7);
            group.Controls.Add(layout);
            Controls.Add(group);
        }

        private void AddRow(TableLayoutPanel layout, string label, Control control, int row)
        {
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        private void AddButton(FlowLayoutPanel panel, string text, EventHandler click)
        {
            var button = new Button { Text = text, Width = 105, Height = 32, Margin = new Padding(0, 0, 8, 0) };
            button.Click += click;
            panel.Controls.Add(button);
        }

        private void Save(object sender, EventArgs e)
        {
            CommunicationOptions options;
            if (!TryReadOptions(out options))
            {
                return;
            }

            string message;
            _settingsService.Save(options, out message);
            _setOptions(options);
            _setStatus(message);
        }

        private async Task TestConnectionAsync()
        {
            CommunicationOptions options;
            if (!TryReadOptions(out options))
            {
                return;
            }

            _setStatus("연결 테스트 중...");
            await Task.Delay(250, CancellationToken.None).ConfigureAwait(true);
            _setStatus(options.Mode == CommunicationMode.Mock
                ? "Mock 연결 테스트 성공"
                : "Modbus TCP는 실제 통신 구현 후 테스트 가능합니다.");
        }

        private void RestoreDefaults(object sender, EventArgs e)
        {
            var options = _settingsService.RestoreDefaults();
            LoadOptions(options);
            _setOptions(options);
            _setStatus("기본 통신 설정을 복원했습니다.");
        }

        private void LoadOptions(CommunicationOptions options)
        {
            _mode.SelectedItem = options.Mode.ToString();
            _ip.Text = options.IpAddress;
            _port.Value = options.Port;
            _deviceId.Value = options.DeviceId;
            _polling.Value = options.PollingIntervalMilliseconds;
            _timeout.Value = options.ConnectionTimeoutMilliseconds;
            _autoReconnect.Checked = options.AutoReconnect;
        }

        private bool TryReadOptions(out CommunicationOptions options)
        {
            options = null;
            IPAddress address;
            if (!IPAddress.TryParse(_ip.Text.Trim(), out address))
            {
                _setStatus("IP 주소가 올바르지 않습니다.");
                return false;
            }

            CommunicationMode mode;
            if (!Enum.TryParse(Convert.ToString(_mode.SelectedItem), out mode))
            {
                mode = CommunicationMode.Mock;
            }

            options = new CommunicationOptions
            {
                Mode = mode,
                IpAddress = _ip.Text.Trim(),
                Port = (int)_port.Value,
                DeviceId = (byte)_deviceId.Value,
                PollingIntervalMilliseconds = (int)_polling.Value,
                ConnectionTimeoutMilliseconds = (int)_timeout.Value,
                AutoReconnect = _autoReconnect.Checked
            };
            return true;
        }
    }
}
