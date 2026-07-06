using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace CursorUsageNotify.GUI.ViewModels;

/// <summary>
/// 所有 ViewModel 的基类，提供 IMessenger 访问与 INotifyPropertyChanged 实现。
/// 继承 ObservableObject 以启用 CommunityToolkit.Mvvm 源生成器（[ObservableProperty]、[RelayCommand]）。
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>消息总线（用于订阅后台同步事件、刷新 UI）。</summary>
    protected IMessenger Messenger { get; }

    protected ViewModelBase(IMessenger messenger)
    {
        Messenger = messenger;
    }
}
