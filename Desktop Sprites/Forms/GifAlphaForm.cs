﻿namespace DesktopSprites.Forms
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Windows.Forms;
    using DesktopSprites.Core;
    using DesktopSprites.SpriteManagement;

    /// <summary>
    /// Allows an ARGB color table to be defined for GIF files.
    /// </summary>
    public partial class GifAlphaForm : Form
    {
        /// <summary>
        /// Location of directory from which to load GIF files.
        /// </summary>
        private readonly string filesPath;
        /// <summary>
        /// The path to the GIF image being modified.
        /// </summary>
        private string filePath;
        /// <summary>
        /// The GIF image to display and modify.
        /// </summary>
        private GifImage<Bitmap> gifImage;
        /// <summary>
        /// The frame index currently being displayed.
        /// </summary>
        private int frameIndex;
        /// <summary>
        /// Maintains the mapping table between source RGB colors and desired ARGB colors.
        /// </summary>
        private Dictionary<Color, Color> colorMappingTable = new Dictionary<Color, Color>();
        /// <summary>
        /// Color swatches that display the original palette.
        /// </summary>
        private List<Panel> sourceSwatches = new List<Panel>(0);
        /// <summary>
        /// Color swatches that display the modified palette.
        /// </summary>
        private List<Panel> desiredSwatches = new List<Panel>(0);
        /// <summary>
        /// The frames of <see cref="F:DesktopSprites.DesktopPonies.GifAlphaForm.gifImage"/>, altered to use the modified palette.
        /// </summary>
        private Bitmap[] desiredFrames;
        /// <summary>
        /// Indicates if an image is currently loaded and being displayed.
        /// </summary>
        private bool loaded;
        /// <summary>
        /// Indicates if a change has been made, and thus saving is required.
        /// </summary>
        private bool changed;
        /// <summary>
        /// The current color in the source image that is currently being edited or otherwise modified.
        /// </summary>
        private Color currentColor;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.Forms.GifAlphaForm"/> class.
        /// </summary>
        /// <param name="path">The path to a directory from which GIF files should be loaded.</param>
        public GifAlphaForm(string path)
        {
            InitializeComponent();
            filesPath = Argument.EnsureNotNull(path, nameof(path));

            Disposed += (sender, e) =>
            {
                if (gifImage != null)
                    foreach (GifFrame<Bitmap> frame in gifImage.Frames)
                        frame.Image.Dispose();
                if (desiredFrames != null)
                    foreach (Bitmap frame in desiredFrames)
                        frame.Dispose();
            };
        }

        /// <summary>
        /// Raised when the form has loaded.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void GifAlphaForm_Load(object sender, EventArgs e)
        {
            BeginInvoke(new MethodInvoker(LoadInternal));
        }

        /// <summary>
        /// Gets the collection of GIF files to be accessed.
        /// </summary>
        private void LoadInternal()
        {
            ImageSelector.Items.AddRange(
                Directory.GetFiles(filesPath, "*.gif", SearchOption.AllDirectories)
                .Select(path => path.Substring(filesPath.Length + 1))
                .ToArray());
            if (ImageSelector.Items.Count != 0)
                ImageSelector.SelectedIndex = 0;
            else
                MessageBox.Show(this,
                    "No .gif files found in {0} or its subdirectories.".FormatWith(filesPath),
                    "No Files", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Raised when a new index is selected from ImageSelector.
        /// Loads the GIF file of that filename.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void ImageSelector_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!SavePrompt())
                return;

            var wasPlaying = Indexer.Playing;
            Indexer.Playing = false;

            loaded = false;
            changed = false;
            filePath = Path.Combine(filesPath, (string)ImageSelector.Items[ImageSelector.SelectedIndex]);

            if (gifImage != null)
                foreach (GifFrame<Bitmap> frame in gifImage.Frames)
                    frame.Image.Dispose();

            if (desiredFrames != null)
                foreach (Bitmap frame in desiredFrames)
                    frame.Dispose();

            gifImage = null;
            frameIndex = -1;
            desiredFrames = null;
            ImageComparison.Panel1.BackgroundImage = null;
            ImageComparison.Panel2.BackgroundImage = null;
            ImageSourceColor.BackColor = Color.Transparent;
            ImageDesiredColor.BackColor = Color.Transparent;
            ImageSourcePalette.BackColor = PaletteControls.BackColor;
            ImageDesiredPalette.BackColor = PaletteControls.BackColor;
            ImageSourcePalette.Controls.Clear();
            ImageDesiredPalette.Controls.Clear();
            foreach (Panel panel in sourceSwatches)
                panel.Dispose();
            foreach (Panel panel in desiredSwatches)
                panel.Dispose();
            sourceSwatches.Clear();
            desiredSwatches.Clear();
            SourceAlphaCode.ResetText();
            SourceColorCode.ResetText();
            DesiredAlphaCode.ResetText();
            DesiredColorCode.ResetText();
            FrameControls.Enabled = false;
            PaletteControls.Enabled = false;
            ColorControls.Enabled = false;
            ErrorLabel.Visible = false;
            currentColor = Color.Empty;

            FileStream gifStream = null;
            try
            {
                gifStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                gifImage = GifImage.OfBitmap(gifStream);
            }
            catch (Exception)
            {
                Console.WriteLine("An error occurred attempting to load the file: " + filePath);
                ShowError("An error occurred attempting to load this file.");
                return;
            }
            finally
            {
                if (gifStream != null)
                    gifStream.Dispose();
            }
            Indexer.UseTimingsFrom(gifImage);

            var map = new AlphaRemappingTable();
            var mapFile = Path.ChangeExtension(filePath, AlphaRemappingTable.FileExtension);
            if (File.Exists(mapFile))
                map.LoadMap(mapFile);

            colorMappingTable.Clear();

            foreach (var frame in gifImage.Frames)
                BuildColorMap(frame.GetColorTable(), map);

            sourceSwatches.Capacity = colorMappingTable.Count;
            desiredSwatches.Capacity = colorMappingTable.Count;

            var swatchSize = ImageSourcePalette.Height - 2;
            var size = new Size(swatchSize, swatchSize);
            var location = new Point(1, 1);

            ImageSourcePalette.SuspendLayout();
            ImageDesiredPalette.SuspendLayout();

            var mappingIndex = 0;
            foreach (var colorMapping in colorMappingTable)
            {
                var sourcePanel = new Panel() { Size = size, Location = location };
                var desiredPanel = new Panel() { Size = size, Location = location };
                sourcePanel.Tag = desiredPanel;
                desiredPanel.Tag = sourcePanel;
                sourceSwatches.Add(sourcePanel);
                desiredSwatches.Add(desiredPanel);
                ImageSourcePalette.Controls.Add(sourcePanel);
                ImageDesiredPalette.Controls.Add(desiredPanel);
                sourceSwatches[mappingIndex].Click += Swatch_Clicked;
                desiredSwatches[mappingIndex].Click += Swatch_Clicked;
                sourceSwatches[mappingIndex].BackColor = colorMapping.Key;
                desiredSwatches[mappingIndex].BackColor = colorMapping.Value;
                if (mappingIndex == 0)
                    currentColor = colorMapping.Key;
                mappingIndex++;
                location.X += swatchSize + 1;
            }
            location.Y = 0;
            var blankSourcePanel =
                new Panel() { Size = ImageSourcePalette.Size, Location = location, BackColor = SystemColors.Control };
            ImageSourcePalette.Controls.Add(blankSourcePanel);
            var blankDesiredPanel =
                new Panel() { Size = ImageDesiredPalette.Size, Location = location, BackColor = SystemColors.Control };
            ImageDesiredPalette.Controls.Add(blankDesiredPanel);
            ImageSourcePalette.ResumeLayout();
            ImageDesiredPalette.ResumeLayout();
            ImageSourcePalette.BackColor = ImageComparison.BackColor;
            ImageDesiredPalette.BackColor = ImageComparison.BackColor;

            desiredFrames = new Bitmap[gifImage.Frames.Length];
            for (var i = 0; i < desiredFrames.Length; i++)
                desiredFrames[i] = (Bitmap)gifImage.Frames[i].Image.Clone();
            UpdateDesiredFrames();

            FrameControls.Enabled = true;
            PaletteControls.Enabled = true;
            ColorControls.Enabled = true;
            SaveCommand.Enabled = true;

            UpdateSelectedFrame(0);
            UpdateColorHex();
            UpdateColorDisplay();

            if (wasPlaying)
                Indexer.Playing = true;

            loaded = true;
        }

        /// <summary>
        /// Builds the color mapping by adding all colors in the given table, and also resolving lookups according to the given alpha map.
        /// </summary>
        /// <param name="colorTable">The colors that should be added to the current lookup mapping.</param>
        /// <param name="alphaMap">The alpha mapping that specifies new ARGB colors that should replace any given RGB colors in the color
        /// table.</param>
        private void BuildColorMap(ArgbColor[] colorTable, AlphaRemappingTable alphaMap)
        {
            foreach (ArgbColor sourceArgbColor in colorTable)
                if (sourceArgbColor.A == 255)
                {
                    var sourceRgbColor = (RgbColor)sourceArgbColor;
                    var sourceColor = Color.FromArgb(sourceRgbColor.ToArgb());
                    if (!colorMappingTable.ContainsKey(sourceColor))
                    {
                        if (!alphaMap.TryGetMapping(sourceRgbColor, out ArgbColor desiredArgbColor))
                            desiredArgbColor = sourceArgbColor;
                        colorMappingTable.Add(sourceColor, Color.FromArgb(desiredArgbColor.ToArgb()));
                    }
                }
        }

        /// <summary>
        /// Displays an error message to the user about why the current image cannot be displayed.
        /// </summary>
        /// <param name="error">The message to display.</param>
        private void ShowError(string error)
        {
            ErrorLabel.Text = error;
            ErrorLabel.Visible = true;
        }

        /// <summary>
        /// Raised when the image index changes.
        /// </summary>
        /// <param name="sender">The source of the event</param>
        /// <param name="e">Data about the event.</param>
        private void Indexer_IndexChanged(object sender, EventArgs e)
        {
            UpdateSelectedFrame(Indexer.FrameIndex);
        }

        /// <summary>
        /// Updates the display to a new frame.
        /// </summary>
        /// <param name="newFrameIndex">The index of the frame that should be displayed.</param>]
        private void UpdateSelectedFrame(int newFrameIndex)
        {
            if (frameIndex != newFrameIndex)
            {
                frameIndex = newFrameIndex;
                var sourceImage = gifImage.Frames[newFrameIndex].Image;
                var desiredImage = desiredFrames[newFrameIndex];
                ImageComparison.Panel1.BackgroundImage = sourceImage;
                ImageComparison.Panel2.BackgroundImage = desiredImage;
                const int Padding = 8;
                ImageComparison.Size = new Size(
                    sourceImage.Width + desiredImage.Width + ImageComparison.SplitterWidth + 2 * Padding,
                    Math.Max(sourceImage.Height, desiredImage.Height) + 2 * Padding);
                ImageComparison.SplitterDistance = sourceImage.Width + Padding;
                ImageComparison.Left = ImagePanel.Width / 2 - ImageComparison.Width / 2 + Padding;
                ImageComparison.Panel1.Invalidate();
                ImageComparison.Panel2.Invalidate();
            }
        }

        /// <summary>
        /// Raised when a color swatch is clicked.
        /// Displays hex values for color.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void Swatch_Clicked(object sender, EventArgs e)
        {
            var panel = (Panel)sender;
            if (panel.Parent == ImageSourcePalette)
                currentColor = panel.BackColor;
            else
                currentColor = ((Panel)panel.Tag).BackColor;
            UpdateColorHex();
            UpdateColorDisplay();
        }

        /// <summary>
        /// Raised when ImageDesiredColor is clicked.
        /// Allows the user to edit the desired color, for the color currently being edited.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void ImageDesiredColor_Click(object sender, EventArgs e)
        {
            changed = true;
            ColorDialog.Color = colorMappingTable[currentColor];
            ColorDialog.ShowDialog(this);
            colorMappingTable[currentColor] = Color.FromArgb(colorMappingTable[currentColor].A, ColorDialog.Color);
            UpdateColorHex();
            UpdateColorDisplay();
        }

        /// <summary>
        /// Updates the controls that display the hexadecimal codes for the color being edited.
        /// </summary>
        private void UpdateColorHex()
        {
            SourceAlphaCode.Text = "{0:X2}".FormatWith(currentColor.A);
            SourceColorCode.Text = "{0:X6}".FormatWith(currentColor.ToArgb() & 0x00FFFFFF);
            DesiredAlphaCode.Text = "{0:X2}".FormatWith(colorMappingTable[currentColor].A);
            DesiredColorCode.Text = "{0:X6}".FormatWith(colorMappingTable[currentColor].ToArgb() & 0x00FFFFFF);
        }

        /// <summary>
        /// Updates the controls that display the color being edited.
        /// </summary>
        private void UpdateColorDisplay()
        {
            ImageSourceColor.BackColor = currentColor;
            ImageDesiredColor.BackColor = colorMappingTable[currentColor];
        }

        /// <summary>
        /// Raised when the text of DesiredAlphaCode is changed.
        /// Attempts to parse the new alpha code and modifies the image accordingly.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void DesiredAlphaCode_TextChanged(object sender, EventArgs e)
        {
            if (!loaded)
                return;

            if (byte.TryParse(DesiredAlphaCode.Text, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out var value))
            {
                DesiredAlphaCode.ForeColor = Color.Black;
                var newColor = Color.FromArgb(value, colorMappingTable[currentColor]);
                if (colorMappingTable[currentColor] != newColor)
                {
                    colorMappingTable[currentColor] = newColor;
                    changed = true;
                }
                foreach (Panel sourcePanel in ImageSourcePalette.Controls)
                    if (sourcePanel.BackColor == currentColor && sourcePanel.Tag != null)
                    {
                        ((Panel)sourcePanel.Tag).BackColor = colorMappingTable[currentColor];
                        break;
                    }
                UpdateColorDisplay();
                UpdateDesiredFrames();
                ImageComparison.Panel2.Invalidate();
            }
            else
            {
                DesiredAlphaCode.ForeColor = Color.DarkRed;
            }
        }

        /// <summary>
        /// Raised when the text of DesiredColorCode is changed.
        /// Attempts to parse the new color code and modifies the image accordingly.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void DesiredColorCode_TextChanged(object sender, EventArgs e)
        {
            if (!loaded)
                return;

            if (int.TryParse(DesiredColorCode.Text, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out var value))
            {
                DesiredColorCode.ForeColor = Color.Black;
                var newColor = Color.FromArgb(colorMappingTable[currentColor].A, value >> 16, value >> 8 & 0xFF, value & 0xFF);
                if (colorMappingTable[currentColor] != newColor)
                {
                    colorMappingTable[currentColor] = newColor;
                    changed = true;
                }
                foreach (Panel sourcePanel in ImageSourcePalette.Controls)
                    if (sourcePanel.BackColor == currentColor && sourcePanel.Tag != null)
                    {
                        ((Panel)sourcePanel.Tag).BackColor = colorMappingTable[currentColor];
                        break;
                    }
                UpdateColorDisplay();
                UpdateDesiredFrames();
                ImageComparison.Panel2.Invalidate();
            }
            else
            {
                DesiredColorCode.ForeColor = Color.DarkRed;
            }
        }

        /// <summary>
        /// Updates all the modified images to use the desired palette.
        /// </summary>
        private void UpdateDesiredFrames()
        {
            for (var frame = 0; frame < gifImage.Frames.Length; frame++)
            {
                var tableSize = gifImage.Frames[frame].ColorTableSize;
                ColorPalette palette = gifImage.Frames[frame].Image.Palette;
                for (var i = 0; i < tableSize; i++)
                    if (palette.Entries[i].A == 255)
                        palette.Entries[i] = colorMappingTable[palette.Entries[i]];
                desiredFrames[frame].Palette = palette;
                desiredFrames[frame].PremultiplyAlpha();
            }
        }

        /// <summary>
        /// Raised when ResetCommand_Click is clicked.
        /// Clears the current mapping.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void ResetCommand_Click(object sender, EventArgs e)
        {
            var nonEmptyMap = false;
            foreach (var colorMapping in colorMappingTable)
                if (colorMapping.Key != colorMapping.Value)
                {
                    nonEmptyMap = true;
                    break;
                }
            if (nonEmptyMap &&
                MessageBox.Show(this,
                    "Are you sure you want to reset the mapping? You can still decline to save later.", "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                var keys = new Color[colorMappingTable.Keys.Count];
                colorMappingTable.Keys.CopyTo(keys, 0);
                foreach (Color color in keys)
                    colorMappingTable[color] = Color.FromArgb(255, color);
                changed = true;
            }
        }

        /// <summary>
        /// Raised when SaveCommand is clicked.
        /// Saves the mapping to file.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void SaveCommand_Click(object sender, EventArgs e)
        {
            SaveMapping();
        }

        /// <summary>
        /// Saves the mapping of the original RGB colors to modified ARGB colors to file.
        /// </summary>
        private void SaveMapping()
        {
            var map = new AlphaRemappingTable();
            foreach (var colorMapping in colorMappingTable)
                if (colorMapping.Key != colorMapping.Value)
                    map.AddMapping(
                        new RgbColor(colorMapping.Key.R, colorMapping.Key.G, colorMapping.Key.B),
                        new ArgbColor(colorMapping.Value.A, colorMapping.Value.R, colorMapping.Value.G, colorMapping.Value.B));

            var mapFilePath = Path.ChangeExtension(filePath, AlphaRemappingTable.FileExtension);
            if (map.SaveMap(mapFilePath))
                MessageBox.Show(this, "Lookup mapping saved to '" + mapFilePath + "'",
                    "Mapping Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "Source and destination colors match. Mapping file '" + mapFilePath + "' deleted.",
                    "Mapping Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information);
            changed = false;
        }

        /// <summary>
        /// Prompts the user to save any outstanding changes, if required, and returns a value indicating whether the caller can continue.
        /// </summary>
        /// <returns>A value indicating whether the caller should continue. Returns true to indicate it is OK to proceed, returns false to
        /// indicate the user wishes to review the current changes.</returns>
        private bool SavePrompt()
        {
            if (changed)
            {
                DialogResult result = MessageBox.Show(this, "You have unsaved changes. Save now?", "Unsaved Changes",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1);
                if (result == DialogResult.Yes)
                {
                    SaveMapping();
                    return true;
                }
                else if (result == DialogResult.No)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Raised when BackgroundColorCommand is clicked.
        /// Allows the user to change the background color that the images and swatches are displayed upon.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void BackgroundColorCommand_Click(object sender, EventArgs e)
        {
            ColorDialog.Color = ImageComparison.BackColor;
            ColorDialog.ShowDialog(this);
            ImageComparison.BackColor = ColorDialog.Color;
            ImageColors.BackColor = ColorDialog.Color;
            ImageSourcePalette.BackColor = ColorDialog.Color;
            ImageDesiredPalette.BackColor = ColorDialog.Color;
        }

        /// <summary>
        /// Raised when either panel on ImageComparison is clicked.
        /// Picks the color under the cursor for editing.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void ImageComparison_Panel_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            var panel = (Panel)sender;

            var imageWidth = gifImage.Frames[Indexer.FrameIndex].Image.Width;
            var imageHeight = gifImage.Frames[Indexer.FrameIndex].Image.Height;
            Point location = e.Location;
            location -= new Size(panel.Width / 2, panel.Height / 2);
            location += new Size(imageWidth / 2, imageHeight / 2);

            if (location.X >= 0 && location.Y >= 0 && location.X < imageWidth && location.Y < imageHeight)
            {
                IEnumerable<Color> colors;
                if (sender == ImageComparison.Panel1)
                    colors = colorMappingTable.Keys;
                else
                    colors = colorMappingTable.Values;

                Color pixel = gifImage.Frames[Indexer.FrameIndex].Image.GetPixel(location.X, location.Y);
                foreach (Color color in colors)
                    // GetPixel always returns a color with binary alpha. This comparison relaxes the alpha comparison to work around this,
                    // but can lead to incorrect picks when two desired colors have the same RGB values but different alpha.
                    if (pixel.A == 255 && color.R == pixel.R && color.G == pixel.G && color.B == pixel.B)
                    {
                        currentColor = pixel;
                        UpdateColorHex();
                        UpdateColorDisplay();
                        break;
                    }
            }
        }

        /// <summary>
        /// Raised when either image palette is resized.
        /// Resizes the last panel to cover unused area which would otherwise contain swatches.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void ImagePalette_Resize(object sender, EventArgs e)
        {
            var panel = (Panel)sender;
            if (panel.Controls.Count != 0)
                panel.Controls[panel.Controls.Count - 1].Width = panel.Width;
        }

        /// <summary>
        /// Raised when the form is closing.
        /// Checks if a save is required.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void GifAlphaForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !SavePrompt();
        }
    }
}
