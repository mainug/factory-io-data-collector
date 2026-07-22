using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MesProj.Controls;
using MesProj.Infrastructure;
using MesProj.Models;
using MesProj.Repositories;
using MesProj.Services;

namespace MesProj.Forms
{
    public sealed class MainForm : Form
    {
        private readonly IFactoryIoService _factoryIoService;
        private readonly IApplicationStateService _stateService;
        private readonly ISettingsService _settingsService;
        private readonly IWorkOrderRepository _workOrderRepository;
        private readonly IProductionResultRepository _productionResultRepository;
        private readonly IAlarmRepository _alarmRepository;
        private readonly ITelemetryService _telemetryService;
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private readonly Panel _contentPanel = new Panel();
        private readonly StatusStrip _statusStrip = new StatusStrip();
        private readonly ToolStripStatusLabel _statusLabel = new ToolStripStatusLabel();
        private readonly Label _clockLabel = new Label();
        private readonly System.Windows.Forms.Timer _clockTimer = new System.Windows.Forms.Timer();
        private CommunicationOptions _options;

        public MainForm()
        {
            _settingsService = new JsonSettingsService();
            _options = _settingsService.Load();
            _factoryIoService = _options.Mode == CommunicationMode.ModbusTcp
                ? (IFactoryIoService)new ModbusFactoryIoService()
                : new MockFactoryIoService();
            _stateService = new ApplicationStateService();
            _workOrderRepository = new MockWorkOrderRepository();
            _productionResultRepository = new MockProductionResultRepository();
            _alarmRepository = new MockAlarmRepository();
            _telemetryService = new CsvTelemetryService(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data"));

            _factoryIoService.StatusChanged += FactoryIoServiceStatusChanged;
            _factoryIoService.CommunicationError += FactoryIoServiceCommunicationError;
            _factoryIoService.ProcessEventOccurred += FactoryIoServiceProcessEventOccurred;
            _stateService.TargetQuantityReached += StateServiceTargetQuantityReached;

            InitializeLayout();
            _stateService.ApplyFactoryStatus(_factoryIoService.GetFactoryStatusAsync(CancellationToken.None).Result);
            ShowControl(new DashboardControl(_stateService));
            SetStatus("Mock 모드 준비 완료");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposeCts.Cancel();
                _clockTimer.Stop();
                _factoryIoService.StatusChanged -= FactoryIoServiceStatusChanged;
                _factoryIoService.CommunicationError -= FactoryIoServiceCommunicationError;
                _factoryIoService.ProcessEventOccurred -= FactoryIoServiceProcessEventOccurred;
                _stateService.TargetQuantityReached -= StateServiceTargetQuantityReached;
                _factoryIoService.Dispose();
                _telemetryService.Dispose();
                _disposeCts.Dispose();
                _clockTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeLayout()
        {
            Text = "Factory I/O 생산관리 MES";
            MinimumSize = new Size(1180, 720);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

            var titlePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(33, 42, 54) };
            var title = new Label
            {
                Text = "Factory I/O 생산관리 대시보드",
                Dock = DockStyle.Left,
                Width = 520,
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 15, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0)
            };
            _clockLabel.Dock = DockStyle.Right;
            _clockLabel.Width = 260;
            _clockLabel.ForeColor = Color.White;
            _clockLabel.Font = new Font("맑은 고딕", 10, FontStyle.Regular);
            _clockLabel.TextAlign = ContentAlignment.MiddleRight;
            _clockLabel.Padding = new Padding(0, 0, 16, 0);
            titlePanel.Controls.Add(title);
            titlePanel.Controls.Add(_clockLabel);

            var menu = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(43, 52, 65),
                Padding = new Padding(12, 18, 12, 12),
                WrapContents = false
            };

