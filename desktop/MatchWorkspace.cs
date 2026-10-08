using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DanmuCinema.Desktop
{
    public sealed partial class ShellWindow
    {
        FloatingPane workspace;
        Grid workspaceLayer;
        Grid workspaceBody;
        MatchView matchView;
        BatchView batchView;
        Button workspaceSources, workspaceDownloads;
        bool workspaceClosing;
        internal bool WorkspaceVisible { get { return workspace != null; } }
        bool EnsureWorkspace()
        {
            if (workspace != null) return false;
            var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
            workspaceBody = new Grid { Margin = new Thickness(20, 8, 20, 20) };
            var header = new Grid { Margin = new Thickness(20, 12, 10, 0) }; header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            workspaceSources = Ui.Button("来源与集数", () => OpenMatchWindow(false), true);
            workspaceDownloads = Ui.Button("下载预览", ShowBatch);
            header.Children.Add(Ui.Row(workspaceSources, workspaceDownloads)); layout.Children.Add(header);
            var close = Ui.Button("", RequestWorkspaceClose); Ui.ConfigureCaption(close, "close"); close.ToolTip = "关闭当前页面"; Grid.SetColumn(close, 2); header.Children.Add(close);
            Grid.SetRow(workspaceBody, 1); layout.Children.Add(workspaceBody);
            workspaceLayer = new Grid { Margin = new Thickness(18, 6, 18, 8), ClipToBounds = true };
            Grid.SetRow(workspaceLayer, 1); Grid.SetRowSpan(workspaceLayer, 2); Panel.SetZIndex(workspaceLayer, 10);
            workspace = new FloatingPane(layout, session.WorkspaceBounds); workspaceLayer.Children.Add(workspace);
            var drag = workspace.DragHandle(); Grid.SetColumn(drag, 1); header.Children.Add(drag);
            ((Grid)View.FindName("WorkArea")).Children.Add(workspaceLayer); SurfaceMotion.FadeIn(workspace);
            var line = Ui.AccentLine(); line.VerticalAlignment = VerticalAlignment.Top; line.HorizontalAlignment = HorizontalAlignment.Left; line.Margin = new Thickness(20, 0, 0, 0); layout.Children.Add(line); Ui.AnimateAccent(line);
            return true;
        }
        void MountMatch(bool autoSearch)
        {
            if (workspaceClosing) return; bool created = EnsureWorkspace();
            if (session.Match == null) { Navigate("tasks"); return; }
            session.Match.Open = true;
            if (matchView == null) { matchView = new MatchView(controller, session.Match, autoSearch, ShowBatch); workspaceBody.Children.Add(matchView); }
            workspaceSources.IsEnabled = true; workspaceSources.Style = (Style)Ui.Resource("Primary"); workspaceDownloads.Style = (Style)Ui.Resource("TextAction");
            SwitchWorkspaceView(matchView, created);
        }
        void MountBatch()
        {
            if (workspaceClosing) return; bool created = EnsureWorkspace();
            if (batchView == null) { batchView = new BatchView(controller); workspaceBody.Children.Add(batchView); }
            workspaceSources.IsEnabled = session.Match != null;
            workspaceSources.Style = (Style)Ui.Resource("TextAction"); workspaceDownloads.Style = (Style)Ui.Resource("Primary");
            SwitchWorkspaceView(batchView, created); controller.Publish();
        }
        void SwitchWorkspaceView(FrameworkElement incoming, bool created)
        {
            var outgoing = workspaceBody.Children.OfType<FrameworkElement>().FirstOrDefault(x => x != incoming && x.Visibility == Visibility.Visible);
            if (!created && outgoing == null && incoming.IsVisible) return;
            var pane = workspace;
            Action mount = () =>
            {
                if (!Object.ReferenceEquals(workspace, pane)) return;
                if (outgoing != null) { outgoing.Visibility = Visibility.Collapsed; SurfaceMotion.Cancel(outgoing); outgoing.Opacity = 1; }
                workspaceClosing = false; incoming.Visibility = Visibility.Visible;
                if (!created) SurfaceMotion.FadeIn(incoming);
            };
            // A newly created pane fades as one surface; never multiply it by a
            // second child-opacity animation. Nested views share the same timings.
            if (!created && outgoing != null) { incoming.Visibility = Visibility.Collapsed; workspaceClosing = true; SurfaceMotion.FadeOut(outgoing, mount); }
            else mount();
        }
        internal void RequestWorkspaceClose()
        {
            if (workspace == null || workspaceClosing) return;
            if (batchView != null && batchView.Visibility == Visibility.Visible && matchView != null)
            {
                MountMatch(false);
            }
            else
            {
                workspaceClosing = true; var outgoing = workspace; SurfaceMotion.FadeOut(outgoing, () => { if (Object.ReferenceEquals(workspace, outgoing)) CloseWorkspace(false); });
            }
        }
        void CloseWorkspace(bool releasingWindow)
        {
            if (workspace == null) return;
            session.WorkspaceBounds = workspace.Bounds; workspaceClosing = false;
            if (matchView != null) matchView.Dispose(); if (batchView != null) batchView.Dispose();
            if (session.Match != null && !releasingWindow) session.Match.Open = false;
            workspace.Dispose(); ((Grid)View.FindName("WorkArea")).Children.Remove(workspaceLayer); workspaceLayer.Children.Clear(); workspaceLayer = null;
            workspaceBody.Children.Clear(); workspace = null; workspaceBody = null; matchView = null; batchView = null; workspaceSources = workspaceDownloads = null;
        }
    }
}
