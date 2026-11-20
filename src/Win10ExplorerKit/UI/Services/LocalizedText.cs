using System.ComponentModel;
namespace Win10ExplorerKit.UI.Services
{
    internal sealed class LocalizedText : INotifyPropertyChanged
    {
        public static LocalizedText Instance { get; } = new LocalizedText();
        public string this[string key] => TranslationService.T(key);
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>Refreshes every translated binding after the selected locale changes</summary>
        public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }
}