            AddMenuButton(menu, "대시보드", delegate { ShowControl(new DashboardControl(_stateService)); });
            AddMenuButton(menu, "설비 제어", delegate { ShowControl(new EquipmentControlControl(_factoryIoService, _settingsService, _stateService, GetOptions, SetStatus)); });
            AddMenuButton(menu, "작업지시", delegate { ShowControl(new WorkOrderControl(_workOrderRepository, _productionResultRepository, _stateService, SetStatus)); });
            AddMenuButton(menu, "생산실적", delegate { ShowControl(new ProductionResultControl(_productionResultRepository)); });
            AddMenuButton(menu, "데이터 분석", delegate { ShowControl(new DataAnalysisControl(_telemetryService)); });
            AddMenuButton(menu, "알람 이력", delegate { ShowControl(new AlarmHistoryControl(_alarmRepository, _stateService, SetStatus)); });
            AddMenuButton(menu, "통신 설정", delegate { ShowControl(new CommunicationSettingsControl(_settingsService, GetOptions, SetOptions, SetStatus)); });

            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.BackColor = Color.FromArgb(246, 247, 249);
            _contentPanel.Padding = new Padding(14);

            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Dock = DockStyle.Fill;

            root.Controls.Add(menu, 0, 0);
            root.SetRowSpan(menu, 3);
            root.Controls.Add(titlePanel, 1, 0);
            root.Controls.Add(_contentPanel, 1, 1);
            root.Controls.Add(_statusStrip, 1, 2);

            Controls.Add(root);

            _clockTimer.Interval = 1000;
            _clockTimer.Tick += delegate { _clockLabel.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); };
            _clockTimer.Start();
        }

        private void AddMenuButton(FlowLayoutPanel menu, string text, EventHandler click)
        {
            var button = new Button
            {
                Text = text,
                Width = 154,
                Height = 44,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(57, 68, 84),
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 10, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 8)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += click;
            menu.Controls.Add(button);
        }

        private void ShowControl(UserControl control)
        {
            foreach (Control existing in _contentPanel.Controls)
            {
                existing.Dispose();
            }

            _contentPanel.Controls.Clear();
            control.Dock = DockStyle.Fill;
            _contentPanel.Controls.Add(control);
        }

        private CommunicationOptions GetOptions()
        {
            return _options;
        }

        private void SetOptions(CommunicationOptions options)
        {
            _options = options;
        }

        private void SetStatus(string message)
        {
            this.SafeInvoke(delegate { _statusLabel.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + message; });
        }

        private void FactoryIoServiceStatusChanged(object sender, FactoryStatus status)
        {
            _telemetryService.Record(status);
            _stateService.ApplyFactoryStatus(status);
        }

        private void FactoryIoServiceCommunicationError(object sender, string message)
        {
            var alarm = new AlarmRecord
            {
                OccurredAt = DateTime.Now,
                EquipmentName = "Factory I/O",
                AlarmCode = "MOCK",
                Message = message,
                Severity = message.Contains("알람") ? "Warning" : "Info",
                IsAcknowledged = false
            };

            _alarmRepository.Add(alarm);
            _stateService.AddAlarm(alarm);
            SetStatus(message);
        }

        private void FactoryIoServiceProcessEventOccurred(object sender, ProcessEvent processEvent)
        {
            _telemetryService.RecordProcessEvent(processEvent);
            SetStatus(string.Format("공정 이벤트: [{0}] {1}", processEvent.Stage, processEvent.SensorName));
        }

        private async void StateServiceTargetQuantityReached(object sender, EventArgs e)
        {
            try
            {
                var snapshot = _stateService.GetSnapshot();
                var order = _workOrderRepository.GetAll().FirstOrDefault(x => x.WorkOrderNo == snapshot.Summary.CurrentWorkOrderNo);
                if (order != null)
                {
                    order.Status = WorkOrderStatus.Completed;
                    order.EndedAt = DateTime.Now;
                    _workOrderRepository.Update(order);
                    _productionResultRepository.SaveFromWorkOrder(order, snapshot.Summary.GoodQuantity, snapshot.Summary.DefectQuantity);
                }

                await _factoryIoService.StopAsync(_disposeCts.Token).ConfigureAwait(true);
                SetStatus("목표 생산량에 도달하여 설비를 자동 정지했습니다.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("Automatic stop at target quantity failed.", ex);
                SetStatus("목표 수량 자동 정지 실패: " + ex.Message);
            }
        }
    }
}
