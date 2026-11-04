using System.ComponentModel;
using AutoHDRManager.Core;
namespace AutoHDRManager.App;

public sealed class LocalizedStrings : INotifyPropertyChanged
{
    public LocalizedStrings() => Localization.Changed += () => PropertyChanged?.Invoke(this, new("Item[]"));
    public string this[string key] => Localization.Get(key);
    public event PropertyChangedEventHandler? PropertyChanged;
}
