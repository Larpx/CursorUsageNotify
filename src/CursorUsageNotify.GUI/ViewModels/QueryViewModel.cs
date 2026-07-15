using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.GUI.Converters;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Export;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels
{
    /// <summary>
    /// 查询 Tab：按时间/模型筛选用量事件，分页展示，支持 CSV 导出。
    /// </summary>
    public sealed partial class QueryViewModel : ViewModelBase
    {
        private readonly IUsageRepository _repository;
        private readonly ICsvExporter _csvExporter;

        /// <summary>
        /// 当前页原始实体（仅用于 CSV 导出，不绑定到 UI）。
        /// </summary>
        private List<UsageEventEntity> _rawEntities = new();

        /// <summary>
        /// 每页行数可选项。
        /// </summary>
        public static readonly int[] PageSizeOptions = { 10, 20, 50, 100 };

        /// <summary>
        /// 构造查询 ViewModel，注入仓储、CSV 导出器与消息总线，
        /// 订阅同步事件并在初始化时加载模型列表与首屏数据。
        /// </summary>
        /// <param name="repository">
        /// 用量数据仓储。
        /// </param>
        /// <param name="csvExporter">
        /// CSV 导出器。
        /// </param>
        /// <param name="messenger">
        /// 消息总线。
        /// </param>
        public QueryViewModel(IUsageRepository repository, ICsvExporter csvExporter, IMessenger messenger)
            : base(messenger)
        {
            _repository = repository;
            _csvExporter = csvExporter;

            // 日期默认：昨天 → 今天
            StartTime = DateTime.Today.AddDays(-1);
            EndTime = DateTime.Today;

            // 平台筛选默认"全部"
            _selectedPlatformOption = PlatformOptions[0];

            Messenger.Register<UsageDataFetchedMessage>(this, async (_, _) => await LoadAsync());
            Messenger.Register<TokenFormatChangedMessage>(this, OnTokenFormatChanged);
            _ = LoadModelsAsync();
            _ = LoadAsync();
        }

        /// <summary>
        /// 每页记录数，默认 10。
        /// </summary>
        [ObservableProperty]
        private int _pageSize = 10;

        /// <summary>
        /// 每页行数选项列表（供 ComboBox 绑定）。
        /// </summary>
        public List<int> PageSizeOptionList { get; } = new(PageSizeOptions);

        /// <summary>
        /// 切换每页行数时重置到第一页并重新加载。
        /// </summary>
        partial void OnPageSizeChanged(int value)
        {
            PageIndex = 1;
            _ = LoadAsync();
        }

        /// <summary>
        /// 筛选起始时间（本地时区，按整天对齐）。
        /// </summary>
        [ObservableProperty]
        private DateTimeOffset? _startTime;

        /// <summary>
        /// 筛选结束时间（本地时区，按整天对齐）。
        /// </summary>
        [ObservableProperty]
        private DateTimeOffset? _endTime;

        /// <summary>
        /// 当前选中的模型名称（null 表示不筛选）。
        /// </summary>
        [ObservableProperty]
        private string? _selectedModel;

        /// <summary>
        /// 模型下拉选项列表。
        /// </summary>
        [ObservableProperty]
        private List<string> _models = new();

        /// <summary>
        /// 平台筛选选项列表（包含"全部"+ 各 PlatformType）。
        /// </summary>
        public List<PlatformFilterOption> PlatformOptions { get; } = PlatformFilterOption.DefaultOptions();

        /// <summary>
        /// 当前选中的平台筛选项（默认"全部"）。
        /// </summary>
        [ObservableProperty]
        private PlatformFilterOption _selectedPlatformOption;

        /// <summary>
        /// 切换平台筛选时重置到第一页并刷新模型下拉与数据。
        /// </summary>
        partial void OnSelectedPlatformOptionChanged(PlatformFilterOption value)
        {
            PageIndex = 1;
            _ = LoadModelsAsync();
            _ = LoadAsync();
        }

        /// <summary>
        /// 切换模型筛选时重置到第一页并重新加载。
        /// </summary>
        partial void OnSelectedModelChanged(string? value)
        {
            PageIndex = 1;
            _ = LoadAsync();
        }

        /// <summary>
        /// 当前页用量事件列表（预计算格式化字符串，避免滚动时转换器开销）。
        /// </summary>
        [ObservableProperty]
        private List<UsageEventDisplayModel> _events = new();

        /// <summary>
        /// 状态文本（如：共 N 条、已导出到 ...）。
        /// </summary>
        [ObservableProperty]
        private string _statusText = "共 0 条";

        // ---- 分页状态 ----

        /// <summary>
        /// 当前页码（从 1 开始）。
        /// </summary>
        [ObservableProperty]
        private int _pageIndex = 1;

        /// <summary>
        /// 满足筛选条件的总记录数。
        /// </summary>
        [ObservableProperty]
        private int _totalCount;

        /// <summary>
        /// 总页数。
        /// </summary>
        [ObservableProperty]
        private int _totalPages = 1;

        /// <summary>
        /// 是否有上一页。
        /// </summary>
        public bool CanGoPrev => PageIndex > 1;

        /// <summary>
        /// 是否有下一页。
        /// </summary>
        public bool CanGoNext => PageIndex < TotalPages;

        /// <summary>
        /// 分页摘要文字。
        /// </summary>
        public string PageSummary => TotalCount > 0
            ? $"第 {PageIndex}/{TotalPages} 页，共 {TotalCount} 条"
            : "共 0 条";

        partial void OnPageIndexChanged(int value)
        {
            OnPropertyChanged(nameof(CanGoPrev));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(PageSummary));
        }

        partial void OnTotalCountChanged(int value)
        {
            OnPropertyChanged(nameof(PageSummary));
        }

        partial void OnTotalPagesChanged(int value)
        {
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(PageSummary));
        }

        /// <summary>
        /// 重置模型筛选，默认不筛选全部模型。
        /// </summary>
        [RelayCommand]
        private void ResetModel()
        {
            SelectedModel = null;
            PageIndex = 1;
            _ = LoadAsync();
        }

        /// <summary>
        /// 上一页。
        /// </summary>
        [RelayCommand]
        private async Task GoPrevAsync()
        {
            if (!CanGoPrev) return;
            PageIndex--;
            await LoadAsync();
        }

        /// <summary>
        /// 下一页。
        /// </summary>
        [RelayCommand]
        private async Task GoNextAsync()
        {
            if (!CanGoNext) return;
            PageIndex++;
            await LoadAsync();
        }

        /// <summary>
        /// Token 格式切换后重新计算所有行的格式化 Token 字符串，替换列表触发重渲染。
        /// 预计算模式下仅重新生成字符串（无转换器调用），10~100 行开销极低。
        /// </summary>
        private void OnTokenFormatChanged(object recipient, TokenFormatChangedMessage msg)
        {
            var mode = msg.Mode;
            foreach (var item in Events)
            {
                item.RefreshTokenFormat(mode);
            }

            // 替换列表触发 DataGrid 重新绑定（不销毁视觉树，仅更新单元格文本）
            Events = new List<UsageEventDisplayModel>(Events);
        }

        /// <summary>
        /// 分页查询数据库（当前筛选条件 + 当前页码）。
        /// </summary>
        [RelayCommand]
        private async Task LoadAsync()
        {
            var startMs = StartTime.HasValue
                ? new DateTimeOffset(StartTime.Value.Date, TimeZoneInfo.Local.GetUtcOffset(StartTime.Value.Date)).ToUnixTimeMilliseconds()
                : 0;
            var endMs = EndTime.HasValue
                ? new DateTimeOffset(EndTime.Value.Date.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(EndTime.Value.Date.AddDays(1))).ToUnixTimeMilliseconds()
                : 0;

            var result = await _repository.QueryEventsPagedAsync(
                startMs, endMs, SelectedModel, PageIndex, PageSize, SelectedPlatformOption.Platform);
            _rawEntities = result.Items;
            var mode = TokenFormatConverter.Mode;
            Events = result.Items.Select(e => UsageEventDisplayModel.FromEntity(e, mode)).ToList();
            TotalCount = result.TotalCount;
            TotalPages = result.TotalPages;
            StatusText = PageSummary;
        }

        /// <summary>
        /// 加载模型下拉选项（按当前平台筛选）。
        /// </summary>
        [RelayCommand]
        private async Task LoadModelsAsync()
        {
            Models = await _repository.GetDistinctModelsAsync(SelectedPlatformOption.Platform);
        }

        /// <summary>
        /// 导出 CSV（使用 Avalonia 12 IStorageProvider API）。
        /// </summary>
        [RelayCommand]
        private async Task ExportCsvAsync()
        {
            if (_rawEntities.Count == 0)
            {
                StatusText = "没有数据可导出";
                return;
            }

            if (MainWindowInstance is null)
            {
                StatusText = "主窗口未就绪";
                return;
            }

            var topLevel = TopLevel.GetTopLevel(MainWindowInstance);
            if (topLevel is null)
            {
                StatusText = "无法获取 TopLevel";
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "选择导出位置",
                DefaultExtension = "csv",
                SuggestedFileName = $"cursor-usage-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("CSV 文件") { Patterns = new[] { "*.csv" } }
                }
            });

            if (file is null)
            {
                return;
            }

            var path = file.Path.LocalPath;
            await _csvExporter.ExportAsync(_rawEntities, path);
            StatusText = $"已导出到 {path}";
        }

        /// <summary>
        /// 由 MainWindow code-behind 设置，供 SaveFileDialog 使用。
        /// </summary>
        public static Window? MainWindowInstance { get; set; }
    }
}
