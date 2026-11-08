using System.Text.RegularExpressions;

namespace Lumina.Library;

public static partial class TitleQuery
{
    [GeneratedRegex(@"^(?<t>.*?)[\s]*(?:\((?<y>(?:19|20)\d\d)\)|(?<y2>(?:19|20)\d\d))?\s*$")]
    private static partial Regex Pattern();

    /// <summary>"Dune 2021" or "Dune (2021)" to ("Dune", 2021). A title that is only a year ("1917") stays a title.</summary>
    public static (string Title, int? Year) Split(string text)
    {
        text = text.Trim();
        var m = Pattern().Match(text);
        var title = m.Groups["t"].Value.Trim();
        var y = m.Groups["y"].Success ? m.Groups["y"].Value : m.Groups["y2"].Success ? m.Groups["y2"].Value : "";
        if (y.Length == 0 || title.Length == 0) return (text, null);
        return (title, int.Parse(y));
    }
}
