using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lumina;

/// <summary>The three visibility converters the templates need, as ready-made static instances.</summary>
public static class Conv
{
    sealed class Fn(Func<object?, Visibility> f) : IValueConverter
    {
        public object Convert(object? value, Type t, object? p, CultureInfo c) => f(value);
        public object ConvertBack(object? value, Type t, object? p, CultureInfo c) => throw new NotSupportedException();
    }

    /// <summary>null or empty string hides.</summary>
    public static readonly IValueConverter NullToCollapsed =
        new Fn(v => v is null or "" ? Visibility.Collapsed : Visibility.Visible);

    public static readonly IValueConverter BoolToVisible =
        new Fn(v => v is true ? Visibility.Visible : Visibility.Collapsed);

    /// <summary>Numbers above zero show; zero or less hides (progress bars with nothing to show).</summary>
    public static readonly IValueConverter PositiveToVisible =
        new Fn(v => v is double d && d > 0 ? Visibility.Visible : Visibility.Collapsed);
}
