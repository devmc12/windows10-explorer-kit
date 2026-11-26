// Displays aggregate selection while toggling only between selecting and clearing all items
using System.Windows.Controls;

namespace Win10ExplorerKit.UI.Services
{
    public sealed class SelectAllCheckBox : CheckBox
    {
        /// <summary>Selects all from an empty or mixed state and clears all from a checked state</summary>
        protected override void OnToggle() => SetCurrentValue(IsCheckedProperty, IsChecked != true);
    }
}
