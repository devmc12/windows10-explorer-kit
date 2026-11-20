using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Win10ExplorerKit.UI.ViewModels
{
    internal abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>Notifies bindings when a displayed value changes</summary>
        /// <param name="name">Bound property name</param>
        protected void Changed([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
