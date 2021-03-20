﻿namespace DesktopSprites.Core
{
    using System;
    using System.Drawing;
    using System.Globalization;
    using System.Threading;
    using System.Windows.Forms;

    /// <summary>
    /// A dialog that reports exceptions to the user.
    /// </summary>
    public partial class ExceptionDialog : Form
    {
        /// <summary>
        /// Bitmap of the severity icon displayed on the form.
        /// </summary>
        private Bitmap iconBitmap;

        /// <summary>
        /// Displays a dialog describing an exception to the user.
        /// </summary>
        /// <param name="ex">The exception to report.</param>
        /// <param name="text">The text to display in the dialog.</param>
        /// <param name="caption">The text to display in the title bar of the dialog.</param>
        /// <param name="fatal">A value indicating if the exception is fatal to the process, which is reflected in the icon used.</param>
        public static void Show(Exception ex, string text, string caption, bool fatal)
        {
            using (var dialog = new ExceptionDialog(ex, text, caption, fatal))
                dialog.ShowDialog();
        }

        /// <summary>
        /// Displays a dialog describing an exception, in front of the specified object, to the user.
        /// </summary>
        /// <param name="owner">An implementation of <see cref="T:System.Windows.Forms.IWin32Window"/> that will own the modal dialog.
        /// </param>
        /// <param name="ex">The exception to report.</param>
        /// <param name="text">The text to display in the dialog.</param>
        /// <param name="caption">The text to display in the title bar of the dialog.</param>
        /// <param name="fatal">A value indicating if the exception is fatal to the process, which is reflected in the icon used.</param>
        public static void Show(IWin32Window owner, Exception ex, string text, string caption, bool fatal)
        {
            using (var dialog = new ExceptionDialog(ex, text, caption, fatal, owner))
                dialog.ShowDialog(owner);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.Core.ExceptionDialog"/> class.
        /// </summary>
        /// <param name="ex">The exception to report.</param>
        /// <param name="text">The text to display in the dialog.</param>
        /// <param name="caption">The text to display in the title bar of the dialog.</param>
        /// <param name="fatal">A value indicating if the exception is fatal to the process, which is reflected in the icon used.</param>
        /// <param name="owner">The owner of the dialog (optional).</param>
        private ExceptionDialog(Exception ex, string text, string caption, bool fatal, IWin32Window owner = null)
        {
            InitializeComponent();
            var screen = owner != null ? Screen.FromHandle(owner.Handle) : Screen.FromControl(this);
            MaximumSize = Rectangle.Intersect(screen.WorkingArea, screen.Bounds).Size;
            ExceptionText.Text = ex.ToString();
            if (!Runtime.IsMono)
            {
                Size oldSize = Size.Empty;
                Size preferredSize = ExceptionText.GetPreferredSize(Size.Empty);
                while (oldSize != ExceptionText.Size && preferredSize != ExceptionText.Size)
                {
                    oldSize = ExceptionText.Size;
                    ExceptionText.Size = preferredSize;
                    preferredSize = ExceptionText.GetPreferredSize(ExceptionText.Size);
                }
            }
            else
            {
                ExceptionText.Size = new Size(750, 350);
            }
            MessageLabel.Text = text;
            Text = caption;
            Icon = fatal ? SystemIcons.Error : SystemIcons.Exclamation;
            iconBitmap = Icon.ToBitmap();
            IconBox.Image = iconBitmap;
            TimeLabel.Text = DateTime.UtcNow.ToString("u", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Raised when the copy text button is clicked.
        /// Copy the exception text to the clipboard.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void CopyTextButton_Click(object sender, EventArgs e)
        {
            var text = string.Join("\r\n", Text, MessageLabel.Text, ExceptionText.Text, TimeLabel.Text);
            ThreadStart copy = () =>
            {
                var owner = InvokeRequired ? null : this;
                try
                {
                    Clipboard.SetText(text);
                    MessageBox.Show(owner, "Text copied to clipboard.",
                        "Text Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    MessageBox.Show(owner, "Failed to copy text to clipboard. Another process may be using the clipboard at this time.",
                        "Copy Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                copy();
            }
            else
            {
                var t = new Thread(copy);
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                t.Join();
            }
        }

        /// <summary>
        /// Raised when the close button is clicked.
        /// Closes the dialog.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                    components.Dispose();
                iconBitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
