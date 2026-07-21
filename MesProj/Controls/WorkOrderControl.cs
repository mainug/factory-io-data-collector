using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MesProj.Models;
using MesProj.Repositories;
using MesProj.Services;

namespace MesProj.Controls
{
    public sealed class WorkOrderControl : UserControl
    {
        private readonly IWorkOrderRepository _repository;
        private readonly IProductionResultRepository _productionResultRepository;
        private readonly IApplicationStateService _stateService;
        private readonly Action<string> _setStatus;
        private readonly TextBox _workOrderNo = new TextBox();
        private readonly TextBox _itemCode = new TextBox();
        private readonly TextBox _itemName = new TextBox();
        private readonly NumericUpDown _targetQuantity = new NumericUpDown();
        private readonly DateTimePicker _startDate = new DateTimePicker();
        private readonly DateTimePicker _endDate = new DateTimePicker();
        private readonly DataGridView _grid = new DataGridView();

        public WorkOrderControl(IWorkOrderRepository repository, IProductionResultRepository productionResultRepository, IApplicationStateService stateService, Action<string> setStatus)
        {
            _repository = repository;
            _productionResultRepository = productionResultRepository;
            _stateService = stateService;
            _setStatus = setStatus;
            InitializeLayout();
            RefreshGrid();
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var input = new GroupBox { Text = "작업지시 입력", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 3, Padding = new Padding(10) };
            for (var i = 0; i < 6; i++) layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.66f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            AddLabeledControl(layout, "작업지시 번호", _workOrderNo, 0);
            AddLabeledControl(layout, "품목 코드", _itemCode, 1);
            AddLabeledControl(layout, "품목명", _itemName, 2);
            _targetQuantity.Maximum = 100000;
            _targetQuantity.Value = 100;
            AddLabeledControl(layout, "목표 생산량", _targetQuantity, 3);
            AddLabeledControl(layout, "시작 예정일", _startDate, 4);
            AddLabeledControl(layout, "종료 예정일", _endDate, 5);

            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
            AddButton(commands, "신규 작업지시 생성", CreateWorkOrder);
            AddButton(commands, "작업 시작", StartWorkOrder);
            AddButton(commands, "작업 완료", CompleteWorkOrder);
            AddButton(commands, "작업 취소", CancelWorkOrder);
            AddButton(commands, "목록 조회", delegate { RefreshGrid(); });
            layout.Controls.Add(commands, 0, 2);
            layout.SetColumnSpan(commands, 6);
            input.Controls.Add(layout);

            ConfigureGrid(_grid);
            root.Controls.Add(input, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            Controls.Add(root);
        }

        private void AddLabeledControl(TableLayoutPanel layout, string label, Control control, int column)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            panel.Controls.Add(control);
            control.Dock = DockStyle.Bottom;
            control.Height = 28;
            panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 24 });
            layout.Controls.Add(panel, column, 0);
        }

        private void AddButton(FlowLayoutPanel panel, string text, EventHandler click)
        {
            var button = new Button { Text = text, Width = 130, Height = 34, Margin = new Padding(0, 0, 8, 8) };
            button.Click += click;
            panel.Controls.Add(button);
        }

        private void ConfigureGrid(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.AutoGenerateColumns = true;
            grid.AllowUserToAddRows = false;
            grid.ReadOnly = true;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        private void CreateWorkOrder(object sender, EventArgs e)
        {
            var order = ReadInput();
            if (_repository.GetAll().Any(x => string.Equals(x.WorkOrderNo, order.WorkOrderNo, StringComparison.OrdinalIgnoreCase)))
            {
                _setStatus("이미 존재하는 작업지시 번호입니다: " + order.WorkOrderNo);
                return;
            }

            order.Status = WorkOrderStatus.Created;
            _repository.Add(order);
            RefreshGrid();
            SelectWorkOrder(order.WorkOrderNo);
            _workOrderNo.Clear();
            _setStatus("작업지시 생성: " + order.WorkOrderNo);
        }

        private void StartWorkOrder(object sender, EventArgs e)
        {
            var order = GetSelectedOrder();
            if (order == null) return;
            if (order.Status != WorkOrderStatus.Created)
            {
                _setStatus("생성 상태의 작업지시만 시작할 수 있습니다: " + order.WorkOrderNo);
                return;
            }

            order.Status = WorkOrderStatus.Running;
            order.StartedAt = DateTime.Now;
            _repository.Update(order);
            _stateService.SetCurrentWorkOrder(order);
            RefreshGrid();
            _setStatus("작업 준비 완료: " + order.WorkOrderNo + " (설비 제어에서 별도로 가동하세요.)");
        }

        private void CompleteWorkOrder(object sender, EventArgs e)
        {
            var order = GetSelectedOrder();
            if (order == null) return;
            var snapshot = _stateService.GetSnapshot();
            order.Status = WorkOrderStatus.Completed;
            order.EndedAt = DateTime.Now;
            _repository.Update(order);
            _productionResultRepository.SaveFromWorkOrder(order, snapshot.Summary.GoodQuantity, snapshot.Summary.DefectQuantity);
            _stateService.CompleteCurrentWorkOrder();
            RefreshGrid();
            _setStatus("작업 완료: " + order.WorkOrderNo);
        }

        private void CancelWorkOrder(object sender, EventArgs e)
        {
            var order = GetSelectedOrder();
            if (order == null) return;
            order.Status = WorkOrderStatus.Canceled;
            order.EndedAt = DateTime.Now;
            _repository.Update(order);
            RefreshGrid();
            _setStatus("작업 취소: " + order.WorkOrderNo);
        }

        private WorkOrder ReadInput()
        {
            return new WorkOrder
            {
                WorkOrderNo = string.IsNullOrWhiteSpace(_workOrderNo.Text) ? "WO-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") : _workOrderNo.Text.Trim(),
                ItemCode = string.IsNullOrWhiteSpace(_itemCode.Text) ? "ITEM-A" : _itemCode.Text.Trim(),
                ItemName = string.IsNullOrWhiteSpace(_itemName.Text) ? "샘플 제품" : _itemName.Text.Trim(),
                TargetQuantity = (int)_targetQuantity.Value,
                PlannedStartDate = _startDate.Value.Date,
                PlannedEndDate = _endDate.Value.Date
            };
        }

        private WorkOrder GetSelectedOrder()
        {
            if (_grid.CurrentRow == null)
            {
                _setStatus("작업지시를 선택하세요.");
                return null;
            }

            return _grid.CurrentRow.DataBoundItem as WorkOrder;
        }

        private void RefreshGrid()
        {
            _grid.DataSource = _repository.GetAll().ToList();
        }

        private void SelectWorkOrder(string workOrderNo)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var order = row.DataBoundItem as WorkOrder;
                if (order != null && order.WorkOrderNo == workOrderNo)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }
    }
}
