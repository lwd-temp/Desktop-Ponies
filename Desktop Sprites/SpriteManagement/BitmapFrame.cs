﻿namespace DesktopSprites.SpriteManagement
{
    using System;
    using System.Drawing;
    using DesktopSprites.Core;

    /// <summary>
    /// Defines a <see cref="T:DesktopSprites.SpriteManagement.Frame`1"/> whose underlying image is a
    /// <see cref="T:System.Drawing.Bitmap"/>.
    /// </summary>
    public sealed class BitmapFrame : Frame<Bitmap>, IDisposable
    {
        /// <summary>
        /// Creates an <see cref="T:DesktopSprites.SpriteManagement.AnimatedImage`1"/> of
        /// <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/>.
        /// </summary>
        /// <param name="fileName">The path to the file which contains the image to be loaded.</param>
        /// <returns>An animated image loaded from the specified file that uses bitmaps for each frame.</returns>
        public static AnimatedImage<BitmapFrame> AnimationFromFile(string fileName)
        {
            return new AnimatedImage<BitmapFrame>(fileName, path => new BitmapFrame(path), FromBuffer, AllowableBitDepths);
        }

        /// <summary>
        /// Gets the method that converts a buffer into an <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/>.
        /// </summary>
        public static BufferToImage<BitmapFrame> FromBuffer
        {
            get { return FromBufferInternal; }
        }
        /// <summary>
        /// The method that converts a buffer into an <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/>.
        /// </summary>
        private static readonly BufferToImage<BitmapFrame> FromBufferInternal =
            (byte[] buffer, RgbColor[] palette, byte? transparentIndex, int stride, int width, int height, byte depth) =>
            {
                Bitmap bitmap = GifImage.BufferToImageOfBitmap(buffer, palette, transparentIndex, stride, width, height, depth);
                var hashCode = GifImage.GetHash(buffer, palette, transparentIndex, width, height);
                return new BitmapFrame(bitmap, hashCode);
            };

        /// <summary>
        /// Represents the allowable set of depths that can be used when generating a
        /// <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/>.
        /// </summary>
        public const BitDepths AllowableBitDepths = GifImage.AllowableDepthsForBitmap;

        /// <summary>
        /// The hash code of the frame image.
        /// </summary>
        private int hashCode;

        /// <summary>
        /// Gets the dimensions of the frame.
        /// </summary>
        public override Size Size
        {
            get { return Image.Size; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/> class from the given
        /// <see cref="T:System.Drawing.Bitmap"/>.
        /// </summary>
        /// <param name="bitmap">The <see cref="T:System.Drawing.Bitmap"/> to use in the frame.</param>
        /// <param name="hash">The hash code of the frame.</param>
        public BitmapFrame(Bitmap bitmap, int hash)
            : base(bitmap)
        {
            hashCode = hash;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/> class from the given file.
        /// </summary>
        /// <param name="fileName">The path to a static image file from which to create a new
        /// <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/>.</param>
        public BitmapFrame(string fileName)
            : this(new Bitmap(fileName), Argument.EnsureNotNull(fileName, nameof(fileName)).GetHashCode())
        {
        }

        /// <summary>
        /// Gets the hash code of the frame image.
        /// </summary>
        /// <returns>A hash code for this frame image.</returns>
        public override int GetFrameHashCode()
        {
            return hashCode;
        }

        /// <summary>
        /// Releases all resources used by the <see cref="T:DesktopSprites.SpriteManagement.BitmapFrame"/> object.
        /// </summary>
        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
