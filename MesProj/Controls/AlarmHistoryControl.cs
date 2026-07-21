using System;
using System.Drawing;
using System.Windows.Forms;
using MesProj.Models;
using MesProj.Repositories;
using MesProj.Services;

namespace MesProj.Controls
{
    public sealed class AlarmHistoryControl : UserControl
    {
        private readonly IAlarmRepository _repository;
        private readonly IApplicationStateService _stateService;
        private readonly Action<string> _setStatus;
        private readonly TextBox _equipmentName = new TextBox();
        private readonly ComboBox _severity = new ComboBox();
        private readonly DateTimePicker _date = new DateTimePicker();
        private readonly CheckBox _useDate = new CheckBox();
        private readonly CheckBox _onlyUnacknowledged = new CheckBox();
        private readonly DataGridView _grid = new DataGridView();

        public AlarmHistoryControl(IAlarmRepository repository, IApplicationStateService stateService, Action<string> setStatus)
        {
            _repository = repository;
            _stateService = stateService;
            _setStatus = setStatus;
            InitializeLayout();
            RefreshGrid();
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filter = new GroupBox { Text = "알람 이력 필터", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true, WrapContents = true };
            _equipmentName.Width = 130;
            _severity.Items.AddRange(new object[] { "전체", "Info", "Warning", "Critical" });
            _severity.SelectedIndex = 0;
            _severity.Width = 100;
            _useDate.Text = "날짜";
            _onlyUnacknowledged.Text = "미확인 알람만";
            _onlyUnacknowledged.Width = 120;
            flow.Controls.Add(new Label { Text = "설비명", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
            flow.Controls.Add(_equipmentName);
            flow.Controls.Add(new Label { Text = "심각도", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
            flow.Controls.Add(_severity);
            flow.Controls.Add(_useDate);
            flow.Controls.Add(_date);
            flow.Controls.Add(_onlyUnacknowledged);
            AddButton(flow, "조회", delegate { RefreshGrid(); });
            AddButton(flow, "알람 확인", AcknowledgeSelected);
            filter.Controls.Add(flow);
            ConfigureGrid();
            root.Controls.Add(filter, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            Controls.Add(root);
        }

        private void AddButton(FlowLayoutPanel panel, string text, EventHandler click)
        {
            var button = new Button { Text = text, Width = 90, Height = 30 };
            button.Click += click;
            panel.Controls.Add(button);
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AutoGenerateColumns = true;
            _grid.AllowUserToAddRows = false;
            _grid.ReadOnly = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        private void RefreshGrid()
        {
            _grid.DataSource = _repository.Search(
                _equipmentName.Text.Trim(),
                Convert.ToString(_severity.SelectedItem),
                _useDate.Checked ? (DateTime?)_date.Value.Date : null,
                _onlyUnacknowledged.Checked);
        }

        private void AcknowledgeSelected(object sender, EventArgs e)
        {
            if (_grid.CurrentRow == null)
            {
                _setStatus("확인할 알람을 선택하세요.");
                return;
            }

            var alarm = _grid.CurrentRow.DataBoundItem as AlarmRecord;
            if (alarm == null)
            {
                return;
            }

            _repository.Acknowledge(alarm);
            _stateService.AcknowledgeAlarm(alarm);
            RefreshGrid();
            _setStatus("알람 확인 처리 완료");
        }
    }
}
