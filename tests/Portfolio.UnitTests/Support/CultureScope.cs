using System.Globalization;

namespace Portfolio.UnitTests.Support;

/// <summary>Switches the current culture for the current async flow and restores it on dispose.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public CultureScope(string cultureName)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
    }

    public void Dispose() => CultureInfo.CurrentCulture = _previous;
}
