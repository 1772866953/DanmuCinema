using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DanmuCinema
{
    public sealed class LibraryLocation
    {
        public readonly string Directory, Filter;
        public LibraryLocation(string directory, string filter) { Directory = directory; Filter = filter ?? ""; }
    }
    public sealed class LibraryNavigation
    {
        readonly Stack<LibraryLocation> back = new Stack<LibraryLocation>(), forward = new Stack<LibraryLocation>();
        public LibraryLocation Current { get; private set; }
        public bool CanBack { get { return back.Count > 0; } }
        public bool CanForward { get { return forward.Count > 0; } }
        public LibraryNavigation() { Current = new LibraryLocation(null, ""); }
        public void UpdateFilter(string filter) { Current = new LibraryLocation(Current.Directory, filter); }
        public bool Visit(string directory, string filter)
        {
            if (String.Equals(Current.Directory, directory, StringComparison.OrdinalIgnoreCase) && Current.Filter == (filter ?? "")) return false;
            back.Push(Current); forward.Clear(); Current = new LibraryLocation(directory, filter); return true;
        }
        public bool Back() { if (!CanBack) return false; forward.Push(Current); Current = back.Pop(); return true; }
        public bool Forward() { if (!CanForward) return false; back.Push(Current); Current = forward.Pop(); return true; }
    }
    public sealed class LibraryMouseNavigation : IMessageFilter, IDisposable
    {
        readonly Form owner;
        readonly Func<bool> enabled;
        readonly Action<bool> navigate;
        public LibraryMouseNavigation(Form owner, Func<bool> enabled, Action<bool> navigate)
        { this.owner = owner; this.enabled = enabled; this.navigate = navigate; Application.AddMessageFilter(this); }
        public bool PreFilterMessage(ref Message message)
        {
            const int Down = 0x20B, Up = 0x20C, AppCommand = 0x319;
            if (message.Msg != Down && message.Msg != Up && message.Msg != AppCommand) return false;
            if (owner.IsDisposed || !enabled()) return false;
            var control = Control.FromChildHandle(message.HWnd);
            if (control == null || control.FindForm() != owner) return false;
            int button = message.Msg == AppCommand ? (int)((message.LParam.ToInt64() >> 16) & 0x7FF) : (int)((message.WParam.ToInt64() >> 16) & 0xFFFF);
            if (button != 1 && button != 2) return false;
            // Consume mouse-down to keep side buttons from changing the grid selection.
            // Dispatch on release; consuming it also prevents a duplicate browser AppCommand.
            if (message.Msg != Down) navigate(button == 2);
            message.Result = new IntPtr(1); return true;
        }
        public void Dispose() { Application.RemoveMessageFilter(this); }
    }
}
