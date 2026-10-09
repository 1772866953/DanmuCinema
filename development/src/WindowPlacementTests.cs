using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DanmuCinema
{
    public static class WindowPlacementTests
    {
        public static void Run(List<string> report)
        {
            SelfTests.Assert(WindowPlacementStore.Load() == null, "首次启动无窗口记录时使用默认布局", report);
            var normal = new Rectangle(180, 90, 1250, 840);
            var placement = new WindowPlacement(); placement.Capture(normal, FormWindowState.Normal);
            WindowPlacementStore.Save(placement);
            var loaded = WindowPlacementStore.Load();
            SelfTests.Assert(loaded.Bounds == normal && !loaded.Maximized, "冷启动记录保留普通窗口位置和自定义尺寸", report);
            placement.Capture(normal, FormWindowState.Maximized); WindowPlacementStore.Save(placement);
            loaded = WindowPlacementStore.Load();
            SelfTests.Assert(loaded.Maximized && loaded.Bounds == normal, "最大化状态持久化，同时保留还原后的普通窗口尺寸", report);
            placement.Capture(new Rectangle(-32000, -32000, 160, 28), FormWindowState.Minimized);
            SelfTests.Assert(placement.Maximized && placement.Bounds == normal, "最小化不覆盖上次最大化状态和有效窗口尺寸", report);
            placement.Capture(normal, FormWindowState.Normal);
            SelfTests.Assert(!placement.Maximized && placement.Bounds == normal, "取消最大化后记忆更新为普通窗口", report);
            var screens = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, 0, 1920, 1040) };
            placement.Capture(new Rectangle(-1600, 80, 1250, 840), FormWindowState.Normal);
            SelfTests.Assert(placement.Fit(screens, new Size(1000, 700)) == placement.Bounds, "负坐标的第二显示器窗口仍保留原位置", report);
            var recovered = placement.Fit(new[] { screens[0] }, new Size(1000, 700));
            SelfTests.Assert(screens[0].Contains(recovered) && recovered.Size == normal.Size, "断开显示器后窗口移回可见区域并保留尺寸", report);
            placement.Capture(new Rectangle(1500, 900, 2500, 1500), FormWindowState.Normal);
            SelfTests.Assert(placement.Fit(new[] { screens[0] }, new Size(1000, 700)) == screens[0], "屏幕分辨率缩小时窗口缩到可用区域", report);
            placement.Capture(new Rectangle(100, 100, 200, 100), FormWindowState.Normal);
            SelfTests.Assert(placement.Fit(new[] { screens[0] }, new Size(1000, 700)).Size == new Size(1000, 700), "恢复窗口遵守界面最小尺寸", report);
            SelfTests.Assert(!File.Exists(WindowPlacementStore.FilePath + ".bak"), "窗口尺寸记录原子覆盖且不生成备份", report);
            File.WriteAllText(WindowPlacementStore.FilePath, "broken");
            SelfTests.Assert(WindowPlacementStore.Load() == null, "窗口记录损坏时安全回退默认布局", report);
            File.WriteAllText(WindowPlacementStore.FilePath, "{\"Width\":1200,\"Height\":800,\"Left\":2147483647}");
            SelfTests.Assert(WindowPlacementStore.Load() == null, "异常窗口坐标被拒绝，避免恢复到不可见位置", report);
        }
    }
}
