﻿namespace DesktopSprites.Forms
{
    using System;
    using System.ComponentModel;
    using System.Drawing;
    using System.Windows.Forms;
    using DesktopSprites.Collections;
    using DesktopSprites.Core;
    using DesktopSprites.SpriteManagement;

    /// <summary>
    /// Provides a control that allows the user to seek among the frames of an animated image.
    /// </summary>
    public partial class AnimatedImageIndexer : UserControl
    {
        /// <summary>
        /// Indicates whether the index is being changed.
        /// </summary>
        private bool updating;
        /// <summary>
        /// The durations of each frame.
        /// </summary>
        private int[] durations;
        /// <summary>
        /// A list of timings in the animation that mark the start and end of frames over the duration of the animation.
        /// </summary>
        private int[] sectionValues;
        /// <summary>
        /// Brushes used to draw sections for each frame.
        /// </summary>
        private static readonly ImmutableArray<Brush> SectionBrushes =
            new Brush[] { Brushes.DarkGray, Brushes.LightGray }.ToImmutableArray();
        /// <summary>
        /// Brush used to draw the section of the currently selected frame.
        /// </summary>
        private Brush sectionHighlightBrush = Brushes.Red;

        /// <summary>
        /// Occurs when the frame or time index has been changed.
        /// </summary>
        [Description("Occurs when the frame or time index has been changed.")]
        public event EventHandler IndexChanged;

        /// <summary>
        /// Gets or sets the frame index.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int FrameIndex
        {
            get
            {
                return durations == null ? -1 : FrameSelector.Value;
            }
            set
            {
                if (updating)
                    return;

                if (durations == null)
                    throw new InvalidOperationException("Cannot set the frame index until indexes have been defined.");

                Argument.EnsureInRangeInclusive(value, nameof(value), FrameSelector.Minimum, FrameSelector.Maximum);

                updating = true;
                FrameSelector.Value = value;

                var timeIndex = 0;
                for (var i = 0; i < value; i++)
                    timeIndex += durations[i];
                timeIndex += durations[value] / 2;
                TimeSelector.Value = timeIndex;

                OnIndexChanged();
                updating = false;
            }
        }

        /// <summary>
        /// Gets or sets the time index, in milliseconds.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TimeIndex
        {
            get
            {
                return durations == null ? -1 : TimeSelector.Value;
            }
            set
            {
                if (updating)
                    return;

                if (durations == null)
                    throw new InvalidOperationException("Cannot set the frame index until indexes have been defined.");

                Argument.EnsureInRangeInclusive(value, nameof(value), TimeSelector.Minimum, TimeSelector.Maximum);

                updating = true;
                TimeSelector.Value = value;

                var seekTime = durations[0];
                var frameIndex = 0;
                while (seekTime <= value && frameIndex < FrameSelector.Maximum)
                    seekTime += durations[++frameIndex];
                FrameSelector.Value = frameIndex;

                OnIndexChanged();
                updating = false;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether automatic playback is taking place.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Playing
        {
            get
            {
                return PlaybackTimer.Enabled;
            }
            set
            {
                PlaybackTimer.Enabled = value;
                FrameSelector.Enabled = !value;
                TimeSelector.Enabled = !value;
                NextCommand.Enabled = !value;
                PreviousCommand.Enabled = !value;
                PlayCommand.Text = value ? "Pause" : "Play";
            }
        }

        /// <summary>
        /// Gets or sets the interval, in milliseconds, at which the time index is advanced when playback is active.
        /// </summary>
        [Description("The interval, in milliseconds, at which the time index is advanced when playback is active.")]
        [DefaultValue(50)]
        public int Step
        {
            get { return PlaybackTimer.Interval; }
            set { PlaybackTimer.Interval = value; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.Forms.AnimatedImageIndexer"/> class.
        /// </summary>
        public AnimatedImageIndexer()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Uses durations from the specified image to set up the control for indexing. This will stop playback.
        /// </summary>
        /// <typeparam name="T">The type of the frame images.</typeparam>
        /// <param name="image">The image whose durations should be used to specify frame and time indexes.</param>
        public void UseTimingsFrom<T>(GifImage<T> image)
        {
            Playing = false;
            if (image == null)
            {
                durations = null;
                sectionValues = null;
                FrameSelector.Maximum = 0;
                TimeSelector.Maximum = 0;
                FrameLabel.Text = "";
                Enabled = false;
            }
            else
            {
                durations = new int[image.Frames.Length];
                sectionValues = new int[image.Frames.Length + 1];
                sectionValues[0] = 0;
                var accumulatedDuration = 0;
                for (var i = 0; i < image.Frames.Length; i++)
                {
                    var duration = image.Frames[i].Duration;
                    durations[i] = duration;
                    accumulatedDuration += duration;
                    sectionValues[i + 1] = accumulatedDuration;
                }
                FrameSelector.Maximum = image.Frames.Length - 1;
                TimeSelector.Maximum = image.Duration;
                UpdateLabel();
                Enabled = true;
            }
            TimeSelectorSections.Invalidate();
        }

        /// <summary>
        /// Updates the index summary label text.
        /// </summary>
        private void UpdateLabel()
        {
            FrameLabel.Text =
                "Frame: {0:00} of {1:00}  Time: {2:00.00} of {3:00.00} seconds".FormatWith(
                FrameIndex + 1, FrameSelector.Maximum + 1, TimeIndex / 1000f, TimeSelector.Maximum / 1000f);
        }

        /// <summary>
        /// Raised when a frame is selected by the user.
        /// Updates the frame index.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Data about the event.</param>
        private void FrameSelector_ValueChanged(object sender, EventArgs e)
        {
            FrameIndex = FrameSelector.Value;
        }

        /// <summary>
        /// Raised when a time is selected by the user.
        /// Updates the time index.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">Data about the event.</param>
        private void TimeSelector_ValueChanged(object sender, EventArgs e)
        {
            TimeIndex = TimeSelector.Value;
        }

        /// <summary>
        /// Raised when PreviousCommand is clicked.
        /// Moves back one frame.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void PreviousCommand_Click(object sender, EventArgs e)
        {
            var value = FrameIndex;
            if (--value < FrameSelector.Minimum)
                value = FrameSelector.Maximum;
            FrameIndex = value;
        }

        /// <summary>
        /// Raised when NextCommand is clicked.
        /// Moves forward one frame.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void NextCommand_Click(object sender, EventArgs e)
        {
            var value = FrameIndex;
            if (++value > FrameSelector.Maximum)
                value = FrameSelector.Minimum;
            FrameIndex = value;
        }

        /// <summary>
        /// Raised when PlayCommand is clicked.
        /// Toggles playback of the animation.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void PlayCommand_Click(object sender, EventArgs e)
        {
            Playing = !Playing;
        }

        /// <summary>
        /// Raised when the playback timer ticks.
        /// Advances the current time index.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void PlaybackTimer_Tick(object sender, EventArgs e)
        {
            var range = TimeSelector.Maximum - TimeSelector.Minimum;
            if (range != 0)
            {
                var value = TimeSelector.Value + PlaybackTimer.Interval;
                value %= range;
                TimeIndex = value;
            }
            else
            {
                TimeIndex = TimeSelector.Minimum;
            }
        }

        /// <summary>
        /// Raised when TimeSelectorSections is painted.
        /// Draws sections along the bar to mark the durations of each frame in sequence.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The event data.</param>
        private void TimeSelectorSections_Paint(object sender, PaintEventArgs e)
        {
            if (sectionValues == null)
                return;

            Graphics graphics = e.Graphics;

            var colorIndex = 0;
            var currentValue = GetRelativeTime(TimeSelector.Value);
            for (var section = 0; section < sectionValues.Length - 1; section++)
            {
                var min = GetRelativeTime(sectionValues[section]);
                var max = GetRelativeTime(sectionValues[section + 1]);

                Brush brush = SectionBrushes[colorIndex];
                if (currentValue >= min && (currentValue < max || (currentValue == 1 && currentValue == max)))
                    brush = sectionHighlightBrush;

                var width = TimeSelectorSections.Width;
                var height = TimeSelectorSections.Height;
                graphics.FillRectangle(brush, min * width, 0, (max - min) * width, height);

                if (++colorIndex >= SectionBrushes.Length)
                    colorIndex = 0;
            }
        }

        /// <summary>
        /// Gets the normalized value of the time into the animation.
        /// </summary>
        /// <param name="time">Absolute time into the animation, in milliseconds.</param>
        /// <returns>A value between 0 and 1 representing the time into the animation.</returns>
        private float GetRelativeTime(int time)
        {
            return (float)(time - TimeSelector.Minimum) / (TimeSelector.Maximum - TimeSelector.Minimum);
        }

        /// <summary>
        /// Raises the <see cref="E:DesktopSprites.DesktopPonies.AnimatedImageIndexer.IndexChanged"/> event.
        /// </summary>
        protected virtual void OnIndexChanged()
        {
            UpdateLabel();
            TimeSelectorSections.Invalidate();
            IndexChanged.Raise(this);
        }
    }
}
