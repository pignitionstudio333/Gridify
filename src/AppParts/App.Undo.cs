using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GridifyApp
{
    /// <summary>復原／重做：只動 Gridify 的設定，不動任何檔案。</summary>
    public partial class App
    {
        // ---------- 復原 / 重做 ----------
        // 每次存檔時比對「結構」（收納區、分頁、項目的歸屬與名稱；不含位置、大小、折疊）。
        // 結構有變，就把變動「之前」的狀態推進復原堆疊。桌面自動同步造成的變動不記錄；
        // 檔案改名後會清空（舊路徑已不存在，還原會弄壞參照）。復原只動 Gridify 的設定，不動任何檔案。
        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();
        string _lastKey, _lastFull;
        bool _inSync, _restoring;
        const int UndoMax = 30;

        string StructKey()
        {
            var sb = new StringBuilder();
            foreach (var g in _cfg.Grids)
            {
                var _x = g.ActiveTabData;
                sb.Append(g.Id).Append('|').Append(g.Title).Append('|').Append(g.IsDefault ? '1' : '0').Append('\n');
                foreach (var t in g.Tabs)
                {
                    sb.Append(' ').Append(t.Id).Append('|').Append(t.Name).Append('\n');
                    if (t.Items != null) foreach (var p in t.Items) sb.Append("  ").Append(p).Append('\n');
                }
            }
            return sb.ToString();
        }

        string Snapshot()
        {
            try { return JsonSerializer.Serialize(_cfg.Grids); } catch { return null; }
        }

        void TrackUndo()
        {
            if (_restoring) return;
            try
            {
                string key = StructKey();
                if (_lastKey == null) { _lastKey = key; _lastFull = Snapshot(); return; }
                if (key == _lastKey) return;
                if (!_inSync && _lastFull != null)
                {
                    _undo.Add(_lastFull);
                    if (_undo.Count > UndoMax) _undo.RemoveAt(0);
                    _redo.Clear();
                }
                _lastKey = key;
                _lastFull = Snapshot();
            }
            catch (Exception ex) { PerfLog.Write("undo track failed: " + ex.Message); }
        }

        void ClearUndo()
        {
            _undo.Clear();
            _redo.Clear();
            _lastKey = null;
            _lastFull = null;
        }

        void DoUndo()
        {
            if (_undo.Count == 0) return;
            string cur = Snapshot();
            string target = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            if (RestoreSnapshot(target) && cur != null) _redo.Add(cur);
        }

        void DoRedo()
        {
            if (_redo.Count == 0) return;
            string cur = Snapshot();
            string target = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            if (RestoreSnapshot(target) && cur != null)
            {
                _undo.Add(cur);
                if (_undo.Count > UndoMax) _undo.RemoveAt(0);
            }
        }

        /// <summary>把收納區 / 分頁 / 項目的歸屬還原成快照；現有收納區保留目前的位置、大小與折疊狀態，只有被刪掉的收納區會用快照裡的位置重建。</summary>
        bool RestoreSnapshot(string json)
        {
            List<GridData> snap;
            try { snap = JsonSerializer.Deserialize<List<GridData>>(json); }
            catch (Exception ex) { PerfLog.Crash("undo-parse", ex); return false; }
            if (snap == null) return false;

            _restoring = true;
            try
            {
                var keep = new List<GridData>();
                var created = new List<GridData>();
                foreach (var sg in snap)
                {
                    if (sg == null) continue;
                    var _s = sg.ActiveTabData;
                    var cg = _cfg.Grids.FirstOrDefault(g => g.Id == sg.Id);
                    if (cg == null) { keep.Add(sg); created.Add(sg); continue; }

                    string activeId = cg.ActiveTabData.Id;
                    cg.Title = sg.Title;
                    cg.IsDefault = sg.IsDefault;
                    cg.Tabs = sg.Tabs;
                    int ai = cg.Tabs.FindIndex(t => t.Id == activeId);   // 盡量停在原本選取的分頁
                    cg.ActiveTab = ai >= 0 ? ai : sg.ActiveTab;
                    var _c = cg.ActiveTabData;
                    keep.Add(cg);
                }

                // 快照之後才新增的收納區：關閉
                foreach (var cg in _cfg.Grids.ToList())
                {
                    if (snap.Any(s => s != null && s.Id == cg.Id)) continue;
                    var w = _wins.FirstOrDefault(x => ReferenceEquals(x.Data, cg));
                    if (w != null) { _wins.Remove(w); w.Close(); }
                }

                _cfg.Grids.Clear();
                _cfg.Grids.AddRange(keep);
                foreach (var g in created) OpenGrid(g);

                ReloadAll();
                ConfigStore.Save(_cfg);
            }
            catch (Exception ex) { PerfLog.Crash("undo-restore", ex); return false; }
            finally
            {
                _restoring = false;
                _lastKey = StructKey();
                _lastFull = Snapshot();
            }

            if (_takeOverActive) SyncDesktop();   // 還原期間桌面若有新檔案或檔案消失，重新對齊
            return true;
        }
    }
}
