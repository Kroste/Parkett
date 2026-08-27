using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Parkett.ViewModels;

namespace Parkett;

/// <summary>Bildet ViewModels auf gleichnamige Views ab (…ViewModels.XViewModel → …Views.X).</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "Kein ViewModel" };
        }

        var name = param.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", string.Empty, StringComparison.Ordinal);

        var type = Type.GetType(name);

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"View nicht gefunden: {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
