using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BilibiliDM_PluginFramework;

namespace BlacklistPlugin
{
    public class BlacklistPlugin : DMPlugin
    {
        private readonly object _lock = new object();
        private HashSet<string> _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastWrite = DateTime.MinValue;
        private bool _initialized;

        public BlacklistPlugin()
        {
            PluginName = "黑名单";
            PluginAuth = "deepseek";
            PluginCont = "";
            PluginVer = "v1.1.0";
            PluginDesc = "按用户名屏蔽弹幕。右键本插件选“管理”打开界面。";
        }

        /// <summary>
        ///     黑名单文件位置：主程序 exe 同目录下的 blacklist.txt
        /// </summary>
        public static string FilePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blacklist.txt");

        public override void Start()
        {
            base.Start();
            try
            {
                EnsureLoaded();
                Log("黑名单已启用，共 " + _names.Count + " 条规则");
            }
            catch (Exception ex)
            {
                Log("启用失败：" + ex.Message);
            }
        }

        /// <summary>
        ///     右键插件 → 管理，打开此窗口
        /// </summary>
        public override void Admin()
        {
            base.Admin();
            var win = new BlacklistWindow(this);
            var owner = Application.Current?.MainWindow;
            if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;
            win.Show();
            win.Activate();
        }

        /// <summary>
        ///     主程序通过反射调用此方法。返回 true 表示拦截这条弹幕。
        ///     方法名、参数、返回值都不能改，否则主程序认不出来。
        /// </summary>
        public bool ShouldBlockDanmaku(DanmakuModel dm)
        {
            if (dm == null) return false;
            var name = dm.UserName;
            if (string.IsNullOrEmpty(name)) return false;

            try
            {
                EnsureLoaded();
                return _names.Contains(name);
            }
            catch
            {
                return false;
            }
        }

        // ================== 供界面调用 ==================

        public List<string> Snapshot()
        {
            try { EnsureLoaded(); }
            catch { }
            lock (_lock) { return _names.ToList(); }
        }

        /// <summary>
        ///     批量添加，支持换行/逗号/分号分隔，自动去重。返回新增数量。
        /// </summary>
        public int AddMany(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var list = Snapshot();
            var existing = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            var added = 0;

            var separators = new[] { '\r', '\n', ',', '，', ';', '；' };
            foreach (var raw in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var v = raw.Trim();
                if (v.Length == 0) continue;
                if (v.StartsWith("#")) continue;
                if (!existing.Add(v)) continue;
                list.Add(v);
                added++;
            }

            if (added > 0) Save(list);
            return added;
        }

        /// <summary>
        ///     批量删除，返回删除数量。
        /// </summary>
        public int RemoveMany(IEnumerable<string> names)
        {
            var list = Snapshot();
            var removed = 0;
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                removed += list.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            }
            if (removed > 0) Save(list);
            return removed;
        }

