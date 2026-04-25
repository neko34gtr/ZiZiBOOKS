using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ZiZiBOOKS
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private BookmarkDict _dict;
        private bool _isInitialized = false;
        private int _editingIndex = -1; // 編集中のインデックス

        public MainWindow()
        {
            InitializeComponent();
            _settings = ConfigManager.LoadSettings();
            _dict = ConfigManager.LoadDict();
            RefreshUI();
            _isInitialized = true;
        }

        private void RefreshUI()
        {
            CreateLauncherButtons();
            CreateEditList();
            ApplySettingsToFields();
        }

        private void CreateLauncherButtons()
        {
            SemiContainer.Children.Clear();
            MajiContainer.Children.Clear();
            foreach (var item in _dict.Items)
            {
                var icon = GetIcon(!string.IsNullOrEmpty(item.IconPath) ? item.IconPath : item.Url);
                var semiBtn = new Button { Content = item.Name, Tag = icon, Style = (Style)FindResource("SemiModeStyle") };
                semiBtn.ToolTip = item.Url; // 本来のURLはToolTip等で保持
                semiBtn.Click += Launch_Click;
                // Launch_Click側で使うURLを保持するためにDataContext等も活用検討
                semiBtn.DataContext = item.Url;
                SemiContainer.Children.Add(semiBtn);

                var majiBtn = new Button { Content = item.Name, Tag = item.Url, Style = (Style)FindResource("ModernTileButton") };
                majiBtn.FontSize = _settings.FontSize;
                majiBtn.Click += Launch_Click;
                MajiContainer.Children.Add(majiBtn);
            }
        }

        private void CreateEditList()
        {
            EditListContainer.Children.Clear();
            for (int i = 0; i < _dict.Items.Count; i++)
            {
                var item = _dict.Items[i];
                var index = i;

                var border = new Border { Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(5) };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); // No
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Data
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Ops

                // Index No
                var txtNo = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
                Grid.SetColumn(txtNo, 0); grid.Children.Add(txtNo);

                // Content
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = item.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
                stack.Children.Add(new TextBlock { Text = item.Url, Foreground = Brushes.Gray, FontSize = 9 });
                Grid.SetColumn(stack, 1); grid.Children.Add(stack);

                // Operations
                var ops = new StackPanel { Orientation = Orientation.Horizontal };

                var btnUp = new Button { Content = "▲", Width = 22, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                btnUp.Click += (s, e) => MoveItem(index, -1);

                var btnDown = new Button { Content = "▼", Width = 22, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                btnDown.Click += (s, e) => MoveItem(index, 1);

                var btnEdit = new Button { Content = "編集", Width = 35, Margin = new Thickness(5, 0, 0, 0), Background = Brushes.DarkCyan, Foreground = Brushes.White, FontSize = 10, Cursor = Cursors.Hand };
                btnEdit.Click += (s, e) => StartEdit(index);

                var btnDel = new Button { Content = "消", Width = 25, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.DarkRed, Foreground = Brushes.White, FontSize = 10, Cursor = Cursors.Hand };
                btnDel.Click += (s, e) => { _dict.Items.RemoveAt(index); RefreshUI(); };

                ops.Children.Add(btnUp); ops.Children.Add(btnDown); ops.Children.Add(btnEdit); ops.Children.Add(btnDel);
                Grid.SetColumn(ops, 2); grid.Children.Add(ops);

                border.Child = grid;
                EditListContainer.Children.Add(border);
            }
        }

        private void MoveItem(int idx, int dir)
        {
            int target = idx + dir;
            if (target < 0 || target >= _dict.Items.Count) return;
            var item = _dict.Items[idx];
            _dict.Items.RemoveAt(idx);
            _dict.Items.Insert(target, item);
            RefreshUI();
        }

        private void StartEdit(int idx)
        {
            _editingIndex = idx;
            var item = _dict.Items[idx];
            NameBox.Text = item.Name;
            UrlBox.Text = item.Url;
            IconPathBox.Text = item.IconPath;
            MemoBox.Text = item.Memo;
            EditTitle.Text = "項目を編集モード";
            EditTitle.Foreground = Brushes.Yellow;
            CancelEditBtn.Visibility = Visibility.Visible;
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            _editingIndex = -1;
            NameBox.Clear(); UrlBox.Clear(); IconPathBox.Clear(); MemoBox.Clear();
            EditTitle.Text = "項目の追加 / 編集";
            EditTitle.Foreground = Brushes.Cyan;
            CancelEditBtn.Visibility = Visibility.Collapsed;
        }

        private void AddUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(UrlBox.Text)) return;

            var newItem = new BookmarkItem { Name = NameBox.Text, Url = UrlBox.Text, IconPath = IconPathBox.Text, Memo = MemoBox.Text };

            if (_editingIndex >= 0)
            {
                _dict.Items[_editingIndex] = newItem;
                _editingIndex = -1;
            }
            else
            {
                _dict.Items.Add(newItem);
            }

            NameBox.Clear(); UrlBox.Clear(); MemoBox.Clear();
            EditTitle.Text = "項目の追加 / 編集";
            EditTitle.Foreground = Brushes.Cyan;
            CancelEditBtn.Visibility = Visibility.Collapsed;
            RefreshUI();
        }

        private void ApplySettingsToFields()
        {
            this.Top = _settings.Top;
            this.Left = _settings.Left;
            FontSizeBox.Text = _settings.FontSize.ToString();
            MajiModeCheck.IsChecked = _settings.IsMajiMode;
            TopmostCheck.IsChecked = _settings.IsTopmost;
            this.Topmost = _settings.IsTopmost;
            UpdateUIMode(_settings.IsMajiMode);
        }

        private void UpdateUIMode(bool isMaji)
        {
            SemiContainer.Visibility = isMaji ? Visibility.Collapsed : Visibility.Visible;
            MajiContainer.Visibility = isMaji ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FontSizeBox.Text, out int s)) _settings.FontSize = s;
            RefreshUI();
            MessageBox.Show("設定を保存しました");
        }

        private void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is string u)
            {
                try { Process.Start(new ProcessStartInfo { FileName = u, UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            }
        }

        private ImageSource? GetIcon(string path)
        {
            try
            {

                if (string.IsNullOrWhiteSpace(path)) return null;

                // Web URLの場合（GoogleのAPIを利用してfaviconを取得）
                if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return new BitmapImage(new Uri($"https://www.google.com/s2/favicons?domain={path}&sz=64"));
                }

                // ローカルファイルの場合
                if (!System.IO.File.Exists(path)) return null;
                using (System.Drawing.Icon? icon = System.Drawing.Icon.ExtractAssociatedIcon(path))

                {
                    if (icon == null) return null;
                    return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { return null; }
        }


        private void TopmostCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            _settings.IsTopmost = TopmostCheck.IsChecked ?? false;
            this.Topmost = _settings.IsTopmost;
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e) => ConfigPanel.Visibility = (ConfigPanel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { } }
        private void ExportConfig_Click(object sender, RoutedEventArgs e) { var s = new SaveFileDialog { Filter = "dict|*.dict" }; if (s.ShowDialog() == true) ConfigManager.SaveDict(_dict); }
        private void ImportConfig_Click(object sender, RoutedEventArgs e) { var o = new OpenFileDialog { Filter = "dict|*.dict" }; if (o.ShowDialog() == true) { _dict = ConfigManager.LoadDict(); RefreshUI(); } }
        private void MajiModeCheck_Changed(object sender, RoutedEventArgs e) { if (_isInitialized) { _settings.IsMajiMode = MajiModeCheck.IsChecked ?? false; UpdateUIMode(_settings.IsMajiMode); } }
        private void Window_LocationChanged(object sender, EventArgs e) { if (_isInitialized) { _settings.Top = Top; _settings.Left = Left; } }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) { ConfigManager.SaveSettings(_settings); ConfigManager.SaveDict(_dict); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}