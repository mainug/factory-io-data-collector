using System;
using System.Drawing;
using System.Windows.Forms;
using MesProj.Repositories;

namespace MesProj.Controls
{
    public sealed class ProductionResultControl : UserControl
    {
        private readonly IProductionResultRepository _repository;
        private readonly DateTimePicker _date = new DateTimePicker();
        private readonly TextBox _itemCode = new TextBox();
        private readonly TextBox _workOrderNo = new TextBox();
        private readonly CheckBox _useDate = new CheckBox();
        private readonly DataGridView _grid = new DataGridView();

        public ProductionResultControl(IProductionResultRepository repository)
        {
            _repository = repository;
            InitializeLayout();
            RefreshGrid();
        }

        private void InitializeLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filter = new GroupBox { Text = "생산실적 필터", Dock = DockStyle.Fill, Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true, WrapContents = true };
            _useDate.Text = "일자";
            _useDate.Width = 55;
            _itemCode.Width = 120;
            _workOrderNo.Width = 150;
            flow.Controls.Add(_useDate);
            flow.Controls.Add(_date);
            flow.Controls.Add(new Label { Text = "품목 코드", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
            flow.Controls.Add(_itemCode);
            flow.Controls.Add(new Label { Text = "작업지시 번호", AutoSize = true, Margin = new Padding(12, 8, 4, 0) });
            flow.Controls.Add(_workOrderNo);
            var search = new Button { Text = "조회", Width = 80, Height = 30 };
            search.Click += delegate { RefreshGrid(); };
            flow.Controls.Add(search);
            filter.Controls.Add(flow);
            ConfigureGrid();
            root.Controls.Add(filter, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            Controls.Add(root);
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
            _grid.DataSource = _repository.Search(_useDate.Checked ? (DateTime?)_date.Value.Date : null, _itemCode.Text.Trim(), _workOrderNo.Text.Trim());
        }
    }
}
