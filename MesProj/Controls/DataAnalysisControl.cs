using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MesProj.Infrastructure;
using MesProj.Services;

namespace MesProj.Controls
{
    public sealed class DataAnalysisControl : UserControl
    {
        private readonly ITelemetryService _telemetry;
        private readonly Label _runRate = new Label();
        private readonly Label _yieldRate = new Label();
        private readonly Label _throughput = new Label();
        private readonly Label _faults = new Label();
        private readonly Label _period = new Label();
        private readonly DataGridView _grid = new DataGridView();
        private readonly DataGridView _eventGrid = new DataGridView();

        public DataAnalysisControl(ITelemetryService telemetry)
        {
            _telemetry = telemetry;
            InitializeLayout();
            _telemetry.AnalysisChanged += AnalysisChanged;
            Render();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _telemetry.AnalysisChanged -= AnalysisChanged;
            base.Dispose(disposing);
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var cards = new FlowLayoutPanel { Dock = DockStyle.Fill };
            AddCard(cards, "가동률", _runRate);
            AddCard(cards, "수율", _yieldRate);
            AddCard(cards, "분당 생산량", _throughput);
            AddCard(cards, "고장 감지 샘플", _faults);
            _period.Dock = DockStyle.Fill;
            _period.TextAlign = ContentAlignment.MiddleLeft;
            _period.Font = new Font("맑은 고딕", 9);
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AutoGenerateColumns = true;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _eventGrid.Dock = DockStyle.Fill;
            _eventGrid.ReadOnly = true;
            _eventGrid.AllowUserToAddRows = false;
            _eventGrid.AutoGenerateColumns = true;
            _eventGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var eventTab = new TabPage("공정 이벤트");
            var telemetryTab = new TabPage("원시 텔레메트리");
            eventTab.Controls.Add(_eventGrid);
            telemetryTab.Controls.Add(_grid);
            tabs.TabPages.Add(eventTab);
            tabs.TabPages.Add(telemetryTab);
            root.Controls.Add(cards, 0, 0);
            root.Controls.Add(_period, 0, 1);
            root.Controls.Add(tabs, 0, 2);
            Controls.Add(root);
        }

        private static void AddCard(FlowLayoutPanel panel, string title, Label value)
        {
            var card = new GroupBox { Text = title, Width = 210, Height = 100, Margin = new Padding(0, 0, 12, 0), Font = new Font("맑은 고딕", 9, FontStyle.Bold) };
            value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleCenter;
            value.Font = new Font("맑은 고딕", 16, FontStyle.Bold);
            card.Controls.Add(value);
            panel.Controls.Add(card);
        }

        private void AnalysisChanged(object sender, EventArgs e) { this.SafeInvoke(Render); }

        private void Render()
        {
            var analysis = _telemetry.GetAnalysis();
            _runRate.Text = analysis.RunRatePercent.ToString("0.0") + "%";
            _yieldRate.Text = analysis.YieldRatePercent.ToString("0.0") + "%";
            _throughput.Text = analysis.UnitsPerMinute.ToString("0.0") + " EA/min";
            _faults.Text = analysis.FaultSampleCount.ToString();
            _period.Text = analysis.FirstSampleAt.HasValue
                ? string.Format("분석 구간: {0:HH:mm:ss} ~ {1:HH:mm:ss} | 샘플 {2:N0}개 | CSV: {3}", analysis.FirstSampleAt, analysis.LastSampleAt, analysis.SampleCount, _telemetry.DataDirectory)
                : "연결 후 수집된 데이터가 여기에 표시됩니다.";
            _grid.DataSource = _telemetry.GetRecentSamples(100).Select(x => new
            {
                시각 = x.Timestamp.ToString("HH:mm:ss"),
                가동 = x.IsRunning,
                총생산 = x.TotalQuantity,
                정상 = x.GoodQuantity,
                불량 = x.DefectQuantity,
                고장설비 = x.FaultEquipmentCount
            }).ToList();
            _eventGrid.DataSource = _telemetry.GetRecentProcessEvents(100).Select(x => new
            {
                시각 = x.Timestamp.ToString("HH:mm:ss.fff"),
                공정단계 = x.Stage,
                센서명 = x.SensorName,
                주소 = "Input " + x.InputAddress,
                이벤트 = x.EventType
            }).ToList();
        }
    }
}
