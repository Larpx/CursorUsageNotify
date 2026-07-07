using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Larpx.PersonalTools.CursorUsageNotify.GUI.ViewModels;


namespace Larpx.PersonalTools.CursorUsageNotify.GUI
{
    /// <summary>
    /// Given a view model, returns the corresponding view if possible.
    /// </summary>
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        /// <summary>
        /// 根据传入的 ViewModel 类型名反射创建对应 View 实例；
        /// 找不到时返回提示性 TextBlock。
        /// </summary>
        /// <param name="param">
        /// ViewModel 实例。
        /// </param>
        /// <returns>
        /// 对应的 View 控件。
        /// </returns>
        public Control? Build(object? param)
        {
            if (param is null)
                return null;

            var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type != null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            return new TextBlock { Text = "Not Found: " + name };
        }

        /// <summary>
        /// 判断当前数据对象是否为 ViewModelBase 派生类型。
        /// </summary>
        /// <param name="data">
        /// 待匹配的数据对象。
        /// </param>
        /// <returns>
        /// 是 ViewModelBase 派生类型返回 true。
        /// </returns>
        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}
