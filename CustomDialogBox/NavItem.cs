namespace CustomDialogBox
{
    /// <summary>
    /// Immutable data item used for both breadcrumb segments and Quick Access entries.
    /// </summary>
    public sealed class NavItem
    {
        public NavItem(string label, string fullPath)
        {
            Label    = label;
            FullPath = fullPath;
        }

        public string Label    { get; }
        public string FullPath { get; }

        public override string ToString() => Label;
    }
}
