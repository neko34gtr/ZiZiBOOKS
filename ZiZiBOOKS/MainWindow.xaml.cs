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
using System.Runtime.InteropServices;
using System.Windows.Interop;

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

        // メインUI位置保持用の変数
        private double _originalLeft;
        private double _originalTop;

        public MainWindow()
        {
            InitializeComponent();
            _settings = ConfigManager.LoadSettings();
            _dict = ConfigManager.LoadDict();

            // [ADD] シャットダウンイベントの登録
            if (Application.Current != null)
            {
                Application.Current.SessionEnding += (s, e) => FinalSave();
            }

            // 座標の補正ロジック
            CheckAndFixWindowPosition();

            RefreshUI();

            // 重要：コンストラクタではなく、ウィンドウ描画完了後に解像度判定を行う
            this.Loaded += MainWindow_Loaded;

            // 焼き付き防止用の監視ループ
            CompositionTarget.Rendering += OnRendering;
        }

        // [ADD] 保存処理のメソッド抽出
        private void FinalSave()
        {
            if (this.WindowState == WindowState.Normal)
            {
                _settings.Top = this.Top;
                _settings.Left = this.Left;
            }
            ConfigManager.SaveSettings(_settings);
            ConfigManager.SaveDict(_dict);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitialized = true;

            // ウィンドウがOSに認識された状態で初回計算を実行
            UpdateWindowSizeLimit();
        }

        private void CheckAndFixWindowPosition()
        {
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHight = SystemParameters.WorkArea.Height;

            bool isInvalid = double.IsNaN(_settings.Top) ||
                             double.IsNaN(_settings.Left) ||
                             _settings.Top < -10000 ||
                             _settings.Top > 20000 ||
                             _settings.Left < -10000 ||
                             _settings.Left > 20000;

            if (isInvalid)
            {
                _settings.Left = (screenWidth - 300) / 2;
                _settings.Top = (screenHight - 400) / 2;
            }

            this.Left = _settings.Left;
            this.Top = _settings.Top;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_isInitialized || ConfigPanel.Visibility == Visibility.Visible)
            {
                this.Opacity = ActiveOpacity;
                return;
            }

            if (this.IsMouseOver || _draggedMainBtn != null || _draggedItem != null)
            {
                _lastActivityTime = DateTime.Now;
            }

            double elapsedSeconds = (DateTime.Now - _lastActivityTime).TotalSeconds;
            double duration = Math.Max(0.1, _settings.IdleSeconds);
            double minOpacity = _settings.IdleOpacity;

            if (elapsedSeconds <= 0)
            {
                if (this.Opacity < ActiveOpacity)
                {
                    this.Opacity = Math.Min(ActiveOpacity, this.Opacity + 0.1);
                }
            }
            else
            {
                double targetOpacity = ActiveOpacity - (elapsedSeconds / duration) * (ActiveOpacity - minOpacity);
                this.Opacity = Math.Max(minOpacity, targetOpacity);
            }
        }

        private void RefreshUI()
        {
            CreateLauncherButtons();
            CreateEditList();
            ApplySettingsToFields();

            // 項目数変更に合わせて制限を再計算（初期化済みの場合のみ）
            if (_isInitialized)
            {
                UpdateWindowSizeLimit();
            }
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
                _lastActivityTime = DateTime.Now;
                _dragStartPoint = e.GetPosition(this);
                _draggedMainBtn = btn;
                _draggedMainIndex = SemiContainer.Children.IndexOf(btn);
            }
        }

        private void MainBtn_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedMainBtn == null) return;

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _lastActivityTime = DateTime.Now;
                Point currentPos = e.GetPosition(this);
                Vector diff = _dragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (!_draggedMainBtn.IsMouseCaptured)
                    {
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
                _lastActivityTime = DateTime.Now;
                bool wasCaptured = _draggedMainBtn.IsMouseCaptured;

                if (wasCaptured)
                {
                    _draggedMainBtn.ReleaseMouseCapture();
                    _draggedMainBtn.Opacity = 1.0;
                    InsertIndicator.Visibility = Visibility.Collapsed;

                    Point upPos = e.GetPosition(this);
                    Vector moveDist = _dragStartPoint - upPos;
                    if (Math.Abs(moveDist.X) < 2 && Math.Abs(moveDist.Y) < 2)
                    {
                        Launch_Click(_draggedMainBtn, e);
                    }
                    else
                    {
                        int oldIndex = _draggedMainIndex;
                        int newIndex = _targetInsertIndex;
                        if (newIndex > oldIndex) newIndex--;

                        if (newIndex != oldIndex && newIndex >= 0 && newIndex < _dict.Items.Count)
                        {
                            var item = _dict.Items[oldIndex];
                            _dict.Items.RemoveAt(oldIndex);
                            _dict.Items.Insert(newIndex, item);

                            RefreshUI();
                            ConfigManager.SaveDict(_dict);
                        }
                    }
                    e.Handled = true;
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
                border.MouseEnter += Border_MouseEnter;
                border.MouseLeftButtonUp += Border_MouseLeftButtonUp;

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

        private void EditArea_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("UniformResourceLocator") || e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void EditArea_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent("UniformResourceLocator"))
                {
                    var stream = e.Data.GetData("UniformResourceLocator") as System.IO.MemoryStream;
                    if (stream != null)
                    {
                        byte[] data = stream.ToArray();
                        string url = System.Text.Encoding.UTF8.GetString(data).Split('\0')[0];
                        UrlBox.Text = url;

                        if (e.Data.GetDataPresent(DataFormats.Text))
                        {
                            string title = e.Data.GetData(DataFormats.Text)?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(title) && title != url) NameBox.Text = title;
                        }
                    }
                }
                else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        UrlBox.Text = files[0];
                        NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(files[0]);
                        IconPathBox.Text = files[0];
                    }
                }
            }
            catch { }
            _lastActivityTime = DateTime.Now;
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            _editingIndex = -1;
            NameBox.Clear();
            UrlBox.Clear();
            IconPathBox.Clear();
            MemoBox.Clear();
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

            IdleSecondsBox.Text = _settings.IdleSeconds.ToString();
            IdleOpacityBox.Text = _settings.IdleOpacity.ToString("0.00");

            UpdateUIMode(_settings.IsMajiMode);
        }

        private void UpdateUIMode(bool isMaji)
        {
            SemiContainer.Visibility = isMaji ? Visibility.Collapsed : Visibility.Visible;
            MajiContainer.Visibility = isMaji ? Visibility.Visible : Visibility.Collapsed;

            // メインUIのスクロール領域も連動
            MainScrollViewer.Visibility = isMaji ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(FontSizeBox.Text, out int fs))
            {
                _settings.FontSize = Math.Max(16, Math.Min(fs, 48));
                FontSizeBox.Text = _settings.FontSize.ToString();
            }
            else
            {
                _settings.FontSize = 16;
            }

            if (int.TryParse(IdleSecondsBox.Text, out int sec)) _settings.IdleSeconds = sec;
            if (double.TryParse(IdleOpacityBox.Text, out double op)) _settings.IdleOpacity = Math.Clamp(op, 0.01, 1.0);

            ConfigManager.SaveSettings(_settings);
            RefreshUI();

            _lastActivityTime = DateTime.Now;
            this.Opacity = 0.5;
            await Task.Delay(100);
            this.Opacity = ActiveOpacity;
        }

        private void Launch_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button b && b.DataContext is string u)
            {
                _lastActivityTime = DateTime.Now;
                try
                {
                    var psi = new ProcessStartInfo { FileName = u, UseShellExecute = true };
                    if (System.IO.File.Exists(u))
                    {
                        psi.WorkingDirectory = System.IO.Path.GetDirectoryName(u);
                    }
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
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

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (ConfigPanel.Visibility == Visibility.Collapsed)
            {
                _originalLeft = this.Left;
                _originalTop = this.Top;

                ConfigPanel.Visibility = Visibility.Visible;
                SemiContainer.Visibility = Visibility.Collapsed;
                MajiContainer.Visibility = Visibility.Collapsed;
                MainScrollViewer.Visibility = Visibility.Collapsed;

                this.UpdateLayout();
                UpdateWindowSizeLimit();

                var handle = new WindowInteropHelper(this).Handle;
                var screenRect = GetCurrentMonitorWorkArea(hWnd: handle);

                double currentRight = this.Left + this.ActualWidth;
                if (currentRight > screenRect.Right)
                {
                    this.Left = screenRect.Right - this.ActualWidth - 10;
                }
            }
            else
            {
                ConfigPanel.Visibility = Visibility.Collapsed;

                this.Left = _originalLeft;
                this.Top = _originalTop;

                UpdateUIMode(_settings.IsMajiMode);
                UpdateWindowSizeLimit();
            }
        }

        #region Win32 API for Monitor Detection (No Windows.Forms)

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private Rect GetCurrentMonitorWorkArea(IntPtr hWnd)
        {
            IntPtr hMonitor = MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST);
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(mi);

            if (GetMonitorInfo(hMonitor, ref mi))
            {
                var source = PresentationSource.FromVisual(this);
                double dpiScale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;

                return new Rect(
                    mi.rcWork.Left / dpiScale,
                    mi.rcWork.Top / dpiScale,
                    (mi.rcWork.Right - mi.rcWork.Left) / dpiScale,
                    (mi.rcWork.Bottom - mi.rcWork.Top) / dpiScale
                );
            }
            return SystemParameters.WorkArea;
        }

        #endregion

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void ExportConfig_Click(object sender, RoutedEventArgs e)
        {
            var s = new SaveFileDialog { Filter = "Bookmark Dictionary (*.dict)|*.dict", FileName = "ZiZiBOOKS.dict" };
            if (s.ShowDialog() == true)
            {
                ConfigManager.SaveDictToPath(_dict, s.FileName);
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            var o = new OpenFileDialog { Filter = "Bookmark Dictionary (*.dict)|*.dict" };
            if (o.ShowDialog() == true)
            {
                var imported = ConfigManager.LoadDictFromPath(o.FileName);
                if (imported != null)
                {
                    _dict = imported;
                    RefreshUI();
                    ConfigManager.SaveDict(_dict);
                }
            }
        }

        private void MajiModeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                _settings.IsMajiMode = MajiModeCheck.IsChecked ?? false;
                UpdateUIMode(_settings.IsMajiMode);
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (_isInitialized && this.WindowState == WindowState.Normal)
            {
                _settings.Top = Top;
                _settings.Left = Left;

                // ディスプレイ移動時に解像度に合わせて制限値を再計算
                UpdateWindowSizeLimit();
            }
        }

        private void UpdateWindowSizeLimit()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                var screenRect = GetCurrentMonitorWorkArea(handle);

                // 4K解像度(高さ2160px)かつ登録数が40個未満の場合は制限をかけない
                if (screenRect.Height >= 2160 && _dict.Items.Count < 40)
                {
                    MainScrollViewer.MaxHeight = double.PositiveInfinity;
                }
                else
                {
                    // それ以外（FHD等）は作業領域の高さの65%を上限とする
                    MainScrollViewer.MaxHeight = screenRect.Height * 0.65;
                }

                // 設定パネルが表示中なら、中のリスト領域も制限
                if (ConfigPanel.Visibility == Visibility.Visible)
                {
                    ConfigListScroll.MaxHeight = screenRect.Height * 0.2;
                }
            }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //既存の Window_Closing を FinalSave 呼び出しに
            FinalSave();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}