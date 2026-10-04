using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using NonetMusicPlayer.Desktop.Services;

namespace NonetMusicPlayer.Desktop.Controls;

public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public LocExtension() { }
    public LocExtension(string text) => Key = text;
    public override object ProvideValue(IServiceProvider serviceProvider) => new DynamicResourceExtension(L10n.Resource(Key)).ProvideValue(serviceProvider);
}
