using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.Models.Entities;
using CursorUsageNotify.Services.Export;
using CursorUsageNotify.Services.Messages;
using CursorUsageNotify.Services.Storage;

namespace CursorUsageNotify.GUI.ViewModels;

/// <summary>
/// 查询 Tab：按时间/模型筛选用量事件，分页展示，支持 CSV 导出。
/// </summary>
public sealed partial class QueryViewModel : ViewModelBase
{
    private readonly IUsageRepository _repository;
    private readonly ICsvExporter _csvExporter;

    /// <summary>每页行数可选项。</summary>
    public static readonly int[] PageSizeOptions = { 10, 20, 50, 100 };

    public QueryViewModel(IUsageRepository repository, ICsvExporter csvExporter, IMessenger messenger)
        : base(messenger)
    {
        _repository = repository;
        _csvExporter = csvExporter;
        Messenger.Register<UsageDataFetchedMessage>(this, async (_, _) => await LoadAsync());
        _ = LoadModelsAsync();
        _ = LoadAsync();
    }

    /// <summary>每页记录数，默认 10。</summary>
    [ObservableProperty]
    private int _pageSize = 10;

    /// <summary>每页行数选项列表（供 ComboBox 绑定）。</summary>
    public List<int> PageSizeOptionList { get; } = new(PageSizeOptions);

    /// <summary>切换每页行数时重置到第一页并重新加载。</summary>
    partial void OnPageSizeChanged(int value)
    {
        PageIndex = 1;
        _ = LoadAsync();
    }

    [ObservableProperty]
    private DateTimeOffset? _startTime;

    [ObservableProperty]
    private DateTimeOffset? _endTime;

    [ObservableProperty]
    private string? _selectedModel;

    [ObservableProperty]
    private List<string> _models = new();

    [ObservableProperty]
    private List<UsageEventEntity> _events = new();

    [ObservableProperty]
    private string _statusText = "共 0 条";

    // ---- 分页状态 ----

    [ObservableProperty]
    private int _pageIndex = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _totalPages = 1;

    /// <summary>是否有上一页。</summary>
    public bool CanGoPrev => PageIndex > 1;

    /// <summary>是否有下一页。</summary>
    public bool CanGoNext => PageIndex < TotalPages;

    /// <summary>分页摘要文字。</summary>
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

    /// <summary>重置模型筛选，默认不筛选全部模型。</summary>
    [RelayCommand]
    private void ResetModel()
    {
        SelectedModel = null;
        PageIndex = 1;
        _ = LoadAsync();
    }

    /// <summary>上一页。</summary>
    [RelayCommand]
    private async Task GoPrevAsync()
    {
        if (!CanGoPrev) return;
        PageIndex--;
        await LoadAsync();
    }

    /// <summary>下一页。</summary>
    [RelayCommand]
    private async Task GoNextAsync()
    {
        if (!CanGoNext) return;
        PageIndex++;
        await LoadAsync();
    }

    /// <summary>分页查询数据库（当前筛选条件 + 当前页码）。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var startMs = StartTime.HasValue
            ? new DateTimeOffset(StartTime.Value.Date, TimeZoneInfo.Local.GetUtcOffset(StartTime.Value.Date)).ToUnixTimeMilliseconds()
            : 0;
        var endMs = EndTime.HasValue
            ? new DateTimeOffset(EndTime.Value.Date.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(EndTime.Value.Date.AddDays(1))).ToUnixTimeMilliseconds()
            : 0;

        var result = await _repository.QueryEventsPagedAsync(startMs, endMs, SelectedModel, PageIndex, PageSize);
        Events = result.Items;
        TotalCount = result.TotalCount;
        TotalPages = result.TotalPages;
        StatusText = PageSummary;
    }

    /// <summary>加载模型下拉选项。</summary>
    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        Models = await _repository.GetDistinctModelsAsync();
    }

    /// <summary>导出 CSV（使用 Avalonia 12 IStorageProvider API）。</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (Events.Count == 0)
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
        await _csvExporter.ExportAsync(Events, path);
        StatusText = $"已导出到 {path}";
    }

    /// <summary>由 MainWindow code-behind 设置，供 SaveFileDialog 使用。</summary>
    public static Window? MainWindowInstance { get; set; }
}