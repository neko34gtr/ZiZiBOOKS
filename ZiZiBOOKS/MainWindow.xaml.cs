using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace ZiZiBOOKS
{
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private BookmarkDict _dict;
        private bool _isInitialized = false;
        private int _editingIndex = -1;

        // 設定画面用ドラッグ変数
        private Border? _draggedItem = null;
        private int _draggedIndex = -1;

        // メイン側D&D用の変数
        private Button? _draggedMainBtn = null;
        private int _draggedMainIndex = -1;
        private int _targetInsertIndex = -1;
        private Point _dragStartPoint;

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

                var semiBtn = new Button
                {
                    Content = item.Name,
                    Tag = icon,
                    DataContext = item.Url,
                    Style = (Style)FindResource("SemiModeStyle")
                };

                semiBtn.PreviewMouseLeftButtonDown += MainBtn_PreviewMouseLeftButtonDown;
                semiBtn.PreviewMouseMove += MainBtn_PreviewMouseMove;
                semiBtn.PreviewMouseLeftButtonUp += MainBtn_PreviewMouseLeftButtonUp;

                semiBtn.ToolTip = item.Url;
                semiBtn.Click += Launch_Click;
                SemiContainer.Children.Add(semiBtn);

                var majiBtn = new Button
                {
                    Content = item.Name,
                    Tag = item.Url,
                    DataContext = item.Url,
                    Style = (Style)FindResource("ModernTileButton")
                };
                majiBtn.FontSize = _settings.FontSize;
                majiBtn.Click += Launch_Click;
                MajiContainer.Children.Add(majiBtn);
            }
        }

        private void MainBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Button btn)
            {
                // ここではまだ CaptureMouse しない
                _dragStartPoint = e.GetPosition(this);
                _draggedMainBtn = btn;
                _draggedMainIndex = SemiContainer.Children.IndexOf(btn);
                Debug.WriteLine($"[MouseDown] {btn.Content} at {_dragStartPoint}");
            }
        }

        private void MainBtn_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedMainBtn == null) return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPos = e.GetPosition(this);
                Vector diff = _dragStartPoint - currentPos;

                // システム設定の「ドラッグとみなす最小距離」を超えたか判定（意図しないドラッグ開始を防止）
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (!_draggedMainBtn.IsMouseCaptured)
                    {
                        Debug.WriteLine("[DragStarted] しきい値を超えたためドラッグを開始");
                        _draggedMainBtn.CaptureMouse();
                        _draggedMainBtn.Opacity = 0.5;
                    }

                    Point mousePos = e.GetPosition(SemiContainer);
                    int newIndex = 0;
                    double accumulatedHeight = 0;

                    foreach (FrameworkElement child in SemiContainer.Children)
                    {
                        double childMid = accumulatedHeight + (child.ActualHeight / 2);
                        if (mousePos.Y < childMid) break;
                        accumulatedHeight += child.ActualHeight + child.Margin.Top + child.Margin.Bottom;
                        newIndex++;
                    }

                    _targetInsertIndex = Math.Clamp(newIndex, 0, SemiContainer.Children.Count);

                    InsertIndicator.Visibility = Visibility.Visible;
                    double lineY = 0;
                    if (_targetInsertIndex < SemiContainer.Children.Count)
                    {
                        var targetElement = (FrameworkElement)SemiContainer.Children[_targetInsertIndex];
                        lineY = targetElement.TransformToAncestor(SemiContainer).Transform(new Point(0, 0)).Y - targetElement.Margin.Top;
                    }
                    else if (SemiContainer.Children.Count > 0)
                    {
                        var lastElement = (FrameworkElement)SemiContainer.Children[^1];
                        lineY = lastElement.TransformToAncestor(SemiContainer).Transform(new Point(0, 0)).Y
                                + lastElement.ActualHeight + lastElement.Margin.Bottom;
                    }
                    InsertIndicator.Margin = new Thickness(0, lineY, 0, 0);
                }
            }
        }

        private void MainBtn_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedMainBtn != null)
            {
                bool wasCaptured = _draggedMainBtn.IsMouseCaptured;
                Debug.WriteLine($"[MouseUp] WasCaptured: {wasCaptured}");

                if (wasCaptured)
                {
                    _draggedMainBtn.ReleaseMouseCapture();
                    _draggedMainBtn.Opacity = 1.0;
                    InsertIndicator.Visibility = Visibility.Collapsed;

                    // キャプチャされていても移動距離が極小であれば、クリックとして救済するロジック
                    Point upPos = e.GetPosition(this);
                    Vector moveDist = _dragStartPoint - upPos;
                    if (Math.Abs(moveDist.X) < 2 && Math.Abs(moveDist.Y) < 2)
                    {
                        Debug.WriteLine("[Rescue] キャプチャされましたが移動がないためクリックとして処理します");
                        Launch_Click(_draggedMainBtn, e);
                    }
                    else
                    {
                        int oldIndex = _draggedMainIndex;
                        int newIndex = _targetInsertIndex;
                        if (newIndex > oldIndex) newIndex--;

                        if (newIndex != oldIndex && newIndex >= 0 && newIndex < _dict.Items.Count)
                        {
                            Debug.WriteLine($"[Rearrange] {oldIndex} -> {newIndex}");
                            var item = _dict.Items[oldIndex];
                            _dict.Items.RemoveAt(oldIndex);
                            _dict.Items.Insert(newIndex, item);

                            RefreshUI();
                            ConfigManager.SaveDict(_dict);
                        }
                    }

                    // ドラッグ処理（または救済処理）をした場合はクリックイベントを発生させない
                    e.Handled = true;
                }
                else
                {
                    // ドラッグしきい値を超えなかった場合は、何もせず Click イベントへ流す
                    Debug.WriteLine("[ClickConfirmed] 通常クリックとして処理");
                }

                _draggedMainBtn = null;
                _draggedMainIndex = -1;
            }
        }

        private void CreateEditList()
        {
            EditListContainer.Children.Clear();
            for (int i = 0; i < _dict.Items.Count; i++)
            {
                var item = _dict.Items[i];
                var index = i;

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 2, 0, 2),
                    Padding = new Thickness(5),
                    Tag = index,
                    Cursor = Cursors.SizeAll
                };

                border.MouseLeftButtonDown += Border_MouseLeftButtonDown;
                border.MouseMove += Border_MouseMove;
                border.MouseLeftButtonUp += Border_MouseLeftButtonUp;
                border.MouseEnter += Border_MouseEnter;

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txtNo = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
                Grid.SetColumn(txtNo, 0); grid.Children.Add(txtNo);

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock { Text = item.Name, Foreground = Brushes.White, FontWeight = FontWeights.Bold });
                stack.Children.Add(new TextBlock { Text = item.Url, Foreground = Brushes.Gray, FontSize = 9 });
                Grid.SetColumn(stack, 1); grid.Children.Add(stack);

                var ops = new StackPanel { Orientation = Orientation.Horizontal };
                var btnEdit = new Button { Content = "編集", Width = 35, Margin = new Thickness(5, 0, 0, 0), Background = Brushes.DarkCyan, Foreground = Brushes.White, FontSize = 10, Cursor = Cursors.Hand };
                btnEdit.Click += (s, e) => StartEdit(index);

                var btnDel = new Button { Content = "消", Width = 25, Margin = new Thickness(2, 0, 0, 0), Background = Brushes.DarkRed, Foreground = Brushes.White, FontSize = 10, Cursor = Cursors.Hand };
                btnDel.Click += (s, e) => { _dict.Items.RemoveAt(index); RefreshUI(); ConfigManager.SaveDict(_dict); };

                ops.Children.Add(btnEdit); ops.Children.Add(btnDel);
                Grid.SetColumn(ops, 2); grid.Children.Add(ops);

                border.Child = grid;
                EditListContainer.Children.Add(border);
            }
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                _draggedItem = border;
                _draggedIndex = (int)border.Tag;
                border.Opacity = 0.5;
                border.CaptureMouse();
            }
        }

        private void Border_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_draggedItem != null && sender is Border targetBorder && _draggedItem != targetBorder)
            {
                int targetIndex = (int)targetBorder.Tag;
                var item = _dict.Items[_draggedIndex];
                _dict.Items.RemoveAt(_draggedIndex);
                _dict.Items.Insert(targetIndex, item);
                _draggedIndex = targetIndex;
                RefreshUI();
            }
        }

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedItem != null)
            {
                _draggedItem.Opacity = 1.0;
                _draggedItem.ReleaseMouseCapture();
                ConfigManager.SaveDict(_dict);
                _draggedItem = null;
                _draggedIndex = -1;
            }
        }

        private void Border_MouseMove(object sender, MouseEventArgs e) { }

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

            CancelEdit_Click(null!, null!);
            RefreshUI();
            ConfigManager.SaveDict(_dict);
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

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FontSizeBox.Text, out int s)) _settings.FontSize = s;
            ConfigManager.SaveSettings(_settings);
            RefreshUI();

            // クールな反応: 一瞬だけ半透明にして戻す
            double originalOpacity = this.Opacity;
            this.Opacity = 0.5;
            await Task.Delay(100);
            this.Opacity = originalOpacity;
        }

        private void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is string u)
            {
                Debug.WriteLine($"[Launch_Click] URL: {u}");
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = u, UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Launch_Error] {ex.Message}");
                    MessageBox.Show(ex.Message);
                }
            }
        }

        private ImageSource? GetIcon(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return new BitmapImage(new Uri($"https://www.google.com/s2/favicons?domain={path}&sz=64"));

                if (!System.IO.File.Exists(path)) return null;
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(path))
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