using Win10ExplorerKit.Features.FileHash;
namespace Win10ExplorerKit.UI.ViewModels
{
    internal sealed class HashRowViewModel : ObservableObject
    {
        internal HashItem Item { get; }
        public string Name => System.IO.Path.GetFileName(Item.Path);
        public string Value { get; private set; }
        public bool CanCopy { get; private set; }
        /// <summary>Wraps a worker-owned result without moving filesystem work onto the UI thread</summary>
        /// <param name="item">Result published by a queue worker</param>
        internal HashRowViewModel(HashItem item) { Item = item; Refresh(); }
        /// <summary>Publishes only changed text and copy availability to virtualized rows</summary>
        internal void Refresh()
        {
            bool complete = Item.State == HashState.Complete;
            string value = complete ? Item.Value : Item.State == HashState.Failed ? TranslationService.T(Item.Error) :
                TranslationService.T("hash." + Item.State.ToString().ToLowerInvariant()) +
                (Item.State == HashState.Reading ? " " + Item.Percent() + "%" : "");
            if (value != Value) { Value = value; Changed(nameof(Value)); }
            if (complete != CanCopy) { CanCopy = complete; Changed(nameof(CanCopy)); }
        }
    }
}
