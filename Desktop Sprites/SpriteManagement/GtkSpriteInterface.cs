﻿namespace DesktopSprites.SpriteManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using DesktopSprites.Collections;
    using DesktopSprites.Core;
    using Gdk;
    using Gtk;
    using SD = System.Drawing;

    // TODO: See what can be done to improve the memory leak. The way Gtk+ seems to handle unmanaged resources does not seem to perform
    // well in a managed environment, and Dispose() does not follow the usual C# idiom.
    // TODO: Fix abysmal performance.
    // TODO: Scaled drawing is not yet supported. Need to be able to adjust clipping masks and drawn images to fit given scale.

    /// <summary>
    /// Creates a series of windows using Gtk# to display sprites.
    /// </summary>
    /// <remarks>
    /// Creates an individual window for every sprite that is to be drawn. Transparency is handled automatically and the system works on
    /// all platforms and with practically no overhead. It should run reasonably under a virtual machine.
    /// The system does not scale well, as the underlying window system must handle an increasingly large number of windows. The system as
    /// a whole has no overhead, but each additional sprite incurs moderate and increasingly large overhead to manage and layer its window.
    /// There is a cost in modifying the collection of sprites between calls, as windows must be created for new sprites and destroyed for
    /// sprites no longer in the collection. It is extremely important to batch draws into one call, to prevent needlessly creating and
    /// destroying windows.
    /// </remarks>
    public sealed class GtkSpriteInterface : ISpriteCollectionView, IDisposable
    {
        #region NSWindow class
        /// <summary>
        /// Provides static methods that operate on a MacOSX NSWindow object.
        /// </summary>
        private static class NSWindow
        {
            /// <summary>
            /// Indicates whether the class is supported on the current operating system.
            /// </summary>
            private static bool isSupported = OperatingSystemInfo.IsMacOSX && OperatingSystemInfo.OSVersion >= new Version(10, 0);
            /// <summary>
            /// Gets a value indicating whether the class is supported on the current operating system.
            /// </summary>
            public static bool IsSupported
            {
                get { return isSupported; }
            }

            /// <summary>
            /// Pointer to the selector for the setHasShadow method.
            /// </summary>
            private static IntPtr setHasShadowSelector;

            /// <summary>
            /// Sets a value indicating whether to apply a drop shadow to the window.
            /// </summary>
            /// <param name="window">An instance of <see cref="T:Gdk.Window"/> whose underlying native window must be a MacOSX NSWindow.
            /// </param>
            /// <param name="hasShadow">Indicates whether to apply a drop shadow.</param>
            public static void SetHasShadow(Gdk.Window window, bool hasShadow)
            {
                // Get the native window handle.
                IntPtr nativeWindow =
                    Interop.MacOSX.NativeMethods.gdk_quartz_window_get_nswindow(window.Handle);

                // Register the method with the runtime, if it has not yet been.
                if (setHasShadowSelector == IntPtr.Zero)
                    setHasShadowSelector = Interop.MacOSX.NativeMethods.sel_registerName("setHasShadow:");

                // Send a message to the window, indicating the set shadow method and specified argument.
                Interop.MacOSX.NativeMethods.objc_msgSend(nativeWindow, setHasShadowSelector, hasShadow);

                // Keep the managed window from being garbage collected until native code is finished running.
                System.GC.KeepAlive(window);
            }
        }
        #endregion

        #region GraphicsWindow class
        /// <summary>
        /// Represents a single <see cref="T:Gtk.Window"/> for use as a canvas to draw a single sprite.
        /// </summary>
        private class GraphicsWindow : Gtk.Window
        {
            #region SpeechWindow class
            /// <summary>
            /// Represents a small pop-up window used to display speech bubbles.
            /// </summary>
            private class SpeechWindow : Gtk.Window
            {
                /// <summary>
                /// Gets or sets the text that appears inside the speech window.
                /// </summary>
                public string Text
                {
                    get { return ((Label)Child).Text; }
                    set { ((Label)Child).Text = value; }
                }

                /// <summary>
                /// Initializes a new instance of the
                /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow.SpeechWindow"/> class.
                /// </summary>
                public SpeechWindow()
                    : base(Gtk.WindowType.Popup)
                {
                    Decorated = false;
                    DoubleBuffered = false;

                    Child = new Label();
                    Child.Show();
                }

                /// <summary>
                /// Show the speech window centered and above the given location.
                /// </summary>
                /// <param name="x">The x co-ordinate of the location where the speech window should be horizontally centered.</param>
                /// <param name="y">The y co-ordinate of the location where the bottom of the speech window should coincide.</param>
                public void ShowAboveCenter(int x, int y)
                {
                    const int XPadding = 6;
                    const int YPadding = 2;
                    Requisition size = Child.SizeRequest();
                    Move(x - size.Width / 2 - XPadding, y - size.Height - YPadding);
                    Resize(size.Width + 2 * XPadding, size.Height + 2 * YPadding);
                    if (!Visible)
                        Show();
                }
            }
            #endregion

            /// <summary>
            /// Gets a value indicating whether the current windowing system supports RGBA (instead of RGB) for the surface of the window.
            /// </summary>
            public bool SupportsRgba { get; private set; }
            /// <summary>
            /// Indicates if the clipping mask is currently being actively updated.
            /// </summary>
            private bool updatingMask = false;
            /// <summary>
            /// The <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow.SpeechWindow"/> that provides the
            /// ability to display speech bubbles for this
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/>.
            /// </summary>
            private SpeechWindow speechBubble;
            /// <summary>
            /// Gets or sets the current <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/> that will be used
            /// for clipping and drawing a sprite.
            /// </summary>
            public ClippedImage CurrentImage { get; set; }
            /// <summary>
            /// The last image that was drawn, used to prevent re-drawing.
            /// </summary>
            private Pixbuf lastImage;
            /// <summary>
            /// A counter that ensures an initial drawing of the image is done, particularly so static images appear. Hacky.
            /// </summary>
            private int initialDrawCountHack = 0;
            /// <summary>
            /// The last clip that was applied, used to prevent re-applying.
            /// </summary>
            private Pixmap lastClip;
            /// <summary>
            /// The last known width of the window, used to prevent clearing the existing portion of a resized window.
            /// </summary>
            private int lastWidth;
            /// <summary>
            /// The last known height of the window, used to prevent clearing the existing portion of a resized window.
            /// </summary>
            private int lastHeight;

            /// <summary>
            /// Gets or sets the <see cref="T:DesktopSprites.SpriteManagement.ISprite"/> that this
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> is responsible for drawing.
            /// </summary>
            public ISprite Sprite { get; set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> class.
            /// </summary>
            public GraphicsWindow()
                : base(Gtk.WindowType.Toplevel)
            {
                DetermineAlphaSupport();

                AppPaintable = true;
                Decorated = false;
                DoubleBuffered = false;
                SkipTaskbarHint = true;

                speechBubble = new SpeechWindow();

                Events |= EventMask.ButtonPressMask | EventMask.ButtonReleaseMask |
                    EventMask.KeyPressMask | EventMask.EnterNotifyMask | EventMask.FocusChangeMask;
            }

            /// <summary>
            /// Determines whether the screen the window is on supports an alpha channel, and sets the color map accordingly.
            /// </summary>
            private void DetermineAlphaSupport()
            {
                Colormap = Screen.RgbaColormap ?? Screen.RgbColormap;
                SupportsRgba = Screen.RgbaColormap != null;
            }

            /// <summary>
            /// Raises the realize event.
            /// Removes drop shadow on MacOSX systems.
            /// </summary>
            protected override void OnRealized()
            {
                base.OnRealized();
                if (NSWindow.IsSupported)
                    NSWindow.SetHasShadow(GdkWindow, false);
            }

            /// <summary>
            /// Raises the screen changed event.
            /// Determines whether the new screen supports an alpha channel.
            /// </summary>
            /// <param name="previousScreen">The previous screen.</param>
            protected override void OnScreenChanged(Screen previousScreen)
            {
                DetermineAlphaSupport();
                base.OnScreenChanged(previousScreen);
            }

            /// <summary>
            /// Raises the configure event.
            /// Clears new areas of a window who size has increased to be transparent, and ensures clipping mask is up to date.
            /// </summary>
            /// <param name='evnt'>Data about the event.</param>
            /// <returns>Returns true to stop other handlers being invoked; otherwise, false.</returns>
            protected override bool OnConfigureEvent(EventConfigure evnt)
            {
                Argument.EnsureNotNull(evnt, nameof(evnt));

                var newWidth = evnt.Width;
                var newHeight = evnt.Height;

                // We can only clear newly exposed areas if we support RGBA drawing.
                if (SupportsRgba)
                {
                    // Clear the window to be transparent in the newly exposed areas.
                    if (newWidth > lastWidth || newHeight > lastHeight)
                    {
                        using (var newRegion = new Region())
                        {
                            // Right edge.
                            if (newWidth > lastWidth)
                                newRegion.UnionWithRect(new Rectangle(
                                    lastWidth, 0, newWidth - lastWidth, lastHeight));
                            // Bottom edge.
                            if (newHeight > lastHeight)
                                newRegion.UnionWithRect(new Rectangle(
                                    0, lastHeight, lastWidth, newHeight - lastHeight));
                            // Bottom-right corner.
                            if (newWidth > lastWidth && newHeight > lastHeight)
                                newRegion.UnionWithRect(new Rectangle(
                                    lastWidth, lastHeight, newWidth - lastWidth, newHeight - lastHeight));

                            // Create the Cairo context for possible alpha drawing. A context may not be reused, and must be recreated
                            // each draw. The context also implements IDisposable, and thus should be disposed after use.
                            using (Cairo.Context context = CairoHelper.Create(GdkWindow))
                            {
                                context.Antialias = Cairo.Antialias.None;
                                if (newRegion == null)
                                    Console.WriteLine("Region empty.");
                                CairoHelper.Region(context, newRegion);

                                if (SupportsRgba)
                                {
                                    // Clear the window to be transparent.
                                    context.Operator = Cairo.Operator.Source;
                                    context.SetSourceRGBA(0, 0, 0, 0);
                                    context.Paint();
                                }
                            }
                        }
                    }
                }
                lastWidth = newWidth;
                lastHeight = newHeight;

                // Update clipping area to cover the whole of the newly resized window.
                if (SupportsRgba && !updatingMask)
                {
                    using (var all = new Region())
                    {
                        all.UnionWithRect(new Rectangle(0, 0, newWidth, newHeight));
                        GdkWindow.InputShapeCombineRegion(all, 0, 0);
                    }
                    lastClip = null;
                }

                return base.OnConfigureEvent(evnt);
            }

            /// <summary>
            /// Raises the enter notify event.
            /// Triggers active updating of the input mask on RGBA supported windows.
            /// </summary>
            /// <param name="evnt">Data about the event.</param>
            /// <returns>Returns true to stop other handlers being invoked; otherwise, false.</returns>
            protected override bool OnEnterNotifyEvent(EventCrossing evnt)
            {
                if (SupportsRgba && CurrentImage != null)
                {
                    // Start actively updating the input mask for RGBA supported windows.
                    updatingMask = true;
                    GetSize(out var width, out var height);
                    SetClip(width, height);
                }
                return base.OnEnterNotifyEvent(evnt);
            }

            /// <summary>
            /// Prevents raising of the delete event.
            /// </summary>
            /// <param name="evnt">Data about the event.</param>
            /// <returns>Returns true to stop other handlers being invoked.</returns>
            protected override bool OnDeleteEvent(Event evnt)
            {
                return true;
            }

            /// <summary>
            /// Raised when the window is destroyed.
            /// Also destroys the speech bubble window.
            /// </summary>
            protected override void OnDestroyed()
            {
                speechBubble.Destroy();
                base.OnDestroyed();
            }

            /// <summary>
            /// Sets the clipping region of the window, based on the current sprite.
            /// </summary>
            /// <param name="width">The width to fit the clipping region to, scaling as required.</param>
            /// <param name="height">The height to fit the clipping region to, scaling as required.</param>
            public void SetClip(int width, int height)
            {
                if (CurrentImage == null)
                    throw new InvalidOperationException("This method may not be called until CurrentImage has been set.");

                if (!SupportsRgba)
                {
                    if (lastClip != CurrentImage.Clip)
                    {
                        // If an alpha channel is not supported, we must constantly update the clipping area for visual and input
                        // transparency.
                        GdkWindow.ShapeCombineMask(CurrentImage.Clip, 0, 0);
                        lastClip = CurrentImage.Clip;
                    }
                }
                else if (updatingMask)
                {
                    // Only update the input mask, since alpha is already taken care of.
                    GetPointer(out var x, out var y);
                    if (x < 0 || y < 0 || x > width || y > height)
                    {
                        if (lastClip != null)
                        {
                            // The cursor is no longer over the window, so we can clear the region to something cheaper to evaluate.
                            updatingMask = false;
                            using (var all = new Region())
                            {
                                all.UnionWithRect(new Rectangle(0, 0, width, height));
                                GdkWindow.InputShapeCombineRegion(all, 0, 0);
                            }
                            lastClip = null;
                        }
                    }
                    else
                    {
                        if (lastClip != CurrentImage.Clip)
                        {
                            // Update the mask if the cursor is still over us.
                            GdkWindow.InputShapeCombineMask(CurrentImage.Clip, 0, 0);
                            lastClip = CurrentImage.Clip;
                        }
                    }
                }
            }

            /// <summary>
            /// Draws the current sprite onto the window.
            /// </summary>
            /// <param name="width">The width to draw the image at, scaling as required.</param>
            /// <param name="height">The height to draw the image at, scaling as required.</param>
            public void DrawFrame(int width, int height)
            {
                if (CurrentImage == null)
                    return;

                // Prevent redrawing of the same image.
                if (initialDrawCountHack < 1)
                {
                    initialDrawCountHack++;
                    lastImage = CurrentImage.Image;
                }
                else if (lastImage == CurrentImage.Image)
                {
                    return;
                }
                else
                {
                    initialDrawCountHack = 0;
                    lastImage = CurrentImage.Image;
                }

                // Create the Cairo context for possible alpha drawing. A context may not be reused, and must be recreated each draw.
                // The context also implements IDisposable, and thus should be disposed after use.
                using (Cairo.Context context = CairoHelper.Create(GdkWindow))
                {
                    context.Antialias = Cairo.Antialias.None;

                    // Draw the current sprite.
                    context.Operator = Cairo.Operator.Source;
                    CairoHelper.SetSourcePixbuf(context, CurrentImage.Image, 0, 0);
                    context.Paint();
                }
            }

            /// <summary>
            /// Shows a speech bubble with the given text, centered and above the given point.
            /// </summary>
            /// <param name="text">The speech to display inside the bubble.</param>
            /// <param name="x">The x co-ordinate of the location where the speech window should be horizontally centered.</param>
            /// <param name="y">The y co-ordinate of the location where the bottom of the speech window should coincide.</param>
            public void ShowSpeech(string text, int x, int y)
            {
                speechBubble.Text = text;
                speechBubble.ShowAboveCenter(x, y);
            }

            /// <summary>
            /// Hides the speech bubble.
            /// </summary>
            public void HideSpeech()
            {
                if (speechBubble.Visible)
                    speechBubble.Hide();
            }
        }
        #endregion

        #region ClippedImage class
        /// <summary>
        /// Represents an image and its associated clipping mask.
        /// </summary>
        private class ClippedImage : IDisposable
        {
            /// <summary>
            /// Gets or sets the image itself.
            /// </summary>
            public Pixbuf Image { get; set; }
            /// <summary>
            /// Gets or sets the clipping mask for the image.
            /// </summary>
            public Pixmap Clip { get; set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/> class.
            /// </summary>
            public ClippedImage()
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/> class
            /// from the given file.
            /// </summary>
            /// <param name="gtkSpriteInterface">The interface that will own this image (resources will be created on the UI thread).
            /// </param>
            /// <param name="fileName">The path to a static image file from which to create a new
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/>.</param>
            public ClippedImage(GtkSpriteInterface gtkSpriteInterface, string fileName)
            {
                gtkSpriteInterface.ApplicationInvoke(() =>
                {
                    // Create the image and get its clipping mask.
                    Image = new Pixbuf(fileName);
                    Image.RenderPixmapAndMask(out Pixmap clipMap, out Pixmap clipMask, 255);
                    Clip = clipMask;
                    clipMap.Dispose();
                });
            }

            /// <summary>
            /// Releases all resources used by the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/> object.
            /// </summary>
            public void Dispose()
            {
                // The image is IDisposable and should be disposed, despite lacking a public method.
                ((IDisposable)Image).Dispose();
                Clip.Dispose();
            }
        }
        #endregion

        #region GtkFrame class
        /// <summary>
        /// Defines a <see cref="T:DesktopSprites.SpriteManagement.Frame`1"/> whose underlying image is a
        /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/>.
        /// </summary>
        private class GtkFrame : Frame<ClippedImage>, IDisposable
        {
            /// <summary>
            /// Creates a new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> from the raw buffer.
            /// </summary>
            /// <param name="gtkSpriteInterface">The interface that will own this image (resources will be created on the UI thread).
            /// </param>
            /// <param name="buffer">The raw buffer.</param>
            /// <param name="palette">The color palette.</param>
            /// <param name="transparentIndex">The index of the transparent color.</param>
            /// <param name="stride">The stride width of the buffer.</param>
            /// <param name="width">The logical width of the buffer.</param>
            /// <param name="height">The logical height of the buffer.</param>
            /// <param name="depth">The bit depth of the buffer.</param>
            /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> for the frame held in the raw
            /// buffer.</returns>
            /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="depth"/> is not 8.</exception>
            public static GtkFrame FromBuffer(GtkSpriteInterface gtkSpriteInterface,
                byte[] buffer, RgbColor[] palette, byte? transparentIndex, int stride, int width, int height, byte depth)
            {
                if (depth != 8)
                    throw new ArgumentOutOfRangeException(nameof(depth), depth, "depth must be 8.");

                var frameImage = new ClippedImage();
                var points = new List<Point>((int)Math.Ceiling((float)width * height / 8f));

                // Create a data buffer to hold 32bbp RGBA values.
                var data = new byte[width * height * 4];

                // Loop over the pixels in each row (to account for stride width of the source).
                for (var row = 0; row < height; row++)
                    for (var x = 0; x < width; x++)
                    {
                        // Get the index value from the 8bbp source.
                        var index = buffer[row * stride + x];
                        // Get the destination offset in the 32bbp array.
                        var offset = 4 * (width * row + x);
                        if (index != transparentIndex)
                        {
                            // Get the color from the palette, and set the RGB values.
                            RgbColor color = palette[index];
                            data[offset + 0] = color.R;
                            data[offset + 1] = color.G;
                            data[offset + 2] = color.B;
                            data[offset + 3] = 255;

                            // Save the point for creating the mask later.
                            points.Add(new Point(x, row));
                        }
                        else
                        {
                            // This color is transparent.
                            data[offset + 3] = 0;
                        }
                    }

                // Create the clipping mask by setting all the pixels in the mask from the list of points we draw on.
                gtkSpriteInterface.ApplicationInvoke(() => frameImage.Clip = new Pixmap(null, width, height, 1));
                if (points.Count > 0)
                    using (var context = new Gdk.GC(frameImage.Clip))
                    {
                        context.Function = Gdk.Function.Set;
                        frameImage.Clip.DrawPoints(context, points.ToArray());
                    }

                // Create the image from the data array.
                frameImage.Image = new Pixbuf(data, true, 8, width, height, width * 4);

                var hashCode = GifImage.GetHash(buffer, palette, transparentIndex, width, height);

                return new GtkFrame(frameImage, hashCode);
            }

            /// <summary>
            /// Gets the set of allowable bit depths for a <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/>.
            /// </summary>
            public static BitDepths AllowableBitDepths
            {
                get { return BitDepths.Indexed8Bpp; }
            }

            /// <summary>
            /// The hash code of the frame.
            /// </summary>
            private int hashCode;

            /// <summary>
            /// Gets the dimensions of the frame.
            /// </summary>
            public override SD.Size Size
            {
                get { return new SD.Size(Image.Image.Width, Image.Image.Height); }
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> class from
            /// the given <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/>.
            /// </summary>
            /// <param name="clippedImage">The <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.ClippedImage"/> to use in
            /// the frame.</param>
            /// <param name="hash">The hash code of the frame.</param>
            public GtkFrame(ClippedImage clippedImage, int hash)
                : base(clippedImage)
            {
                hashCode = hash;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> class from
            /// the given file.
            /// </summary>
            /// <param name="gtkSpriteInterface">The interface that will own this image (resources will be created on the UI thread).
            /// </param>
            /// <param name="fileName">The path to a static image file from which to create a new
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/>.</param>
            public GtkFrame(GtkSpriteInterface gtkSpriteInterface, string fileName)
                : this(new ClippedImage(gtkSpriteInterface, fileName), fileName.GetHashCode())
            {
            }

            /// <summary>
            /// Gets the hash code of the frame.
            /// </summary>
            /// <returns>A hash code for this frame.</returns>
            public override int GetFrameHashCode()
            {
                return hashCode;
            }

            /// <summary>
            /// Releases all resources used by the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> object.
            /// </summary>
            public void Dispose()
            {
                Image.Dispose();
            }
        }
        #endregion

        #region GtkContextMenuItem class
        /// <summary>
        /// Wraps a <see cref="T:Gtk.MenuItem"/> in order to expose the
        /// <see cref="T:DesktopSprites.SpriteManagement.ISimpleContextMenuItem"/> interface.
        /// </summary>
        private class GtkContextMenuItem : ISimpleContextMenuItem, IDisposable
        {
            /// <summary>
            /// The wrapped <see cref="T:Gtk.MenuItem"/>.
            /// </summary>
            private MenuItem item;
            /// <summary>
            /// The method that runs on activation.
            /// </summary>
            private EventHandler activatedMethod;
            /// <summary>
            /// The method that runs on activation, queued to the <see cref="T:System.Threading.ThreadPool"/>.
            /// </summary>
            private EventHandler queuedActivatedMethod;
            /// <summary>
            /// The method that runs on button press, queued to the <see cref="T:System.Threading.ThreadPool"/>.
            /// </summary>
            private ButtonPressEventHandler queuedButtonPressMethod;
            /// <summary>
            /// Indicates if the item exists on a top level menu, instead of a sub-menu.
            /// </summary>
            private bool topLevel;

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenuItem"/>
            /// class for the given <see cref="T:Gtk.SeparatorMenuItem"/>.
            /// </summary>
            /// <param name="separatorItem">The underlying <see cref="T:Gtk.SeparatorMenuItem"/> that this class wraps.</param>
            /// <param name="topLevel">Indicates if the menu item is in a top level menu.</param>
            /// <exception cref="T:System.ArgumentNullException"><paramref name="separatorItem"/> is null.</exception>
            public GtkContextMenuItem(SeparatorMenuItem separatorItem, bool topLevel)
            {
                Argument.EnsureNotNull(separatorItem, nameof(separatorItem));
                this.topLevel = topLevel;
                item = separatorItem;
            }
            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenuItem"/>
            /// class for the given <see cref="T:Gtk.MenuItem"/>, and links up the given activation method.
            /// </summary>
            /// <param name="menuItem">The underlying <see cref="T:Gtk.MenuItem"/> that this class wraps.</param>
            /// <param name="activated">The method to be run when the item is activated.</param>
            /// <param name="topLevel">Indicates if the menu item is in a top level menu.</param>
            /// <exception cref="T:System.ArgumentNullException"><paramref name="menuItem"/> is null.</exception>
            public GtkContextMenuItem(MenuItem menuItem, EventHandler activated, bool topLevel)
            {
                Argument.EnsureNotNull(menuItem, nameof(menuItem));
                this.topLevel = topLevel;
                item = menuItem;
                Activated = activated;
            }
            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenuItem"/>
            /// class for the given <see cref="T:Gtk.MenuItem"/>, and links up the activation method to display a new sub-menu.
            /// </summary>
            /// <param name="menuItem">The underlying <see cref="T:Gtk.MenuItem"/> that this class wraps.</param>
            /// <param name="subItems">The items to appear in the sub-menu.</param>
            /// <param name="parent">The <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> that will own this
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenuItem"/>.</param>
            /// <param name="topLevel">Indicates if the menu item is in a top level menu.</param>
            /// <exception cref="T:System.ArgumentNullException"><paramref name="menuItem"/> is null.-or-<paramref name="subItems"/> is
            /// null.-or-<paramref name="parent"/> is null.</exception>
            /// <exception cref="T:System.ArgumentException"><paramref name="subItems"/> is empty.</exception>
            public GtkContextMenuItem(MenuItem menuItem, IEnumerable<ISimpleContextMenuItem> subItems, GtkSpriteInterface parent,
                bool topLevel)
            {
                Argument.EnsureNotNull(menuItem, nameof(menuItem));
                Argument.EnsureNotNullOrEmpty(subItems, nameof(subItems));
                this.topLevel = topLevel;
                item = menuItem;
                var gtkContextMenu = new GtkContextMenu(parent, subItems, false);
                item.Submenu = gtkContextMenu;
                SubItems = gtkContextMenu.Items;
            }

            /// <summary>
            /// Gets or sets a value indicating whether the item is a separator.
            /// </summary>
            public bool IsSeparator
            {
                get
                {
                    return item is SeparatorMenuItem;
                }
                set
                {
                    if (IsSeparator && !value)
                        item = new MenuItem();
                    else if (!IsSeparator && value)
                        item = new SeparatorMenuItem();
                }
            }
            /// <summary>
            /// Gets or sets the text displayed to represent this item.
            /// </summary>
            /// <exception cref="T:System.InvalidOperationException">The item is a separator.</exception>
            public string Text
            {
                get
                {
                    if (IsSeparator)
                        throw new InvalidOperationException("Cannot get the text from a separator item.");
                    return ((Label)item.Child).Text;
                }
                set
                {
                    if (IsSeparator)
                        throw new InvalidOperationException("Cannot set the text on a separator item.");
                    ((Label)item.Child).Text = value;
                }
            }
            /// <summary>
            /// Gets or sets the method that runs when the item is activated by the user.
            /// </summary>
            /// <exception cref="T:System.InvalidOperationException">The item does not support an activation method.</exception>
            public EventHandler Activated
            {
                get
                {
                    if (IsSeparator || SubItems != null)
                        throw new InvalidOperationException("Cannot get the activation method from this type of item.");
                    return activatedMethod;
                }
                set
                {
                    if (IsSeparator || SubItems != null)
                        throw new InvalidOperationException("Cannot set the activation method on this type of item.");
                    if (queuedActivatedMethod != null)
                        item.Activated -= queuedActivatedMethod;
                    if (queuedButtonPressMethod != null)
                        item.ButtonPressEvent -= queuedButtonPressMethod;

                    activatedMethod = value;
                    queuedActivatedMethod = null;
                    queuedButtonPressMethod = null;

                    if (activatedMethod != null)
                    {
                        queuedActivatedMethod = (o, args) => ThreadPool.QueueUserWorkItem(obj => activatedMethod(o, args));
                        item.Activated += queuedActivatedMethod;
                        if (!topLevel)
                        {
                            queuedButtonPressMethod = (o, args) => ThreadPool.QueueUserWorkItem(obj => activatedMethod(o, args));
                            item.ButtonPressEvent += queuedButtonPressMethod;
                        }
                    }
                }
            }
            /// <summary>
            /// Gets the sub-items in an item that displays a new sub-menu of items.
            /// </summary>
            public IList<ISimpleContextMenuItem> SubItems { get; private set; }

            /// <summary>
            /// Releases all resources used by the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenuItem"/>
            /// object.
            /// </summary>
            public void Dispose()
            {
                item.Dispose();

                if (SubItems != null)
                    foreach (IDisposable subitem in SubItems)
                        subitem.Dispose();
            }
        }
        #endregion

        #region GtkContextMenu class
        /// <summary>
        /// Extends a <see cref="T:Gtk.Menu"/> in order to expose the <see cref="T:DesktopSprites.SpriteManagement.ISimpleContextMenu"/>
        /// interface.
        /// </summary>
        private class GtkContextMenu : Menu, ISimpleContextMenu, IDisposable
        {
            /// <summary>
            /// The <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> that owns this
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenu"/>.
            /// </summary>
            private GtkSpriteInterface owner;
            /// <summary>
            /// Gets the collection of menu items in this menu.
            /// </summary>
            public IList<ISimpleContextMenuItem> Items { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenu"/> class
            /// to display the given menu items.
            /// </summary>
            /// <param name="parent">The <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> that will own this
            /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenu"/>.</param>
            /// <param name="menuItems">The items which should be displayed in this menu.</param>
            /// <param name="topLevel">Indicates if the menu is a top level menu, instead of a sub-menu.</param>
            /// <exception cref="T:System.ArgumentNullException"><paramref name="parent"/> is null.-or-<paramref name="menuItems"/> is
            /// null.</exception>
            public GtkContextMenu(GtkSpriteInterface parent, IEnumerable<ISimpleContextMenuItem> menuItems, bool topLevel)
            {
                Argument.EnsureNotNull(parent, nameof(parent));
                Argument.EnsureNotNull(menuItems, nameof(menuItems));

                owner = parent;

                var items = new List<ISimpleContextMenuItem>();
                Items = items;

                foreach (ISimpleContextMenuItem menuItem in menuItems)
                {
                    MenuItem gtkMenuItem;
                    if (menuItem.IsSeparator)
                        gtkMenuItem = new SeparatorMenuItem();
                    else
                        gtkMenuItem = new MenuItem(menuItem.Text);
                    Append(gtkMenuItem);
                    gtkMenuItem.Show();

                    GtkContextMenuItem gtkContextMenuItem;
                    if (menuItem.IsSeparator)
                        gtkContextMenuItem = new GtkContextMenuItem((SeparatorMenuItem)gtkMenuItem, topLevel);
                    else if (menuItem.SubItems == null)
                        gtkContextMenuItem = new GtkContextMenuItem(gtkMenuItem, menuItem.Activated, topLevel);
                    else
                        gtkContextMenuItem = new GtkContextMenuItem(gtkMenuItem, menuItem.SubItems, owner, topLevel);

                    items.Add(gtkContextMenuItem);
                }
            }

            /// <summary>
            /// Displays the context menu at the given co-ordinates.
            /// </summary>
            /// <param name="x">The x co-ordinate of the location where the menu should be shown.</param>
            /// <param name="y">The y co-ordinate of the location where the menu should be shown.</param>
            public void Show(int x, int y)
            {
                owner.ApplicationInvoke(() => Popup());
            }

            /// <summary>
            /// Releases all resources used by the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenu"/>
            /// object.
            /// </summary>
            public override void Dispose()
            {
                foreach (GtkContextMenuItem item in Items)
                    item.Dispose();
                base.Dispose();
            }
        }
        #endregion

        #region Fields and Properties
        /// <summary>
        /// Indicates if we have disposed of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/>.
        /// </summary>
        private static bool disposed = true;
        /// <summary>
        /// Indicates if we have yet attached to the static <see cref="E:GLib.ExceptionManager.UnhandledException"/> event.
        /// </summary>
        private static bool exceptionsRaised;
        /// <summary>
        /// Stores the images for each sprite as a series of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/>,
        /// indexed by filename.
        /// </summary>
        private readonly Dictionary<string, AnimatedImage<GtkFrame>> images =
            new Dictionary<string, AnimatedImage<GtkFrame>>(SpriteImagePaths.Comparer);
        /// <summary>
        /// Stores the animation pairs to use when drawing sprites, indexed by their path pairs.
        /// </summary>
        private readonly Dictionary<SpriteImagePaths, AnimationPair<GtkFrame>> animationPairsByPaths =
            new Dictionary<SpriteImagePaths, AnimationPair<GtkFrame>>();
        /// <summary>
        /// Delegate to the CreatePair function.
        /// </summary>
        private readonly Func<SpriteImagePaths, AnimationPair<GtkFrame>> createPair;
        /// <summary>
        /// Delegate to the CreateAnimatedImage function.
        /// </summary>
        private readonly Func<string, AnimatedImage<GtkFrame>> createAnimatedImage;
        /// <summary>
        /// List of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkContextMenu"/> which have been created by the
        /// interface.
        /// </summary>
        private readonly LinkedList<GtkContextMenu> contextMenus = new LinkedList<GtkContextMenu>();
        /// <summary>
        /// Indicates if we started a new UI thread, otherwise we are borrowing the callers thread.
        /// </summary>
        private readonly bool dedicatedAppThread;
        /// <summary>
        /// The thread running the application.
        /// </summary>
        private readonly Thread appThread;
        /// <summary>
        /// Indicates if drawing is paused.
        /// </summary>
        private bool paused;
        /// <summary>
        /// Synchronization object used to prevent other operations conflicting with drawing.
        /// </summary>
        private readonly object drawSync = new object();
        /// <summary>
        /// Links a <see cref="T:DesktopSprites.SpriteManagement.ISprite"/> to the
        /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> responsible for drawing it.
        /// </summary>
        private readonly Dictionary<ISprite, GraphicsWindow> spriteWindows = new Dictionary<ISprite, GraphicsWindow>();
        /// <summary>
        /// Maintains the list of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> in the desired draw
        /// order.
        /// </summary>
        private readonly List<GraphicsWindow> drawOrderedWindows = new List<GraphicsWindow>(0);
        /// <summary>
        /// Maintains the list of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> that were removed
        /// since last draw.
        /// </summary>
        private readonly List<ISprite> removedSprites = new List<ISprite>();
        /// <summary>
        /// Title for instances of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/>.
        /// </summary>
        private string windowTitle = "Gtk# Sprite Window";
        /// <summary>
        /// Path to the icon for instances of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/>.
        /// </summary>
        private string windowIconFilePath = null;
        /// <summary>
        /// Icon for instances of <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/>.
        /// </summary>
        private Pixbuf windowIcon = null;
        /// <summary>
        /// Indicates if each <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> should act as a topmost
        /// window.
        /// </summary>
        private bool windowTopmost = true;
        /// <summary>
        /// Records the time the last mouse down event occurred.
        /// </summary>
        private DateTime mouseDownTime;

        /// <summary>
        /// Synchronization object for use when invoking a method on the application thread.
        /// </summary>
        private readonly object invokeSync = new object();

        /// <summary>
        /// Gets a value indicating whether the object is being, or has been, disposed.
        /// </summary>
        public bool Disposed
        {
            get { return disposed; }
        }
        /// <summary>
        /// Gets or sets a value indicating whether the interface will be allowed to close itself. If the interface needs to close itself,
        /// it will attempt to do so as soon as this property is set to true.
        /// </summary>
        public bool PreventSelfClose { get; set; }
        /// <summary>
        /// Gets or sets the text to use in the title frame of each window.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public string WindowTitle
        {
            get
            {
                EnsureNotDisposed();
                return windowTitle;
            }
            set
            {
                EnsureNotDisposed();
                windowTitle = value;
                ApplicationInvoke(() =>
                {
                    foreach (GraphicsWindow window in spriteWindows.Values)
                        window.Title = windowTitle;
                });
            }
        }
        /// <summary>
        /// Gets or sets the icon used for each window to the icon at the given path.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public string WindowIconFilePath
        {
            get
            {
                EnsureNotDisposed();
                return windowIconFilePath;
            }
            set
            {
                EnsureNotDisposed();
                windowIconFilePath = value;
                ApplicationInvoke(() =>
                {
                    if (windowIcon != null)
                        windowIcon.Dispose();
                    if (windowIconFilePath == null)
                        windowIcon = null;
                    else
                        windowIcon = new Pixbuf(windowIconFilePath);

                    foreach (GraphicsWindow window in spriteWindows.Values)
                        window.Icon = windowIcon;
                });
            }
        }
        /// <summary>
        /// Gets or sets a value indicating whether windows will display as topmost windows.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public bool Topmost
        {
            get
            {
                EnsureNotDisposed();
                return windowTopmost;
            }
            set
            {
                EnsureNotDisposed();
                windowTopmost = value;
                ApplicationInvoke(() =>
                {
                    foreach (GraphicsWindow window in spriteWindows.Values)
                        window.KeepAbove = windowTopmost;
                });
            }
        }
        /// <summary>
        /// Gets or sets a value indicating whether a window should appear in the taskbar.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public bool ShowInTaskbar
        {
            get
            {
                EnsureNotDisposed();
                return false;
            }
            set
            {
                EnsureNotDisposed();
            }
        }
        /// <summary>
        /// Gets the current location of the cursor.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public SD.Point CursorPosition
        {
            get
            {
                EnsureNotDisposed();
                GetPointer(out var x, out var y, out ModifierType mod);
                return new SD.Point(x, y);
            }
        }
        /// <summary>
        /// Gets the mouse buttons which are currently held down.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public SimpleMouseButtons MouseButtonsDown
        {
            get
            {
                EnsureNotDisposed();
                GetPointer(out var x, out var y, out ModifierType mod);
                SimpleMouseButtons buttons = SimpleMouseButtons.None;
                if ((mod & ModifierType.Button1Mask) == ModifierType.Button1Mask)
                    buttons |= SimpleMouseButtons.Left;
                if ((mod & ModifierType.Button2Mask) == ModifierType.Button2Mask)
                    buttons |= SimpleMouseButtons.Middle;
                if ((mod & ModifierType.Button3Mask) == ModifierType.Button3Mask)
                    buttons |= SimpleMouseButtons.Right;
                return buttons;
            }
        }

        /// <summary>
        /// Gets the mouse pointer state.
        /// </summary>
        /// <param name="x">When this method returns, contains the x location of the mouse in screen coordinates.</param>
        /// <param name="y">When this method returns, contains the y location of the mouse in screen coordinates.</param>
        /// <param name="mod">When this method returns, contains the state of the modifier keys and mouse buttons.</param>
        private void GetPointer(out int x, out int y, out ModifierType mod)
        {
            int x1 = 0, y1 = 0;
            ModifierType mod1 = ModifierType.None;
            ApplicationInvoke(() =>
                (drawOrderedWindows.Count > 0 ? drawOrderedWindows[0].Display : Display.Default).GetPointer(out x1, out y1, out mod1));
            x = x1;
            y = y1;
            mod = mod1;
        }

        /// <summary>
        /// Gets a value indicating whether the interface has input focus.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public bool HasFocus
        {
            get
            {
                EnsureNotDisposed();
                var hasFocus = false;
                ApplicationInvoke(() =>
                {
                    foreach (var window in drawOrderedWindows)
                        if (window.HasToplevelFocus)
                        {
                            hasFocus = true;
                            break;
                        }
                });
                return hasFocus;
            }
        }

        /// <summary>
        /// Gets or sets an optional function that pre-processes a decoded GIF buffer before the buffer is used by the viewer.
        /// </summary>
        public BufferPreprocess BufferPreprocess { get; set; }

        /// <summary>
        /// Gets or sets the FrameRecordCollector for debugging purposes.
        /// </summary>
        internal AnimationLoopBase.FrameRecordCollector Collector { get; set; }
        #endregion

        #region Events
        /// <summary>
        /// Gets the equivalent <see cref="T:DesktopSprites.SpriteManagement.SimpleMouseButtons"/> enumeration from the native button
        /// code.
        /// </summary>
        /// <param name="button">The code of the mouse button that was pressed.</param>
        /// <returns>The equivalent <see cref="T:DesktopSprites.SpriteManagement.SimpleMouseButtons"/> enumeration for this button.
        /// </returns>
        private static SimpleMouseButtons GetButtonsFromNative(uint button)
        {
            switch (button)
            {
                case 1: return SimpleMouseButtons.Left;
                case 2: return SimpleMouseButtons.Middle;
                case 3: return SimpleMouseButtons.Right;
                default: return SimpleMouseButtons.None;
            }
        }
        /// <summary>
        /// Raised when a mouse button has been pressed down.
        /// Raises the MouseDown event.
        /// </summary>
        /// <param name="o">The object that raised the event.</param>
        /// <param name="args">Data about the event.</param>
        private void GraphicsWindow_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            var button = GetButtonsFromNative(args.Event.Button);
            if (button == SimpleMouseButtons.None)
                return;
            mouseDownTime = DateTime.UtcNow;
        }
        /// <summary>
        /// Raised when a mouse button has been released.
        /// Raises the MouseClick and MouseUp events.
        /// </summary>
        /// <param name="o">The object that raised the event.</param>
        /// <param name="args">Data about the event.</param>
        private void GraphicsWindow_ButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
        {
            var button = GetButtonsFromNative(args.Event.Button);
            if (button == SimpleMouseButtons.None)
                return;
            var e = new SimpleMouseEventArgs(button, (int)args.Event.XRoot, (int)args.Event.YRoot);
            var doubleClickMillisesonds =
                (drawOrderedWindows.Count > 0 ?
                Settings.GetForScreen(drawOrderedWindows[0].Screen) :
                Settings.Default).DoubleClickTime;
            if (DateTime.UtcNow - mouseDownTime <= TimeSpan.FromMilliseconds(doubleClickMillisesonds))
                MouseClick.Raise(this, e);
        }
        /// <summary>
        /// Raised when a key has been pressed.
        /// Raises the KeyPress event.
        /// </summary>
        /// <param name="o">The object that raised the event.</param>
        /// <param name="args">Data about the event.</param>
        private void GraphicsWindow_KeyPressEvent(object o, KeyPressEventArgs args)
        {
            KeyPress.Raise(this, () => new SimpleKeyEventArgs((char)args.Event.KeyValue));
        }

        /// <summary>
        /// Occurs when a key is pressed while a window has focus.
        /// </summary>
        public event EventHandler<SimpleKeyEventArgs> KeyPress;
        /// <summary>
        /// Occurs when a window is clicked by the mouse.
        /// </summary>
        public event EventHandler<SimpleMouseEventArgs> MouseClick;
        /// <summary>
        /// Occurs when the interface gains input focus.
        /// </summary>
        public event EventHandler Focused;
        /// <summary>
        /// Occurs when the interface loses input focus.
        /// </summary>
        public event EventHandler Unfocused;
        /// <summary>
        /// Occurs when the interface is closed.
        /// </summary>
        public event EventHandler InterfaceClosed;
        #endregion

        /// <summary>
        /// Gets a value indicating whether a <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> can be used in the
        /// current environment.
        /// </summary>
        public static bool IsRunable
        {
            get
            {
                try
                {
                    var domain = AppDomain.CreateDomain("Assembly Test Domain");
                    domain.Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
                    domain.Load("atk-sharp, Version=2.12.0.0, Culture=neutral, PublicKeyToken=35e10195dab3c99f");
                    domain.Load("gdk-sharp, Version=2.12.0.0, Culture=neutral, PublicKeyToken=35e10195dab3c99f");
                    domain.Load("glib-sharp, Version=2.12.0.0, Culture=neutral, PublicKeyToken=35e10195dab3c99f");
                    //domain.Load("gtk-dotnet, Version=2.12.0.0, Culture=neutral, PublicKeyToken=35e10195dab3c99f");
                    domain.Load("gtk-sharp, Version=2.12.0.0, Culture=neutral, PublicKeyToken=35e10195dab3c99f");
                    domain.Load("Mono.Cairo, Version=2.0.0.0, Culture=neutral, PublicKeyToken=0738eb9f132ed756");
                    AppDomain.Unload(domain);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> class with a dedicated
        /// thread.
        /// </summary>
        public GtkSpriteInterface()
            : this(true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> class.
        /// </summary>
        /// <param name="startNewThread">If true, a new UI thread will be started and dedicated to drawing; otherwise it is assumed Gtk#
        /// has been started on the current thread and that the interface may use that thread for drawing.</param>
        public GtkSpriteInterface(bool startNewThread)
        {
            lock (drawSync)
            {
                if (!disposed)
                    throw new InvalidOperationException("Only one instance of GtkSpriteInterface may be active at any time.");
                disposed = false;

                // Catch unhandled exceptions on the UI thread and re-throw them.
                if (!exceptionsRaised)
                {
                    GLib.ExceptionManager.UnhandledException += (args) =>
                    {
                        throw (Exception)args.ExceptionObject;
                    };
                    exceptionsRaised = true;
                }
            }

            createPair = CreatePair;
            createAnimatedImage = CreateAnimatedImage;

            dedicatedAppThread = startNewThread;
            if (dedicatedAppThread)
            {
                appThread = new Thread(ApplicationRun) { Name = "GtkSpriteInterface.ApplicationRun" };
                appThread.SetApartmentState(ApartmentState.STA);
                appThread.Start();
            }
            else
            {
                appThread = Thread.CurrentThread;
            }
        }

        /// <summary>
        /// Runs the main application loop.
        /// </summary>
        private void ApplicationRun()
        {
            Application.Init();
            Application.Run();
        }

        /// <summary>
        /// Invokes a method synchronously on the main application thread.
        /// </summary>
        /// <param name="method">The method to invoke. The method should take no parameters and return void.</param>
        private void ApplicationInvoke(System.Action method)
        {
            if (Thread.CurrentThread == appThread)
            {
                method();
            }
            else
            {
                lock (invokeSync)
                {
                    Application.Invoke((o, args) =>
                    {
                        method();
                        lock (invokeSync)
                            Monitor.Pulse(invokeSync);
                    });
                    Monitor.Wait(invokeSync);
                }
            }
        }

        /// <summary>
        /// Creates a new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> from the given file, loading extra
        /// transparency information and adjusting the colors as required by the transparency.
        /// </summary>
        /// <param name="fileName">The path to a static image file from which to create a new
        /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/>.</param>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> created from the given file.
        /// </returns>
        private GtkFrame GtkFrameFromFile(string fileName)
        {
            return Disposable.SetupSafely(new GtkFrame(this, fileName), frame => AlterPixbufForTransparency(fileName, frame.Image.Image));
        }

        /// <summary>
        /// Creates a new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> from the raw buffer, loading extra
        /// transparency information and adjusting the colors as required by the transparency.
        /// </summary>
        /// <param name="buffer">The raw buffer.</param>
        /// <param name="palette">The color palette.</param>
        /// <param name="transparentIndex">The index of the transparent color.</param>
        /// <param name="stride">The stride width of the buffer.</param>
        /// <param name="width">The logical width of the buffer.</param>
        /// <param name="height">The logical height of the buffer.</param>
        /// <param name="depth">The bit depth of the buffer.</param>
        /// <param name="fileName">The path to the GIF file being loaded.</param>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GtkFrame"/> for the frame held in the raw
        /// buffer.</returns>
        private GtkFrame GtkFrameFromBuffer(byte[] buffer, RgbColor[] palette, byte? transparentIndex,
            int stride, int width, int height, byte depth, string fileName)
        {
            if (BufferPreprocess != null)
                BufferPreprocess(ref buffer, ref palette, ref transparentIndex, ref stride, ref width, ref height, ref depth);
            var frame = GtkFrame.FromBuffer(this, buffer, palette, transparentIndex, stride, width, height, depth);
            AlterPixbufForTransparency(fileName, frame.Image.Image);
            return frame;
        }

        /// <summary>
        /// Alters a pixel buffer to account for transparency settings.
        /// </summary>
        /// <param name="fileName">The path to the GIF file from which the image was loaded, in case an alpha color table exists.</param>
        /// <param name="pixbuf">The <see cref="T:Gdk.Pixbuf"/> to be altered.</param>
        private static void AlterPixbufForTransparency(string fileName, Pixbuf pixbuf)
        {
            var mapFilePath = Path.ChangeExtension(fileName, AlphaRemappingTable.FileExtension);
            if (File.Exists(mapFilePath))
            {
                var map = new AlphaRemappingTable();
                map.LoadMap(mapFilePath);

                // Loop over the pixels in each row (to account for stride width of the source).
                IntPtr start = pixbuf.Pixels;
                var scan = new byte[pixbuf.Rowstride];
                for (var row = 0; row < pixbuf.Height; row++)
                {
                    // Copy the scan line into a managed array.
                    var rowPtr = IntPtr.Add(start, row * pixbuf.Rowstride);
                    Marshal.Copy(rowPtr, scan, 0, pixbuf.Rowstride);
                    for (var x = 0; x < pixbuf.Width; x++)
                    {
                        // Map RGB colors to ARGB colors.
                        var offset = 4 * x;
                        if (map.TryGetMapping(new RgbColor(scan[offset + 0], scan[offset + 1], scan[offset + 2]), out ArgbColor argbColor))
                        {
                            scan[offset + 0] = argbColor.R;
                            scan[offset + 1] = argbColor.G;
                            scan[offset + 2] = argbColor.B;
                            scan[offset + 3] = argbColor.A;
                        }
                    }
                    // Copy the altered array back into the source.
                    Marshal.Copy(scan, 0, rowPtr, pixbuf.Rowstride);
                }
            }
        }

        /// <summary>
        /// Loads the given collection of file paths as images in a format that this
        /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> can display.
        /// </summary>
        /// <param name="imagePaths">The collection of paths to image files that should be loaded by the interface. Any images not
        /// loaded by this method will be loaded on demand.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="imagePaths"/> is null.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void LoadImages(IEnumerable<SpriteImagePaths> imagePaths)
        {
            LoadImages(imagePaths, null);
        }

        /// <summary>
        /// Loads the given collection of file paths as images in a format that this
        /// <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> can display.
        /// </summary>
        /// <param name="imagePaths">The collection of paths to image files that should be loaded by the interface. Any images not
        /// loaded by this method will be loaded on demand.</param>
        /// <param name="imageLoadedHandler">An <see cref="T:System.EventHandler"/> that is raised when an image is loaded.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="imagePaths"/> is null.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void LoadImages(IEnumerable<SpriteImagePaths> imagePaths, EventHandler imageLoadedHandler)
        {
            Argument.EnsureNotNull(imagePaths, nameof(imagePaths));
            EnsureNotDisposed();

            foreach (var paths in imagePaths)
                LoadPaths(paths, imageLoadedHandler);
        }

        /// <summary>
        /// Ensures an animation pair exists for the given paths.
        /// </summary>
        /// <param name="paths">A pair of paths for which an animation pair should be created, if one does not yet exists.</param>
        /// <param name="imageLoadedHandler">An event handler to raise unconditionally at the end of the method.</param>
        private void LoadPaths(SpriteImagePaths paths, EventHandler imageLoadedHandler)
        {
            if (!animationPairsByPaths.ContainsKey(paths))
                animationPairsByPaths.Add(paths, CreatePair(paths));
            imageLoadedHandler.Raise(this);
        }

        /// <summary>
        /// Creates an animation pair for a specified pair of paths.
        /// </summary>
        /// <param name="paths">The paths for which an animation pair should be generated.</param>
        /// <returns>An animation pair for displaying the specified paths.</returns>
        private AnimationPair<GtkFrame> CreatePair(SpriteImagePaths paths)
        {
            var leftImage = images.GetOrAdd(paths.Left, createAnimatedImage);
            var leftAnimation = new Animation<GtkFrame>(leftImage);

            if (SpriteImagePaths.Comparer.Equals(paths.Left, paths.Right))
                return new AnimationPair<GtkFrame>(leftAnimation, leftAnimation);

            var rightImage = images.GetOrAdd(paths.Right, createAnimatedImage);
            var rightAnimation = new Animation<GtkFrame>(rightImage);

            return new AnimationPair<GtkFrame>(leftAnimation, rightAnimation);
        }

        /// <summary>
        /// Creates an animated image by loading it from file.
        /// </summary>
        /// <param name="path">The path to the file that should be loaded.</param>
        /// <returns>A new animated image created from the specified file.</returns>
        private AnimatedImage<GtkFrame> CreateAnimatedImage(string path)
        {
            return new AnimatedImage<GtkFrame>(path, GtkFrameFromFile,
                (b, p, tI, s, w, h, d) => GtkFrameFromBuffer(b, p, tI, s, w, h, d, path), GtkFrame.AllowableBitDepths);
        }

        /// <summary>
        /// Creates an <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> specific context menu for the given set of menu
        /// items.
        /// </summary>
        /// <param name="menuItems">The collections of items to be displayed in the menu.</param>
        /// <returns>An <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> specific context menu.</returns>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="menuItems"/> is null.</exception>
        public ISimpleContextMenu CreateContextMenu(IEnumerable<ISimpleContextMenuItem> menuItems)
        {
            EnsureNotDisposed();

            GtkContextMenu menu = null;
            ApplicationInvoke(() => menu = new GtkContextMenu(this, menuItems, true));
            contextMenus.AddLast(menu);
            return menu;
        }

        /// <summary>
        /// Opens the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/>.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void Open()
        {
            EnsureNotDisposed();
        }

        /// <summary>
        /// Hides the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/>.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void Hide()
        {
            EnsureNotDisposed();
            ApplicationInvoke(() =>
            {
                foreach (GraphicsWindow window in spriteWindows.Values)
                    window.Hide();
            });
        }

        /// <summary>
        /// Shows the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/>.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void Show()
        {
            EnsureNotDisposed();
            ApplicationInvoke(() =>
            {
                foreach (GraphicsWindow window in spriteWindows.Values)
                    window.Show();
            });
        }

        /// <summary>
        /// Freezes the display of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/>.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void Pause()
        {
            EnsureNotDisposed();
            paused = true;
        }

        /// <summary>
        /// Resumes display of the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> from a paused state.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void Resume()
        {
            Show();
            paused = false;
        }

        /// <summary>
        /// Creates a new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/> for the given sprite with the
        /// current window settings, attaches the appropriate event handlers and realizes the window.
        /// </summary>
        /// <param name="sprite">The <see cref="T:DesktopSprites.SpriteManagement.ISprite"/> that should be drawn by this window.</param>
        /// <returns>The new <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface.GraphicsWindow"/>.</returns>
        private GraphicsWindow CreateWindow(ISprite sprite)
        {
            GraphicsWindow window = null;
            ApplicationInvoke(() =>
            {
                window = Disposable.SetupSafely(new GraphicsWindow(), win =>
                {
                    win.Sprite = sprite;
                    win.Title = windowTitle;
                    win.Icon = windowIcon;
                    win.KeepAbove = windowTopmost;
                    win.ButtonPressEvent += GraphicsWindow_ButtonPressEvent;
                    win.ButtonReleaseEvent += GraphicsWindow_ButtonReleaseEvent;
                    win.KeyPressEvent += GraphicsWindow_KeyPressEvent;
                    win.FocusInEvent += (sender, e) => Focused.Raise(this);
                    win.FocusOutEvent += (sender, e) => Unfocused.Raise(this);
                    win.Realize();
                });
            });
            return window;
        }

        /// <summary>
        /// Draws the given collection of sprites.
        /// </summary>
        /// <param name="sprites">The collection of sprites to draw.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="sprites"/> is null.</exception>
        /// <exception cref="T:System.ObjectDisposedException">The interface has been disposed.</exception>
        public void Draw(ICollection<ISprite> sprites)
        {
            Argument.EnsureNotNull(sprites, nameof(sprites));
            EnsureNotDisposed();

            if (paused)
                return;

            lock (drawSync)
            {
                // Remove all back references from windows to sprites. Any that are not restored indicate removed sprites.
                foreach (GraphicsWindow window in spriteWindows.Values)
                    window.Sprite = null;

                // Link sprites to windows, creating new windows as needed for new sprites.
                drawOrderedWindows.Clear();
                foreach (ISprite sprite in sprites)
                {
                    // Create a new window for a new sprite.
                    if (!spriteWindows.ContainsKey(sprite))
                        spriteWindows[sprite] = CreateWindow(sprite);

                    // Save the windows position in the draw order.
                    drawOrderedWindows.Add(spriteWindows[sprite]);

                    // Create a back reference to the sprite the window is responsible for, which indicates it is in use.
                    spriteWindows[sprite].Sprite = sprite;
                }

                /*
                // Set the stacking order of the windows.
                foreach (GraphicsWindow window in drawOrderedWindows)
                    if (window.Visible)
                        window.GdkWindow.Raise();
                    else
                        window.Show();
                */
                // FIXME: Refreshing the whole stacking order is highly draining and leads to flickering.
                // Implement a LinkedList based minimum moves restack.
                foreach (GraphicsWindow window in drawOrderedWindows)
                    ApplicationInvoke(() =>
                    {
                        if (!window.Visible)
                            window.Show();
                    });

                // Remove windows whose sprites have been removed (and thus were not re-linked).
                foreach (var kvp in spriteWindows)
                    if (kvp.Value.Sprite == null)
                    {
                        removedSprites.Add(kvp.Key);
                        ApplicationInvoke(() => kvp.Value.Destroy());
                    }
                foreach (ISprite sprite in removedSprites)
                    spriteWindows.Remove(sprite);
                removedSprites.Clear();

                // Draw each sprite in the collection to its own window, in the correct order.
                foreach (GraphicsWindow loopWindow in drawOrderedWindows)
                {
                    // C# 4 behavior. Using a loop variable in an anonymous expression will capture the final value, and not the current
                    // iteration. To do that a local copy must be made. This is fixed in C# 5.
                    GraphicsWindow window = loopWindow;

                    ISprite sprite = window.Sprite;

                    // Gtk# operations need to be invoked on the main thread. Although they will usually succeed, eventually an invalid
                    // unmanaged memory access is likely to result.
                    // By invoking within the loop, the actions are chunked up so that the message pump doesn't become tied down for too
                    // long, which allows it to continue to respond to other messages in a timely manner.
                    ApplicationInvoke(() =>
                    {
                        var imagePath = sprite.FacingRight ? sprite.ImagePaths.Right : sprite.ImagePaths.Left;
                        if (imagePath != null)
                        {
                            var pair = animationPairsByPaths.GetOrAdd(sprite.ImagePaths, createPair);
                            var animation = sprite.FacingRight ? pair.Right : pair.Left;
                            var frame = animation.Image[sprite.ImageTimeIndex, sprite.PreventAnimationLoop];
                            if (frame != null)
                                window.CurrentImage = frame.Image;
                            else
                                window.CurrentImage = null;
                        }

                        // The window takes on the location and size of the sprite to draw.
                        window.GdkWindow.MoveResize(sprite.Region.X, sprite.Region.Y, sprite.Region.Width, sprite.Region.Height);

                        // Apply the image now the window is set up, by updating the clipping region and drawing it.
                        window.SetClip(sprite.Region.Width, sprite.Region.Height);
                        window.DrawFrame(sprite.Region.Width, sprite.Region.Height);

                        // Display any speech.
                        if (sprite is ISpeakingSprite speakingSprite && speakingSprite.SpeechText != null)
                            window.ShowSpeech(speakingSprite.SpeechText, sprite.Region.X + sprite.Region.Width / 2, sprite.Region.Y - 2);
                        else
                            window.HideSpeech();
                    });
                }
            }
        }

        /// <summary>
        /// Checks the current instance has not been disposed, otherwise throws an <see cref="T:System.ObjectDisposedException"/>.
        /// </summary>
        /// <exception cref="T:System.ObjectDisposedException">The current instance has been disposed.</exception>
        private void EnsureNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        /// Closes the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/>.
        /// </summary>
        public void Close()
        {
            Dispose();
        }

        /// <summary>
        /// Releases all resources used by the <see cref="T:DesktopSprites.SpriteManagement.GtkSpriteInterface"/> object.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                lock (drawSync)
                {
                    ApplicationInvoke(() =>
                    {
                        foreach (GraphicsWindow window in spriteWindows.Values)
                            window.Hide();
                        foreach (GraphicsWindow window in spriteWindows.Values)
                            window.Destroy();
                        foreach (GtkContextMenu menu in contextMenus)
                            menu.Destroy();
                    });

                    if (dedicatedAppThread)
                        Application.Quit();
                    spriteWindows.Clear();

                    if (windowIcon != null)
                        windowIcon.Dispose();

                    foreach (AnimatedImage<GtkFrame> image in images.Values)
                        image.Dispose();
                }
                InterfaceClosed.Raise(this);
            }
        }
    }
}
