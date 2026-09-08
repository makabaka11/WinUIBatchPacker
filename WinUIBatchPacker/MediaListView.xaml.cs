using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinUIBatchPacker;

public sealed partial class MediaListView : UserControl
{
    public ObservableCollection<MediaRow> Items { get; } = [];
    public string HeaderText { get => HeaderBlock?.Text ?? ""; set { if (HeaderBlock != null) HeaderBlock.Text = value; } }
    public event EventHandler? SelectionChangedByCheck;
    public event EventHandler? Reordered;
    public event EventHandler? RefreshRequested;
    public bool IsRefreshing => RefreshRing.IsActive;

    public MediaListView() { InitializeComponent(); RefreshRing.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 120, 212)); RowsList.ItemsSource = Items; }

    public void SetRefreshing(bool refreshing)
    {
        RefreshBtn.Visibility = refreshing ? Visibility.Collapsed : Visibility.Visible;
        RefreshRing.Visibility = refreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshRing.IsActive = refreshing;
    }

    public void SetItems(IEnumerable<MediaRow> rows) { Items.Clear(); foreach (var row in rows) Items.Add(row); Renumber(); SelectAllCheck.IsChecked = Items.Count > 0; }
    public List<MediaRow> SelectedRows() => Items.Where(x => x.IsSelected).ToList();
    public void Renumber() { var n = 0; foreach (var row in Items) row.Number = row.IsSelected ? (++n).ToString() : ""; SelectAllCheck.IsChecked = Items.Count > 0 && Items.All(x => x.IsSelected); }
    private void ItemCheckBox_Click(object sender, RoutedEventArgs e) { Renumber(); SelectionChangedByCheck?.Invoke(this, EventArgs.Empty); }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { var value = SelectAllCheck.IsChecked == true; foreach (var row in Items) row.IsSelected = value; Renumber(); SelectionChangedByCheck?.Invoke(this, EventArgs.Empty); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
    private void RowsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) { DispatcherQueue.TryEnqueue(() => { Renumber(); Reordered?.Invoke(this, EventArgs.Empty); }); }
    private void Up_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int offset) { if (RowsList.SelectedItem is not MediaRow row) return; var from = Items.IndexOf(row); var to = from + offset; if (to < 0 || to >= Items.Count) return; Items.Move(from, to); RowsList.SelectedItem = row; Renumber(); Reordered?.Invoke(this, EventArgs.Empty); }
}
