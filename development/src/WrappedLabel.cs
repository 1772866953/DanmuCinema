using System;
using System.Drawing;
using System.Windows.Forms;

namespace DanmuCinema
{
    public sealed class WrappedLabel : Label
    {
        Control owner;
        public WrappedLabel() { AutoSize = true; Dock = DockStyle.Top; UseMnemonic = false; }
        protected override void OnParentChanged(EventArgs e)
        {
            if (owner != null) owner.SizeChanged -= FitWidth;
            base.OnParentChanged(e); owner = Parent;
            if (owner != null) owner.SizeChanged += FitWidth;
            FitWidth(this, EventArgs.Empty);
        }
        void FitWidth(object sender, EventArgs e)
        {
            if (owner == null) return;
            int width = Math.Max(80, owner.ClientSize.Width - owner.Padding.Horizontal - Margin.Horizontal);
            if (MaximumSize.Width != width) MaximumSize = new Size(width, 0);
        }
        protected override void Dispose(bool disposing)
        { if (disposing && owner != null) owner.SizeChanged -= FitWidth; base.Dispose(disposing); }
    }
}
