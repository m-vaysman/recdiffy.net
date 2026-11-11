using System.Collections.Specialized;
using System.Windows;

namespace RecDiffy.ExcelAddIn.Views;

public sealed partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)EntryGrid.Items).CollectionChanged += OnEntriesChanged;
        Loaded += (_, _) => ShowNewest();
    }

    protected override void OnClosed(EventArgs e)
    {
        ((INotifyCollectionChanged)EntryGrid.Items).CollectionChanged -= OnEntriesChanged;
        base.OnClosed(e);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add)
            ShowNewest();
    }

    private void ShowNewest()
    {
        if (EntryGrid.Items.Count > 0)
            EntryGrid.ScrollIntoView(EntryGrid.Items[^1]);
    }
}
