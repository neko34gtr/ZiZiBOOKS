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
using System.Collections.Generic;

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

        // OLED焼き付き防止用変数
        private DateTime _lastActivityTime = DateTime.Now;
        private const double ActiveOpacity = 1.0; // アクティブ時の透明度 (100%)

        public MainWindow()
        {
            InitializeComponent();
            _settings = ConfigManager.LoadSettings();
            _dict = ConfigManager.LoadDict();

            // 座標の補正ロジックを追加
            CheckAndFixWindowPosition();

            RefreshUI();
            _isInitialized = true;

            // 焼き付き防止用の監視ループ
            CompositionTarget.Rendering += OnRendering;
        }

        private void CheckAndFixWindowPosition()
        {
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHight = SystemParameters.WorkArea.Height;
            // 値がNan、または画面外(-32000など)の異常値かチェック
            bool isInvalid = double.IsNaN(_settings.Top) || double.IsNaN(_settings.Left) || _settings.Top < -10000 || _settings.Top > 20000 || _settings.Left < -10000 || _settings.Left > 20000;
            if (isInvalid)
            {
                // 画面中央付近に初期配置(例: 幅300,高さ400と仮定して計算)
                // 実際にはRefreshUIでSizeToContentが走るので大まかな位置でOK
                _settings.Left = (screenWidth - 300) / 2;
                _settings.Top = (screenHight - 400) / 2;
            }
            // ウィンドウに値を反映
            this.Left = _settings.Left;
            this.Top = _settings.Top;
        }

        // 毎フレーム呼ばれる処理で不透明度を制御
        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_isInitialized || ConfigPanel.Visibility == Visibility.Visible)
            {
                this.Opacity = ActiveOpacity;
                return;
            }

            // マウスがウィンドウ内にあるか、ドラッグ中なら操作中とみなす
            if (this.IsMouseOver || _draggedMainBtn != null || _draggedItem != null)
            {
                _lastActivityTime = DateTime.Now;
            }

            double elapsedSeconds = (DateTime.Now - _lastActivityTime).TotalSeconds;
            double duration = Math.Max(0.1, _settings.IdleSeconds); // 0除算防止
            double minOpacity = _settings.IdleOpacity;

            if (elapsedSeconds <= 0)
            {
                // 操作されたら即座に、またはふわっと戻す
                if (this.Opacity < ActiveOpacity)
                {
                    this.Opacity = Math.Min(ActiveOpacity, this.Opacity + 0.1);
                }
            }
            else
            {
                // 指定秒数かけてリニアに落とすロジック
                // 計算式：初期値 - (経過秒数 / 合計秒数) * 透明度の変化幅
                double targetOpacity = ActiveOpacity - (elapsedSeconds / duration) * (ActiveOpacity - minOpacity);
                this.Opacity = Math.Max(minOpacity, targetOpacity);
            }
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
                _lastActivityTime = DateTime.Now; // アクティビティ更新
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
                _lastActivityTime = DateTime.Now; // アクティビティ更新
                Point currentPos = e.GetPosition(this);
                Vector diff = _dragStartPoint - currentPos;

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
                _lastActivityTime = DateTime.Now; // アクティビティ更新
                bool wasCaptured = _draggedMainBtn.IsMouseCaptured;
                Debug.WriteLine($"[MouseUp] WasCaptured: {wasCaptured}");

                if (wasCaptured)
                {
                    _draggedMainBtn.ReleaseMouseCapture();
                    _draggedMainBtn.Opacity = 1.0;
                    InsertIndicator.Visibility = Visibility.Collapsed;

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
                    e.Handled = true;
                }
                else
                {
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
                _lastActivityTime = DateTime.Now;
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
                _lastActivityTime = DateTime.Now;
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
                _lastActivityTime = DateTime.Now;
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

            // 追加設定項目の反映
            IdleSecondsBox.Text = _settings.IdleSeconds.ToString();
            IdleOpacityBox.Text = _settings.IdleOpacity.ToString("0.00");

            UpdateUIMode(_settings.IsMajiMode);
        }

        private void UpdateUIMode(bool isMaji)
        {
            SemiContainer.Visibility = isMaji ? Visibility.Collapsed : Visibility.Visible;
            MajiContainer.Visibility = isMaji ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FontSizeBox.Text, out int fs))
            {
                // 16px〜48pxの範囲外は許容しない（クランプ処理）
                _settings.FontSize = Math.Max(16, Math.Min(fs, 48));
                // 補正された値をテキストボックスに書き戻す
                FontSizeBox.Text = _settings.FontSize.ToString();
            }
            else
            {
                // 数値以外が入力された場合はデフォルトの16pxを適用
                _settings.FontSize = 16;
            }

            // 追加設定項目の取得とバリデーション
            if (int.TryParse(IdleSecondsBox.Text, out int sec)) _settings.IdleSeconds = sec;
            if (double.TryParse(IdleOpacityBox.Text, out double op)) _settings.IdleOpacity = Math.Clamp(op, 0.01, 1.0);

            ConfigManager.SaveSettings(_settings);
            RefreshUI();

            _lastActivityTime = DateTime.Now;
            double originalOpacity = ActiveOpacity;
            this.Opacity = 0.5;
            await Task.Delay(100);
            this.Opacity = originalOpacity;
        }

        private void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is string u)
            {
                _lastActivityTime = DateTime.Now;
                Debug.WriteLine($"[Launch_Click] URL: {u}");
                try
                {
                    var psi = new ProcessStartInfo { FileName = u, UseShellExecute = true };
                    // URLではなくローカルのファイルパス(.exeなど)の場合、その親フォルダを作業パスに設定
                    if (System.IO.File.Exists(u))
                    {
                        psi.WorkingDirectory = System.IO.Path.GetDirectoryName(u);
                    }
                    Process.Start(psi);
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
        //private void ExportConfig_Click(object sender, RoutedEventArgs e) { var s = new SaveFileDialog { Filter = "dict|*.dict" }; if (s.ShowDialog() == true) ConfigManager.SaveDict(_dict); }
        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var s = new SaveFileDialog { Filter = "Bookmark Dictionary (*.dict)|*.dict", FileName = "ZiZiBOOKS.dict" };
            if (s.ShowDialog() == true)
            {
                // 指定されたファイルパスへ現在の _dict を保存する
                ConfigManager.SaveDictToPath(_dict, s.FileName);
            }
        }
        //private void ImportConfig_Click(object sender, RoutedEventArgs e) { var o = new OpenFileDialog { Filter = "dict|*.dict" }; if (o.ShowDialog() == true) { _dict = ConfigManager.LoadDict(); RefreshUI(); } }
        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var o = new OpenFileDialog { Filter = "Bookmark Dictionary (*.dict)|*.dict" };
            if (o.ShowDialog() == true)
            {
                // 指定されたファイルからロードし、現在の _dict を差し替える
                var imported = ConfigManager.LoadDictFromPath(o.FileName);
                if (imported != null)
                {
                    _dict = imported;
                    RefreshUI();
                    // アプリケーション既定の保存先にも反映
                    ConfigManager.SaveDict(_dict);
                }
            }
        }

        private void MajiModeCheck_Changed(object sender, RoutedEventArgs e) { if (_isInitialized) { _settings.IsMajiMode = MajiModeCheck.IsChecked ?? false; UpdateUIMode(_settings.IsMajiMode); } }
        private void Window_LocationChanged(object sender, EventArgs e) { if (_isInitialized) { _settings.Top = Top; _settings.Left = Left; } }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 最小化（Iconic）状態のときは座標を更新しない
            if (this.WindowState == WindowState.Normal)
            {
                _settings.Top = this.Top;
                _settings.Left = this.Left;
            }
            ConfigManager.SaveSettings(_settings);
            ConfigManager.SaveDict(_dict);
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}