using Win10ExplorerKit.Features.FileHash;
namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class HashRowViewModel : ObservableObject
    {
        internal HashItem Item { get; }
        public string Name => System.IO.Path.GetFileName(Item.Path);
        public string Value { get; private set; }
        public bool CanCopy { get; private set; }
        internal HashState DisplayState { get; private set; }
        /// <summary>Wraps a worker-owned result without moving filesystem work onto the UI thread</summary>
        /// <param name="item">Result published by a queue worker</param>
        internal HashRowViewModel(HashItem item) { Item = item; Refresh(); }
        /// <summary>Publishes only changed text and copy availability to virtualized rows</summary>
        internal void Refresh()
        {
            DisplayState = Item.State;
            bool complete = DisplayState == HashState.Complete;
            string value = complete ? Item.Value : DisplayState == HashState.Failed ? TranslationService.T(Item.Error) :
                TranslationService.T("hash." + DisplayState.ToString().ToLowerInvariant()) +
                (DisplayState == HashState.Reading ? " " + Item.Percent() + "%" : "");
            if (value != Value) { Value = value; Changed(nameof(Value)); }
            if (complete != CanCopy) { CanCopy = complete; Changed(nameof(CanCopy)); }
        }
    }
}
