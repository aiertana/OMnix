using System.Reflection;
using OMniX.Core.Helpers;

namespace OMniX.Core.GamesService.Requests;

public class OverrideCharacterRequest
{
    public NewValue<string> DisplayName { get; set; }

    public NewValue<bool> IsMultiMod { get; set; }

    public NewValue<Uri?> Image { get; set; }

    public NewValue<string[]> Keys { get; set; }

    /// <summary>
    ///     二开：所属属性。空字符串表示「其他」（无属性）。
    /// </summary>
    public NewValue<string> Element { get; set; }


    public bool AnyValuesSet => GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.PropertyType.IsAssignableTo(typeof(ISettableProperty)))
        .Any(p => (p.GetValue(this) as ISettableProperty)?.IsSet == true);
}