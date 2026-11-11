using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using OMniX.WinUI.ViewModels;

namespace OMniX.WinUI.Views.Dialogs;

/// <summary>OilFX 列表行模板选择器：分组头 / mod 行用不同模板，
/// 避免 ListView 回收 container 时单模板多 Visibility 分支渲染错乱。</summary>
public sealed class OilFxRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? ModTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        return item is OilFxRowVm { IsGroupHeader: true }
            ? HeaderTemplate ?? base.SelectTemplateCore(item, container)
            : ModTemplate ?? base.SelectTemplateCore(item, container);
    }

    protected override DataTemplate SelectTemplateCore(object item) =>
        item is OilFxRowVm { IsGroupHeader: true } ? HeaderTemplate ?? ModTemplate! : ModTemplate ?? HeaderTemplate!;
}
