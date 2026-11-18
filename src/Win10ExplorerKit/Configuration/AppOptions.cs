namespace Win10ExplorerKit.Configuration
{
    internal sealed class AppOptions
    {
        public string Language { get; set; }
        public bool QuotePaths { get; set; }
        public bool RememberFolders { get; set; }
        public string[] FixedFolders { get; set; }
    }
}
