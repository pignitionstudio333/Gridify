using System;

namespace GridifyApp
{
    /// <summary>收納區視窗呼叫 App 的所有回呼。</summary>
    public class GridHost
    {
        public Action Save;
        public Action Exit;
        public Action NewGrid;
        public Action<GridWindow> DeleteGrid;
        public Action<GridWindow, string[]> Dropped;    // 有項目被拖進這個收納區
        public Action<GridWindow, string[]> MovedOut;   // 項目要移出這個收納區
        public Action<string, string> Renamed;           // 檔案被改名（舊路徑、新路徑）
        public Action Changed;                           // 檔案被刪除等，需要重新同步桌面
        public Action<int> IconStep;                     // Ctrl+滾輪：圖示大小增減（像素，正負）
        public Action<int> IconSet;                      // 圖示大小設為指定值
        public Action Appearance;                        // 開啟外觀設定視窗
        public Action<GridWindow, int> DeleteTab;        // 刪除分頁（確認、項目回到「桌面」收納區）
        public Action Undo;                              // 復原 / 重做（只動 Gridify 的設定，不動任何檔案）
        public Action Redo;
        public Func<bool> CanUndo;
        public Func<bool> CanRedo;
        public Action<GridWindow, int, GridWindow, int> MoveTabTo;   // 把分頁（來源視窗, 索引）搬到（目標視窗, 插入位置）；同一視窗 = 重新排序
    }
}