        /// <summary>
        ///     覆盖保存整个名单到 blacklist.txt，并立即刷新内存。
        /// </summary>
        public void Save(IEnumerable<string> names)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names)
            {
                var v = (n ?? "").Trim();
                if (v.Length == 0) continue;
                if (v.StartsWith("#")) continue;
                set.Add(v);
            }

            var lines = new List<string>
            {
                "# 黑名单文件：每行写一个要屏蔽的用户名",
                "# 以 # 开头的行会被忽略",
                "# 修改保存后立即生效，无需重启弹幕姬",
                "# 大小写不敏感"
            };
            lines.AddRange(set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

            File.WriteAllLines(FilePath, lines);

            lock (_lock)
            {
                _names = set;
                try { _lastWrite = File.GetLastWriteTime(FilePath); } catch { _lastWrite = DateTime.MinValue; }
                _initialized = true;
            }
        }

        // ================== 内部：读取 + 热更新 ==================

        private void EnsureLoaded()
        {
            var path = FilePath;

            // 文件不存在：首次自动生成一个带说明的空文件
            if (!File.Exists(path))
            {
                if (_initialized) return;
                lock (_lock)
                {
                    if (_initialized) return;
                    try
                    {
                        File.WriteAllText(path,
                            "# 黑名单文件：每行写一个要屏蔽的用户名\r\n" +
                            "# 以 # 开头的行会被忽略\r\n" +
                            "# 修改保存后立即生效，无需重启弹幕姬\r\n" +
                            "# 大小写不敏感\r\n");
                    }
                    catch { }

                    _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _lastWrite = DateTime.MinValue;
                    _initialized = true;
                }
                return;
            }

            // 文件存在：修改时间变了才重读
            var lastWrite = File.GetLastWriteTime(path);
            if (_initialized && lastWrite == _lastWrite) return;

            lock (_lock)
            {
                lastWrite = File.GetLastWriteTime(path);
                if (_initialized && lastWrite == _lastWrite) return;

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var n = rawLine.Trim();
                    if (n.Length == 0) continue;
                    if (n.StartsWith("#")) continue;
                    set.Add(n);
                }
                _names = set;
                _lastWrite = lastWrite;
                _initialized = true;
            }
        }
    }

    /// <summary>
    ///     纯代码构建的黑名单管理窗口（不需要 XAML）。
    /// </summary>
    public class BlacklistWindow : Window
    {
        private readonly BlacklistPlugin _plugin;
        private readonly ListBox _list;
        private readonly TextBox _input;
        private readonly TextBlock _status;
        private readonly ObservableCollection<string> _items;

        public BlacklistWindow(BlacklistPlugin plugin)
        {
            _plugin = plugin;

            Title = "黑名单管理 - " + plugin.PluginName;
            Width = 460;
            Height = 520;
            MinWidth = 360;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // 提示
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 列表
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // 输入
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // 底部

            // ---- 提示 ----
            var tip = new TextBlock
            {
                Text = "支持一次粘贴多个：用换行、逗号或分号分隔，自动去重。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(tip, 0);
            root.Children.Add(tip);

            // ---- 列表 ----
            _items = new ObservableCollection<string>(_plugin.Snapshot());
            _list = new ListBox
            {
                ItemsSource = _items,
                SelectionMode = SelectionMode.Extended
            };
            Grid.SetRow(_list, 1);
            root.Children.Add(_list);

            // ---- 输入区 ----
            var inputPanel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _input = new TextBox
            {
                Padding = new Thickness(4),
                VerticalContentAlignment = VerticalAlignment.Center,
                AcceptsReturn = false
            };
            _input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    DoAdd();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(_input, 0);
            inputPanel.Children.Add(_input);

            var addBtn = new Button
            {
                Content = "添加",
                Width = 80,
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(0, 4, 0, 4)
            };
            addBtn.Click += (s, e) => DoAdd();
            Grid.SetColumn(addBtn, 1);
            inputPanel.Children.Add(addBtn);

            Grid.SetRow(inputPanel, 2);
            root.Children.Add(inputPanel);

            // ---- 底部 ----
            var bottomPanel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(_status, 0);
            bottomPanel.Children.Add(_status);

            var delBtn = new Button
            {
                Content = "删除选中",
                Width = 90,
                Margin = new Thickness(6, 0, 6, 0),
                Padding = new Thickness(0, 4, 0, 4)
            };
            delBtn.Click += (s, e) => DoDelete();
            Grid.SetColumn(delBtn, 1);
            bottomPanel.Children.Add(delBtn);

            var closeBtn = new Button
            {
                Content = "关闭",
                Width = 70,
                Padding = new Thickness(0, 4, 0, 4)
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 2);
            bottomPanel.Children.Add(closeBtn);

            Grid.SetRow(bottomPanel, 3);
            root.Children.Add(bottomPanel);

            Content = root;

            UpdateStatus();
            Loaded += (s, e) => _input.Focus();
        }

        private void DoAdd()
        {
            var text = _input.Text ?? "";
            if (text.Trim().Length == 0) return;

            var added = _plugin.AddMany(text);
            if (added <= 0)
            {
                MessageBox.Show(this, "没有新增（可能已在名单里，或内容为空）", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // 重新从插件拉一次完整列表，保证顺序和去重后的结果一致
                _items.Clear();
                foreach (var n in _plugin.Snapshot()) _items.Add(n);
            }

            _input.Clear();
            UpdateStatus();
            _input.Focus();
        }

        private void DoDelete()
        {
            var sel = _list.SelectedItems.Cast<string>().ToList();
            if (sel.Count == 0)
            {
                MessageBox.Show(this, "先在列表里选中要删的条目", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(this, "确认删除选中的 " + sel.Count + " 条吗？", "确认",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            _plugin.RemoveMany(sel);
            _items.Clear();
            foreach (var n in _plugin.Snapshot()) _items.Add(n);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            _status.Text = "共 " + _items.Count + " 条\r\n文件：" + BlacklistPlugin.FilePath;
        }
    }
}