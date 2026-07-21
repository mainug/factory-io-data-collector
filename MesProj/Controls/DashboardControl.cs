using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MesProj.Infrastructure;
using MesProj.Models;
using MesProj.Services;

namespace MesProj.Controls
{
    public sealed class DashboardControl : UserControl
    {
        private readonly IApplicationStateService _stateService;
        private readonly Dictionary<string, Label> _summaryLabels = new Dictionary<string, Label>();
        private readonly FlowLayoutPanel _equipmentPanel = new FlowLayoutPanel();
        private readonly ProgressBar _progressBar = new ProgressBar();
        private readonly Label _progressLabel = new Label();
        private readonly DataGridView _alarmGrid = new DataGridView();

        public DashboardControl(IApplicationStateService stateService)
        {
            _stateService = stateService;
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
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 204));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var summary = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(0, 0, 0, 4) };
            AddSummaryCard(summary, "설비 가동 상태", "RunState");
            AddSummaryCard(summary, "현재 생산량", "Current");
            AddSummaryCard(summary, "목표 생산량", "Target");
            AddSummaryCard(summary, "달성률", "Rate");
            AddSummaryCard(summary, "정상 생산 수량", "Good");
            AddSummaryCard(summary, "불량 수량", "Defect");
            AddSummaryCard(summary, "현재 작업지시 번호", "WorkOrder");

            var equipmentGroup = new GroupBox { Text = "설비 상태", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            _equipmentPanel.Dock = DockStyle.Fill;
            _equipmentPanel.AutoScroll = true;
            equipmentGroup.Controls.Add(_equipmentPanel);

            var progressGroup = new GroupBox { Text = "생산 진행률", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
            _progressBar.Dock = DockStyle.Top;
            _progressBar.Height = 24;
            _progressLabel.Dock = DockStyle.Fill;
            _progressLabel.TextAlign = ContentAlignment.MiddleLeft;
            _progressLabel.Font = new Font("맑은 고딕", 10);
            progressLayout.Controls.Add(_progressBar);
            progressLayout.Controls.Add(_progressLabel);
            progressGroup.Controls.Add(progressLayout);

            var alarmGroup = new GroupBox { Text = "최근 알람", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            ConfigureGrid(_alarmGrid);
            alarmGroup.Controls.Add(_alarmGrid);

            root.Controls.Add(summary, 0, 0);
            root.Controls.Add(equipmentGroup, 0, 1);
            root.Controls.Add(progressGroup, 0, 2);
            root.Controls.Add(alarmGroup, 0, 3);
            Controls.Add(root);
        }

        private void AddSummaryCard(FlowLayoutPanel panel, string title, string key)
        {
            var card = new Panel { Width = 170, Height = 92, Margin = new Padding(0, 0, 10, 10), BackColor = Color.White };
            var titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 32, Padding = new Padding(10, 8, 10, 0), ForeColor = Color.FromArgb(95, 103, 115), Font = new Font("맑은 고딕", 9) };
            var valueLabel = new Label { Text = "-", Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 8), Font = new Font("맑은 고딕", 13, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            panel.Controls.Add(card);
            _summaryLabels[key] = valueLabel;
        }

        private void ConfigureGrid(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = false;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "발생 시각", DataPropertyName = "OccurredAt" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "설비명", DataPropertyName = "EquipmentName" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "알람 코드", DataPropertyName = "AlarmCode" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "내용", DataPropertyName = "Message" });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "심각도", DataPropertyName = "Severity" });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "확인 여부", DataPropertyName = "IsAcknowledged" });
        }

        private void StateServiceStateChanged(object sender, AppStateSnapshot snapshot)
        {
            this.SafeInvoke(delegate { Render(snapshot); });
        }

        private void Render(AppStateSnapshot snapshot)
        {
            var summary = snapshot.Summary;
            _summaryLabels["RunState"].Text = summary.IsEmergencyStopped ? "비상 정지" : (summary.IsRunning ? "Running" : summary.ConnectionState.ToString());
            _summaryLabels["Current"].Text = summary.CurrentQuantity.ToString();
            _summaryLabels["Target"].Text = summary.TargetQuantity.ToString();
            _summaryLabels["Rate"].Text = summary.AchievementRate + "%";
            _summaryLabels["Good"].Text = summary.GoodQuantity.ToString();
            _summaryLabels["Defect"].Text = summary.DefectQuantity.ToString();
            _summaryLabels["WorkOrder"].Text = string.IsNullOrWhiteSpace(summary.CurrentWorkOrderNo) ? "-" : summary.CurrentWorkOrderNo;

            _progressBar.Value = Math.Max(0, Math.Min(100, summary.AchievementRate));
            _progressLabel.Text = string.Format("{0} / {1} EA    달성률 {2}%", summary.CurrentQuantity, summary.TargetQuantity, summary.AchievementRate);

            RenderEquipment(snapshot.EquipmentStatuses);
            _alarmGrid.DataSource = snapshot.RecentAlarms.Select(x => new AlarmGridRow(x)).ToList();
        }

        private void RenderEquipment(IReadOnlyList<EquipmentStatus> statuses)
        {
            _equipmentPanel.Controls.Clear();
            foreach (var status in statuses)
            {
                var panel = new Panel
                {
                    Width = 176,
                    Height = 66,
                    Margin = new Padding(0, 0, 8, 8),
                    BackColor = status.State.ToBackColor()
                };
                panel.Controls.Add(new Label { Text = status.State.ToDisplayText(), Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleCenter, ForeColor = status.State.ToForeColor(), Font = new Font("맑은 고딕", 10, FontStyle.Bold) });
                panel.Controls.Add(new Label { Text = status.Name, Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("맑은 고딕", 9, FontStyle.Bold) });
                _equipmentPanel.Controls.Add(panel);
            }
        }

        private sealed class AlarmGridRow
        {
            private readonly AlarmRecord _source;
            public AlarmGridRow(AlarmRecord source) { _source = source; }
            public string OccurredAt { get { return _source.OccurredAt.ToString("HH:mm:ss"); } }
            public string EquipmentName { get { return _source.EquipmentName; } }
            public string AlarmCode { get { return _source.AlarmCode; } }
            public string Message { get { return _source.Message; } }
            public string Severity { get { return _source.Severity; } }
            public bool IsAcknowledged { get { return _source.IsAcknowledged; } }
        }
    }
}
