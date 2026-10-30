using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace GridifyApp
{
    public partial class GridWindow : Window
    {
        const string GridFormat = "GridifyApp.Paths";
        const string TabFormat = "GridifyApp.Tab";
        static GridWindow _dragTabSource;   // 正在被拖曳的分頁來自哪個收納區
        static int _dragTabIndex = -1;

        readonly GridData _data;
        readonly GridHost _host;
        readonly DispatcherTimer _saveTimer;
        Point _dragStart;
        bool _dragArmed;
        bool _animating;
        bool _ready;
        int _renameTab = -1;            // 正在改名的分頁索引（-1 = 改的是收納區名稱）
        string _tabSig;                 // 分頁列目前的結構（名稱清單），沒變就只更新外觀、不重建
        Point _tabPress;
        bool _tabPressed;
        DispatcherTimer _hoverTimer;
        int _hoverIndex = -1;
        static GridWindow _dragSource;   // 目前正在被拖曳的項目來自哪個收納區（用來分辨「調整順序」與「從別處拖進來」）

        static readonly DropShadowEffect ShadowFx = MakeShadow();

        static DropShadowEffect MakeShadow()
        {
            var e = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.45 };
            e.Freeze();
            return e;
        }

        public GridData Data => _data;
        public ObservableCollection<GridItem> Items { get; } = new ObservableCollection<GridItem>();

        public GridWindow(GridData data, GridHost host)
        {
            InitializeComponent();
            Deactivated += Window_Deactivated;
            _data = data;
            _host = host;

            Left = data.Left; Top = data.Top; Width = data.Width; Height = data.Height;
            EnsureVisible();
            TitleText.Text = data.Title;
            Reload();
            DataContext = this;

            ApplyLook();
            if (_data.Collapsed)
            {
                MinHeight = 0;
                Height = CollapsedHeight();
                List.Visibility = Visibility.Collapsed;
            }
            UpdateChevron();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); _host.Save(); };
            PreviewKeyDown += Window_PreviewKeyDown;
            LocationChanged += (s, e) => Touch();
            SizeChanged += (s, e) => Touch();

            Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() => _ready = true), DispatcherPriority.ApplicationIdle);
            Theme.Changed += OnThemeChanged;
            Closed += (s, e) => Theme.Changed -= OnThemeChanged;
        }

        /// <summary>依 Data.Items 重建顯示的項目。</summary>
        public void Reload()
        {
            RefreshTabs();
            // 內容沒變就什麼都不做，避免每次同步都重建所有圖示
            if (Items.Count == _data.Items.Count)
            {
                bool same = true;
                for (int i = 0; i < Items.Count; i++)
                    if (!string.Equals(Items[i].FullPath, _data.Items[i], StringComparison.Ordinal)) { same = false; break; }
                if (same) return;
            }
            Items.Clear();
            foreach (var p in _data.Items) Items.Add(new GridItem(p));
        }

        // ---------- 儲存 ----------
        void Touch()
        {
            if (!IsLoaded || _animating) return;
            // 高度不在這裡存：只有使用者拖曳調整大小時才更新 _data.Height（收合 / 展開動畫、陰影變化都不會污染它）
            _data.Left = Left; _data.Top = Top; _data.Width = Width;
            _saveTimer.Stop(); _saveTimer.Start();
        }

        void PersistItems()
        {
            _data.Items = Items.Select(i => i.FullPath).ToList();
            _host.Save();
        }
    }
}
