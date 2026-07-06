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
/// 查询 Tab：按时间/模型筛选用量事件，支持 CSV 导出。
/// </summary>
public sealed partial class QueryViewModel : ViewModelBase
{
    private readonly IUsageRepository _repository;
    private readonly ICsvExporter _csvExporter;

    public QueryViewModel(IUsageRepository repository, ICsvExporter csvExporter, IMessenger messenger)
        : base(messenger)
    {
        _repository = repository;
        _csvExporter = csvExporter;
        Messenger.Register<UsageDataFetchedMessage>(this, async (_, _) => await LoadAsync());
        _ = LoadAsync();
        _ = LoadModelsAsync();
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

    /// <summary>查询数据库。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var startMs = StartTime.HasValue
            ? new DateTimeOffset(StartTime.Value.Date, TimeZoneInfo.Local.GetUtcOffset(StartTime.Value.Date)).ToUnixTimeMilliseconds()
            : 0;
        var endMs = EndTime.HasValue
            ? new DateTimeOffset(EndTime.Value.Date.AddDays(1), TimeZoneInfo.Local.GetUtcOffset(EndTime.Value.Date.AddDays(1))).ToUnixTimeMilliseconds()
            : 0;

        var events = await _repository.QueryEventsAsync(startMs, endMs, SelectedModel);
        Events = events;
        StatusText = $"共 {events.Count} 条";
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
