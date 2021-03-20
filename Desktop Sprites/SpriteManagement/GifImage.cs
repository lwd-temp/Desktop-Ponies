﻿namespace DesktopSprites.SpriteManagement
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using DesktopSprites.Collections;
    using DesktopSprites.Core;

    #region BufferToImage delegate
    /// <summary>
    /// Takes the raw decoded buffer, and turns them it into an image of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the image to create.</typeparam>
    /// <param name="buffer">The values that make up the image. The buffer is of logical size stride * height. It is of physical size
    /// stride * height * depth / 8.</param>
    /// <param name="palette">The color palette for the image. Each value in the buffer is an index into the array. Note the size of the
    /// palette can exceed the maximum size addressable by values in the buffer.</param>
    /// <param name="transparentIndex">The index of the transparent color, or null to indicate no transparency. Where possible, this index
    /// in the palette should be replaced by transparency in the resulting frame, or else some suitable replacement. The color value in the
    /// palette for this index is undefined.</param>
    /// <param name="stride">The stride width, in bytes, of the buffer.</param>
    /// <param name="width">The logical width of the image the buffer contains.</param>
    /// <param name="height">The logical height of the image the buffer contains.</param>
    /// <param name="depth">The bit depth of the buffer (either 1, 2, 4 or 8).</param>
    /// <returns>A new <typeparamref name="T"/> image, constructed from the given raw buffer and palette.</returns>
    /// <remarks>
    /// <para>The <see cref="T:DesktopSprites.SpriteManagement.GifImage`1"/> will decode an image into a low-level buffer and palette.
    /// This function allows you to construct a graphics object of your choosing from this buffer. A typical example would be a
    /// <see cref="T:System.Drawing.Bitmap"/>.</para>
    /// <para>Usually, you will copy the buffer row by row to the target buffer of your object, in order to account for differing stride
    /// widths. Example pseudocode is provided for a theoretical Image object.</para>
    /// <example>
    /// <code><![CDATA[
    /// // Create an image of the given width and height in pixels, and of the given bit depth.
    /// // The GifDecoder can be constrained to produce certain bit depths, for formats that only support certain values.
    /// Image frame = new Image(width, height, depth);
    ///
    /// // Assume the image provides access to its buffer. Different formats allow different things. Some may allow an array to be passed
    /// // in as a parameter in the constructor, and then lock it.  Others may require use of marshaling to get access.
    /// byte[] frameBuffer = frame.Buffer;
    ///
    /// // Copy over row by row.
    /// for(int row = 0; row < height; row++)
    /// {
    ///     // Copy from the source buffer to the destination buffer.
    ///     // Notice how the starting index for each array is dependent of the stride width of their respective sources.
    ///     Array.Copy(buffer, row * stride, frameBuffer, row * frame.Stride, stride);
    /// }
    ///
    /// // Copy over the source palette, into a palette made up of theoretical Color elements.
    /// // Note we cannot be sure which palette will be bigger, and thus must check the bounds of each.
    /// for(int i = 0; i < palette.Length && i < frame.Palette.Length; i++)
    ///     frame.Palette[i] = new Color(palette[i].R, palette[i].G, palette[i].B);
    ///
    /// // Apply the transparent color. We assume our theoretical color has an alpha channel.
    /// if(transparentIndex != -1)
    ///     frame.Palette[transparentIndex] = Color.Transparent;
    ///
    /// // The image is complete.
    /// return frame;
    /// ]]></code></example>
    /// </remarks>
    public delegate T BufferToImage<T>(
    byte[] buffer, RgbColor[] palette, byte? transparentIndex, int stride, int width, int height, byte depth);
    #endregion

    /// <summary>
    /// Provides implementations of <see cref="T:DesktopSprites.SpriteManagement.GifImage`1"/> using various formats.
    /// </summary>
    public static class GifImage
    {
        #region BufferToImageOfBitmap function
        /// <summary>
        /// Gets the method that converts a buffer into an <see cref="T:System.Drawing.Bitmap"/>.
        /// </summary>
        public static BufferToImage<Bitmap> BufferToImageOfBitmap
        {
            get { return BufferToImageOfBitmapInternal; }
        }

        /// <summary>
        /// The method that converts a buffer into an <see cref="T:System.Drawing.Bitmap"/>.
        /// </summary>
        private static readonly BufferToImage<Bitmap> BufferToImageOfBitmapInternal =
            (byte[] buffer, RgbColor[] palette, byte? transparentIndex, int stride, int width, int height, byte depth) =>
            {
                PixelFormat targetFormat;
                if (depth == 1)
                    targetFormat = PixelFormat.Format1bppIndexed;
                else if (depth == 4)
                    targetFormat = PixelFormat.Format4bppIndexed;
                else if (depth == 8)
                    targetFormat = PixelFormat.Format8bppIndexed;
                else
                    throw new ArgumentOutOfRangeException(nameof(depth), depth, "depth must be 1, 4 or 8.");

                // Create the bitmap.
                return Disposable.SetupSafely(new Bitmap(width, height, targetFormat), bitmap =>
                {
                    // Lock the data into memory for fast marshaled access.
                    BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                    // Copy the frame buffer to the bitmap. To account for stride padding, copy row by row. Then unlock it.
                    for (var row = 0; row < data.Height; row++)
                        System.Runtime.InteropServices.Marshal.Copy(
                            buffer,
                            row * stride,
                            IntPtr.Add(data.Scan0, row * data.Stride),
                            stride);
                    bitmap.UnlockBits(data);

                    // Fill in the color palette from the current table.
                    ColorPalette bitmapPalette = bitmap.Palette;
                    for (var i = 0; i < palette.Length; i++)
                        bitmapPalette.Entries[i] = Color.FromArgb(palette[i].ToArgb());

                    // Apply transparency.
                    if (transparentIndex != null)
                        bitmapPalette.Entries[transparentIndex.Value] = Color.Transparent;

                    // Set palette on bitmap.
                    bitmap.Palette = bitmapPalette;
                });
            };
        #endregion

        /// <summary>
        /// Represents the allowable set of depths that can be used when generating a <see cref="T:System.Drawing.Bitmap"/>.
        /// </summary>
        public const BitDepths AllowableDepthsForBitmap = BitDepths.Indexed1Bpp | BitDepths.Indexed4Bpp | BitDepths.Indexed8Bpp;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifImage`1"/> class of type
        /// <see cref="T:System.Drawing.Bitmap"/> by decoding a GIF from the given stream.
        /// </summary>
        /// <param name="stream">A <see cref="T:System.IO.Stream"/> ready to read a GIF file.</param>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GifImage`1"/> of type <see cref="T:System.Drawing.Bitmap"/>
        /// initialized from the given stream.</returns>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="stream"/> is null.</exception>
        /// <exception cref="T:System.ArgumentException"><paramref name="stream"/> does not support reading.</exception>
        /// <exception cref="T:System.NotSupportedException">The file uses a feature of GIF that is not supported by the decoder.
        /// </exception>
        /// <exception cref="T:System.IO.InvalidDataException"><paramref name="stream"/> was not a GIF file, or was a badly formed GIF
        /// file.</exception>
        public static GifImage<Bitmap> OfBitmap(Stream stream)
        {
            return new GifImage<Bitmap>(stream, BufferToImageOfBitmap, AllowableDepthsForBitmap);
        }

        /// <summary>
        /// Gets a hash code for a frame buffer.
        /// </summary>
        /// <param name="buffer">The values that make up the image.</param>
        /// <param name="palette">The color palette for the image.</param>
        /// <param name="transparentIndex">The index of the transparent color in the palette.</param>
        /// <param name="width">The logical width of the image the buffer contains.</param>
        /// <param name="height">The logical height of the image the buffer contains.</param>
        /// <returns>A hash code for the current frame.</returns>
        public static int GetHash(byte[] buffer, RgbColor[] palette, byte? transparentIndex, int width, int height)
        {
            Argument.EnsureNotNull(buffer, nameof(buffer));
            Argument.EnsureNotNull(palette, nameof(palette));

            // Generate a quick hash just using the raw buffer values. This means some visually identical frames could hash differently if
            // the underlying buffer and color table conspire sufficiently.
            var hash = Hash.Fnv1A32(buffer);
            var colorValues = new byte[palette.Length * 3];
            var i = 0;
            foreach (var color in palette)
            {
                colorValues[i++] = color.R;
                colorValues[i++] = color.G;
                colorValues[i++] = color.B;
            }
            hash = Hash.Fnv1A32(colorValues, hash);
            if (transparentIndex != null)
                hash = Hash.Fnv1A32(BitConverter.GetBytes(transparentIndex.Value), hash);
            hash = Hash.Fnv1A32(BitConverter.GetBytes(width), hash);
            hash = Hash.Fnv1A32(BitConverter.GetBytes(height), hash);

            return hash;
        }
    }

    /// <summary>
    /// Describes a GIF file as a series of frames.
    /// </summary>
    /// <typeparam name="T">The type of the frame images.</typeparam>
    /// <remarks>
    /// This class provides easy access to the frames of an animated GIF file, as well as allowing the type of the image used for each
    /// frame to be specified so different graphics formats can make use of the class.
    /// </remarks>
    public class GifImage<T>
    {
        /// <summary>
        /// Gets the total duration of the image, in milliseconds.
        /// </summary>
        public int Duration { get; private set; }
        /// <summary>
        /// Gets the number of times this image plays. If 0, it loops indefinitely.
        /// </summary>
        public int Iterations { get; private set; }
        /// <summary>
        /// Gets the frames that make up this GIF image.
        /// </summary>
        public ImmutableArray<GifFrame<T>> Frames { get; private set; }
        /// <summary>
        /// Gets the size of the image.
        /// </summary>
        public Size Size
        {
            get { return new Size(Width, Height); }
        }
        /// <summary>
        /// Gets the width of the image.
        /// </summary>
        public int Width { get; private set; }
        /// <summary>
        /// Gets the height of the image.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifImage`1"/> class by decoding a GIF from the
        /// given stream.
        /// </summary>
        /// <param name="stream">A <see cref="T:System.IO.Stream"/> ready to read a GIF file.</param>
        /// <param name="imageFactory">The method used to construct an image of type <typeparamref name="T"/> from the decoded buffer.
        /// </param>
        /// <param name="allowableDepths">The allowable set of bit depths for the decoded buffer. Specify as many indexed formats as are
        /// supported by <typeparamref name="T"/>. If no such formats are supported, it is suggested you specify only the
        /// <see cref="F:DesktopSprites.SpriteManagement.BitDepths.Indexed8Bpp"/> format to make conversion easier. The
        /// <see cref="F:DesktopSprites.SpriteManagement.BitDepths.Indexed8Bpp"/> format must be specified.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="stream"/> is null.-or-<paramref name="imageFactory"/> is null.
        /// </exception>
        /// <exception cref="T:System.ComponentModel.InvalidEnumArgumentException"><paramref name="allowableDepths"/> is invalid.
        /// </exception>
        /// <exception cref="T:System.ArgumentException"><paramref name="stream"/> does not support reading.-or-
        /// <paramref name="allowableDepths"/> does not specify <see cref="F:DesktopSprites.SpriteManagement.BitDepths.Indexed8Bpp"/>.
        /// </exception>
        /// <exception cref="T:System.NotSupportedException">The file uses a feature of GIF that is not supported by the decoder.
        /// </exception>
        /// <exception cref="T:System.IO.InvalidDataException"><paramref name="stream"/> was not a GIF file, or was a badly formed GIF
        /// file.</exception>
        public GifImage(Stream stream, BufferToImage<T> imageFactory, BitDepths allowableDepths)
        {
            var decoder = new GifDecoder<T>(stream, imageFactory, allowableDepths);
            Duration = decoder.Duration;
            Iterations = decoder.Iterations;
            Frames = decoder.Frames;
            Width = decoder.Width;
            Height = decoder.Height;
        }
    }

    /// <summary>
    /// Decodes a GIF file into its component frames.
    /// </summary>
    /// <typeparam name="T">The type of the frame images.</typeparam>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "Field is disposed within constructor and so will never need disposing by consumers.")]
    internal sealed class GifDecoder<T>
    {
        #region BlockCode enum
        /// <summary>
        /// Defines block codes used to identify the type of block.
        /// </summary>
        private enum BlockCode : byte
        {
            /// <summary>
            /// Specifies the block is an image descriptor block (Value 0x2C, 44).
            /// </summary>
            ImageDescriptor = 44,
            /// <summary>
            /// Specifies the block is an extension block (Value 0x21, 33).
            /// </summary>
            Extension = 33,
            /// <summary>
            /// Specifies the block is the data stream trailer, and that the stream has ended (Value 0x3B, 59).
            /// </summary>
            Trailer = 59
        }
        #endregion

        #region ExtensionLabel enum
        /// <summary>
        /// Defines extension labels used within extension blocks to identify the type of extension.
        /// </summary>
        private enum ExtensionLabel : byte
        {
            /// <summary>
            /// Specifies the extension is a graphic control extension (Value 0xF9, 249).
            /// </summary>
            GraphicControl = 0xF9,
            /// <summary>
            /// Specifies the extension is an application extension (Value 0xFF, 255).
            /// </summary>
            Application = 0xFF,
            /// <summary>
            /// Specifies the extension is a comment extension (Value 0xFE, 254).
            /// </summary>
            Comment = 0xFE,
            /// <summary>
            /// Specifies the extension is a plain text extension (Value 0x01, 1).
            /// </summary>
            PlainText = 0x01,
        }
        #endregion

        #region DataBuffer class
        /// <summary>
        /// Provides a backing store for a set of values in 2-dimensions, with a given number of bits used for each value.
        /// </summary>
        private class DataBuffer
        {
            #region Iterator class
            /// <summary>
            /// Provides the ability to iterate over the rows of a <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DataBuffer"/>
            /// efficiently.
            /// </summary>
            public class Iterator
            {
                /// <summary>
                /// Gets the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DataBuffer"/> being iterated over.
                /// </summary>
                public DataBuffer DataBuffer { get; private set; }

                /// <summary>
                /// Reference to the underlying buffer in the data buffer.
                /// </summary>
                private byte[] buffer;
                /// <summary>
                /// Number of bits per value in the data buffer.
                /// </summary>
                private byte bitsPerValue;
                /// <summary>
                /// Number of values per byte in the data buffer.
                /// </summary>
                private byte valuesPerByte;

                /// <summary>
                /// Index into the buffer.
                /// </summary>
                private int index;
                /// <summary>
                /// A mask with the lowest n bits set where n is the number of bits per value.
                /// </summary>
                private byte lowMask = byte.MaxValue;
                /// <summary>
                /// A mask with the highest n bits set where n is the number of bits per value.
                /// </summary>
                private byte highMask = byte.MaxValue;
                /// <summary>
                /// The current mask with the n set bits shifted into the current position.
                /// </summary>
                private byte currentMask = byte.MaxValue;
                /// <summary>
                /// The initial shift to use when reading from the first value in a byte.
                /// </summary>
                private int startShift = 0;
                /// <summary>
                /// The current shift which would bring the current value into the low bits.
                /// </summary>
                private int shift = 0;

                /// <summary>
                /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DataBuffer.Iterator"/>
                /// class.
                /// </summary>
                /// <param name="dataBuffer">The data buffer to be iterated over.</param>
                /// <param name="x">The initial x co-ordinate position.</param>
                /// <param name="y">The initial y co-ordinate position.</param>
                public Iterator(DataBuffer dataBuffer, int x, int y)
                {
                    DataBuffer = dataBuffer;
                    buffer = DataBuffer.Buffer;
                    bitsPerValue = DataBuffer.BitsPerValue;
                    valuesPerByte = DataBuffer.ValuesPerByte;
                    SetPosition(x, y);
                }
                /// <summary>
                /// Increments the x co-ordinate. This is efficient and allows for quick row traversal.
                /// </summary>
                public void IncrementX()
                {
                    if (valuesPerByte == 1)
                    {
                        index++;
                        return;
                    }
                    shift -= bitsPerValue;
                    if (shift >= 0)
                    {
                        currentMask >>= bitsPerValue;
                    }
                    else
                    {
                        index++;
                        shift = startShift;
                        currentMask = highMask;
                    }
                }
                /// <summary>
                /// Sets the position in the buffer to be iterated from. This is slow and should only be used change to the start of new
                /// rows if possible.
                /// </summary>
                /// <param name="x">The x co-ordinate to set.</param>
                /// <param name="y">The y co-ordinate to set.</param>
                public void SetPosition(int x, int y)
                {
                    var seek = DataBuffer.Seek(x, y);
                    if (valuesPerByte == 1)
                    {
                        index = seek;
                        return;
                    }
                    index = seek / valuesPerByte;
                    startShift = 8 - bitsPerValue;
                    shift = startShift - bitsPerValue * (seek % valuesPerByte);
                    lowMask = (byte)(byte.MaxValue >> startShift);
                    highMask = (byte)(lowMask << startShift);
                    currentMask = (byte)(lowMask << shift);
                }
                /// <summary>
                /// Gets the value at the current position of the iterator.
                /// </summary>
                /// <returns>The value located in the current logical position of the data buffer that is being iterated over.</returns>
                public byte GetValue()
                {
                    return (byte)((buffer[index] >> shift) & lowMask);
                }
                /// <summary>
                /// Sets the value at the current position of the iterator.
                /// </summary>
                /// <param name="value">The value to set in the current logical position of the data buffer that is being iterated over.
                /// </param>
                public void SetValue(byte value)
                {
                    if (valuesPerByte == 1)
                    {
                        buffer[index] = value;
                        return;
                    }
                    buffer[index] &= (byte)~currentMask;
                    buffer[index] |= (byte)(value << shift);
                }
            }
            #endregion

            /// <summary>
            /// The number of bits used per value in the buffer.
            /// </summary>
            private byte bitsPerValue;
            /// <summary>
            /// Gets the number of bits used per value in the buffer.
            /// </summary>
            public byte BitsPerValue
            {
                get
                {
                    return bitsPerValue;
                }
                private set
                {
                    bitsPerValue = value;
                    ValuesPerByte = (byte)(8 / bitsPerValue);
                    UpdateSeekMultiplier();
                }
            }
            /// <summary>
            /// Gets the number of values that are stored in each byte of the buffer.
            /// </summary>
            public byte ValuesPerByte { get; private set; }
            /// <summary>
            /// Gets the maximum value that may be stored given the current BitsPerValue of the buffer.
            /// </summary>
            public byte MaxValue
            {
                get { return (byte)((1 << BitsPerValue) - 1); }
            }
            /// <summary>
            /// Gets the logical width of the buffer.
            /// </summary>
            public ushort Width { get; private set; }
            /// <summary>
            /// Gets the logical height of the buffer.
            /// </summary>
            public ushort Height { get; private set; }
            /// <summary>
            /// The stride width of the buffer.
            /// </summary>
            private ushort stride;
            /// <summary>
            /// Gets the stride width of the buffer.
            /// </summary>
            public ushort Stride
            {
                get
                {
                    return stride;
                }
                private set
                {
                    stride = value;
                    UpdateSeekMultiplier();
                }
            }
            /// <summary>
            /// Cache of the value Stride * ValuesPerByte. Helps speed up the Seek(int, int) method which is used in tight loops.
            /// </summary>
            private int seekMultiplier;
            /// <summary>
            /// Gets the underlying array of bytes for this buffer.
            /// </summary>
            public byte[] Buffer { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DataBuffer"/> class capable of
            /// holding enough values for the specified dimensions, using the given number of bits to store each value.
            /// </summary>
            /// <param name="width">The width of the buffer to create.</param>
            /// <param name="height">The height of the buffer to create.</param>
            /// <param name="bitsPerValue">The number of bits used to store each value. Any values given must be representable in this
            /// number of bits. The number of bits can only be 1, 2, 4 or 8.</param>
            /// <param name="initialValue">The initial value to use for each entry in the buffer.</param>
            /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="bitsPerValue"/> is not 1, 2, 4 or 8.-or-
            /// <paramref name="initialValue"/> is greater than the largest value that can be stored in the given number of bits.
            /// </exception>
            public DataBuffer(ushort width, ushort height, byte bitsPerValue, byte initialValue = 0)
            {
                if (bitsPerValue != 1 && bitsPerValue != 2 && bitsPerValue != 4 && bitsPerValue != 8)
                    throw new ArgumentOutOfRangeException(nameof(bitsPerValue), bitsPerValue, "bitsPerValue may only be 1, 2, 4 or 8.");

                BitsPerValue = bitsPerValue;

                if (initialValue > MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(initialValue), initialValue,
                        "initialValue is greater than the largest value that can be stored in the given number of bits (" +
                        MaxValue + ").");

                Width = width;
                Height = height;
                Stride = (ushort)Math.Ceiling(Width * BitsPerValue / 8f);
                Buffer = new byte[Stride * Height];

                if (initialValue != 0)
                    FillBuffer(initialValue);
            }
            /// <summary>
            /// Refreshes the value of the seekMultipler field.
            /// </summary>
            private void UpdateSeekMultiplier()
            {
                seekMultiplier = Stride * ValuesPerByte;
            }
            /// <summary>
            /// Efficiently sets all values in the buffer to the given value.
            /// </summary>
            /// <param name="value">The value to set for each entry in the buffer.</param>
            public void FillBuffer(byte value)
            {
                var byteValue = AllValuesInByte(value);
                for (var i = 0; i < Buffer.Length; i++)
                    Buffer[i] = byteValue;
            }
            /// <summary>
            /// Efficiently sets all values in the buffer within the given bounds to the given value.
            /// </summary>
            /// <param name="value">The value to set for each entry in the buffer.</param>
            /// <param name="bounds">The area in which to apply the value. Areas outside these bounds will not be changed.</param>
            /// <exception cref="T:System.ArgumentException"><paramref name="bounds"/> extends outside the area of the buffer.</exception>
            public void FillBuffer(byte value, Rectangle bounds)
            {
                var byteValue = AllValuesInByte(value);
                FillBuffer(bounds, true, (x, y) => value, i => byteValue);
            }
            /// <summary>
            /// Efficiently sets all values in the buffer within the given bounds to the values contained in the source buffer.
            /// </summary>
            /// <param name="source">The buffer whose values are the source values to set.</param>
            /// <param name="bounds">The area in which to change values. Areas outside these bounds will not be changed.</param>
            /// <exception cref="T:System.ArgumentException"><paramref name="bounds"/> extends outside the area of the buffer.</exception>
            public void FillBuffer(DataBuffer source, Rectangle bounds)
            {
                FillBuffer(bounds, false, (x, y) => source.GetValue(x, y), i => source.Buffer[i]);
            }
            /// <summary>
            /// Efficiently sets all values in the buffer within the given bounds to the values created by the given functions.
            /// </summary>
            /// <param name="bounds">The area in which to apply the value. Areas outside these bounds will not be changed.</param>
            /// <param name="fixedValues">Indicates if both the factory delegates return fixed values regardless of input.</param>
            /// <param name="valueFactory">Function that returns the value to set, given an x and y location in the buffer.</param>
            /// <param name="bufferValueFactory">Function that returns a byte to set, given an index in the buffer.</param>
            /// <exception cref="T:System.ArgumentException"><paramref name="bounds"/> extends outside the area of the buffer.</exception>
            private void FillBuffer(Rectangle bounds, bool fixedValues,
                Func<int, int, byte> valueFactory, Func<int, byte> bufferValueFactory)
            {
                if (!new Rectangle(Point.Empty, new Size(Width, Height)).Contains(bounds))
                    throw new ArgumentException("Given bounds must not extend outside the area of the buffer.", nameof(bounds));

                // We want to set all the indices within the given area only.

                // Easy method: iterate over the values within area and SetValue everything.
                // Repeated bit twiddling on the same bytes isn't the fastest though.
                // for (int y = bounds.Top; y < bounds.Bottom; y++)
                //     for (int x = bounds.Left; x < bounds.Right; x++)
                //         SetValue(x, y, value);

                // Iterate over each row.
                // Use SetValue on the left and right edges of the row, when bit twiddling is required.
                // We can then speed things up by using the contiguous middle region and setting it with our precomputed value.
                for (var y = bounds.Top; y < bounds.Bottom; y++)
                {
                    var x = bounds.Left;
                    // Set values until we are aligned on a byte
                    while (x % ValuesPerByte != 0 && x < bounds.Right)
                    {
                        SetValue(x, y, valueFactory(x, y));
                        x++;
                    }
                    // Set whole bytes along this row.
                    if (x < bounds.Right)
                    {
                        var rowAlignmentStart = Seek(x, y) / ValuesPerByte;
                        var rowAlignmentEnd = Seek(bounds.Right, y) / ValuesPerByte - 1;
                        if (fixedValues)
                        {
                            var value = bufferValueFactory(0);
                            for (var i = rowAlignmentStart; i < rowAlignmentEnd; i++)
                            {
                                Buffer[i] = value;
                                x += ValuesPerByte;
                            }
                        }
                        else
                        {
                            for (var i = rowAlignmentStart; i < rowAlignmentEnd; i++)
                            {
                                Buffer[i] = bufferValueFactory(i);
                                x += ValuesPerByte;
                            }
                        }
                    }
                    // Set values in the unaligned area until the row is done.
                    while (x < bounds.Right)
                    {
                        SetValue(x, y, valueFactory(x, y));
                        x++;
                    }
                }
            }
            /// <summary>
            /// Gets a byte with all values in that byte set to the given value.
            /// </summary>
            /// <param name="value">The value to set for each entry in the byte.</param>
            /// <returns>A byte with all values in that byte set to the given value.</returns>
            private byte AllValuesInByte(byte value)
            {
                var resultValue = value;
                if (BitsPerValue != 8 && value != byte.MinValue && value != byte.MaxValue)
                {
                    if (BitsPerValue == 4)
                        resultValue = (byte)((value << 4) | value);
                    else if (BitsPerValue == 2)
                        resultValue = (byte)((value << 6) | (value << 4) | (value << 2) | value);
                    else if (BitsPerValue == 1)
                        resultValue = (byte)((value << 7) | (value << 6) | (value << 5) | (value << 4) |
                            (value << 3) | (value << 2) | (value << 1) | value);
                }
                return resultValue;
            }
            /// <summary>
            /// Gets the logical index of the value to seek along the single dimension of the buffer.
            /// </summary>
            /// <param name="x">The x co-ordinate of the value.</param>
            /// <param name="y">The y co-ordinate of the value.</param>
            /// <returns>The logical index to seek, along the single dimension of the buffer.</returns>
            private int Seek(int x, int y)
            {
                return seekMultiplier * y + x;
            }
            /// <summary>
            /// Gets the value from the given location in the buffer.
            /// </summary>
            /// <param name="x">The x co-ordinate of the value to get.</param>
            /// <param name="y">The y co-ordinate of the value to get.</param>
            /// <returns>The value at this location.</returns>
            public byte GetValue(int x, int y)
            {
                var seek = Seek(x, y);
                if (ValuesPerByte == 1)
                {
                    return Buffer[seek];
                }
                else
                {
                    var index = seek / ValuesPerByte;
                    var shiftToEdge = 8 - BitsPerValue;
                    var shift = shiftToEdge - BitsPerValue * (seek % ValuesPerByte);
                    var lowMask = byte.MaxValue >> shiftToEdge;
                    var valueShifted = Buffer[index] >> shift;
                    return (byte)(valueShifted & lowMask);
                }
            }
            /// <summary>
            /// Sets a value at the given location in the buffer. The given value should be less than or equal to
            /// <see cref="P:DesktopSprites.SpriteManagement.GifImage`1.DataBuffer.MaxValue"/>.
            /// </summary>
            /// <param name="x">The x co-ordinate of the value to set.</param>
            /// <param name="y">The y co-ordinate of the value to set.</param>
            /// <param name="value">The value to set at this location.</param>
            public void SetValue(int x, int y, byte value)
            {
                var seek = Seek(x, y);
                if (ValuesPerByte == 1)
                {
                    Buffer[seek] = value;
                }
                else
                {
                    var index = seek / ValuesPerByte;
                    var shiftToEdge = 8 - BitsPerValue;
                    var shift = shiftToEdge - BitsPerValue * (seek % ValuesPerByte);
                    var lowMask = byte.MaxValue >> shiftToEdge;
                    var mask = ~(lowMask << shift);
                    var valueShifted = value << shift;
                    Buffer[index] &= (byte)mask;
                    Buffer[index] |= (byte)valueShifted;
                }
            }
            /// <summary>
            /// Gets an iterator that can be used to efficiently set values in this buffer.
            /// </summary>
            /// <param name="x">The initial x co-ordinate of the iterator.</param>
            /// <param name="y">The initial y co-ordinate of the iterator.</param>
            /// <returns>An iterator that can be used to efficiently set values in this buffer.</returns>
            public Iterator GetIterator(int x, int y)
            {
                return new Iterator(this, x, y);
            }
            /// <summary>
            /// Doubles the current <see cref="P:DesktopSprites.SpriteManagement.GifImage`1.DataBuffer.BitPerValue"/>, thus doubling the
            /// range of values that may be stored in each entry of the buffer.
            /// </summary>
            /// <exception cref="T:System.InvalidOperationException">The number of bits used for each value is already at its maximum size.
            /// </exception>
            public void UpsizeBuffer()
            {
                if (BitsPerValue == 8)
                    throw new InvalidOperationException("The BitsPerValue for the buffer is already at its maximum of 8.");

                var newBitsPerValue = (byte)(BitsPerValue * 2);
                var newDataBuffer = new DataBuffer(Width, Height, newBitsPerValue);

                for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                        newDataBuffer.SetValue(x, y, GetValue(x, y));

                BitsPerValue = newBitsPerValue;
                Stride = newDataBuffer.Stride;
                Buffer = newDataBuffer.Buffer;
            }
            /// <summary>
            /// Makes this <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DataBuffer"/> equivalent to
            /// <paramref name="buffer"/>.
            /// </summary>
            /// <param name="buffer">The <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DataBuffer"/> whose data should be
            /// copied into this buffer.</param>
            public void MakeEqual(DataBuffer buffer)
            {
                while (buffer.BitsPerValue > BitsPerValue)
                    UpsizeBuffer();

                Array.Copy(buffer.Buffer, Buffer, Buffer.Length);
            }
            /// <summary>
            /// Enumerates through each lookup value in the frame buffer.
            /// </summary>
            /// <returns>An <see cref="T:System.Collections.Generic.IEnumerable`1"/> that returns each lookup value in the frame buffer.
            /// </returns>
            public IEnumerable<byte> EnumerateValues()
            {
                if (ValuesPerByte == 1)
                {
                    // Values are byte aligned.
                    for (var i = 0; i < Buffer.Length; i++)
                        yield return Buffer[i];
                }
                else if (Stride == Width)
                {
                    // Values are packed, and so must be unpacked.
                    var mask = (byte)(0xFF >> 8 - BitsPerValue);
                    for (var i = 0; i < Buffer.Length; i++)
                    {
                        var value = Buffer[i];
                        for (byte valueInByte = 0; valueInByte < ValuesPerByte; valueInByte++)
                        {
                            yield return (byte)(value & mask);
                            value >>= BitsPerValue;
                        }
                    }
                }
                else
                {
                    // Values are packed, and we must account for some padding at the end of each row which should be skipped.
                    var x = 0;
                    var y = 0;
                    var mask = (byte)(0xFF >> 8 - BitsPerValue);
                    for (var i = 0; i < Buffer.Length; i++)
                    {
                        var value = Buffer[i];
                        for (byte valueInByte = 0; valueInByte < ValuesPerByte; valueInByte++)
                        {
                            yield return (byte)(value & mask);
                            value >>= BitsPerValue;
                            if (++x >= Width)
                            {
                                x = 0;
                                y++;
                                // Skip the padding bits at the end of the row.
                                break;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region LzwDictionary class
        /// <summary>
        /// Processes LZW codes, providing decoded bytes.
        /// </summary>
        private class LzwDictionary
        {
            /// <summary>
            /// Indicates the result of processing a code.
            /// </summary>
            public enum Result
            {
                /// <summary>
                /// The end of information from the stream has been reached.
                /// </summary>
                EndOfInformation,
                /// <summary>
                /// The dictionary was reset.
                /// </summary>
                Cleared,
                /// <summary>
                /// The first code was processed and the value was pushed to the stack.
                /// </summary>
                FirstCode,
                /// <summary>
                /// A code was processed and the codeword values were pushed into the stack.
                /// </summary>
                PushedCodeword
            }
            // The values read in are variable length codewords, which can be of any length.
            // The GIF specification sets a maximum code size of 12, and thus a maximum codeword count of 2^12 = 4096.
            // Thus we need to maintain a dictionary of up to 4096 codewords (which each can be of any length).
            // These codewords have a property that make them easy to store however.
            // If some word w plus some character c is in the dictionary, then the word w will be in the dictionary.
            // Thus the prefix word w can be used to lookup the character at the end of its length.
            // The suffix character c on this word maps to an actual value that is added to the pixel stack.
            /// <summary>
            /// The lookup array of "prefix" words. The value is the index of the "suffix" character at the end of the given codeword.
            /// </summary>
            private short[] prefix = new short[MaxCodeWords];
            /// <summary>
            /// The lookup array of "suffix" characters. This holds the actual decompressed byte value for the given code character.
            /// </summary>
            private byte[] suffix = new byte[MaxCodeWords];
            /// <summary>
            /// Gets the first unused index in the dictionary.
            /// </summary>
            public short Available { get; private set; }
            /// <summary>
            /// Stores the root character at the end of a codeword.
            /// </summary>
            private byte rootCode;
            /// <summary>
            /// The clear code is the first available unused code. We will use values below this as root characters in the dictionary.
            /// </summary>
            private short clearCode;
            /// <summary>
            /// The last code added to the dictionary.
            /// </summary>
            private short codeLastAdded;

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.LzwDictionary"/> class.
            /// </summary>
            /// <param name="minimumCodeSize">The minimum length of codes in bits.</param>
            public LzwDictionary(byte minimumCodeSize)
            {
                clearCode = (short)(1 << minimumCodeSize);
                // Initialize table with root characters.
                for (short i = 0; i < clearCode; i++)
                {
                    prefix[i] = 0;
                    suffix[i] = (byte)i;
                }
                Reset();
            }
            /// <summary>
            /// Resets the dictionary.
            /// </summary>
            private void Reset()
            {
                Available = (short)(clearCode + 2);
                codeLastAdded = -1;
            }
            /// <summary>
            /// Processes an incoming code, potentially adding it to the dictionary. If the code is not special, the codeword it represents
            /// will be decoded into the given stack buffer.
            /// </summary>
            /// <param name="code">The new code to process.</param>
            /// <param name="stack">A stack into which a decoded codeword should reside.</param>
            /// <param name="stackIndex">The index to the first free element in the stack.</param>
            /// <returns>The result of processing the code.</returns>
            public Result ProcessCode(short code, byte[] stack, ref int stackIndex)
            {
                // Indicates the end of input data.
                if (code > Available || code == clearCode + 1)
                    return Result.EndOfInformation;

                // A clear code means the decoder dictionary should be reset.
                if (code == clearCode)
                {
                    Reset();
                    return Result.Cleared;
                }

                // Record the first code after an initialization/reset.
                if (codeLastAdded == -1)
                {
                    stack[stackIndex++] = suffix[code];
                    codeLastAdded = code;
                    rootCode = (byte)code;
                    return Result.FirstCode;
                }

                DecompressCodeword(code, stack, ref stackIndex);
                Add(code);
                return Result.PushedCodeword;
            }
            /// <summary>
            /// Decodes the codeword the given code represents into the given stack buffer.
            /// </summary>
            /// <param name="code">The code.</param>
            /// <param name="stack">A stack into which a decoded codeword should reside.</param>
            /// <param name="stackIndex">The index to the first free element in the stack.</param>
            private void DecompressCodeword(short code, byte[] stack, ref int stackIndex)
            {
                // Handle the case where a code word was not yet defined. This only happens in one special case.
                // Given string s and character c, this happens only when we see the sequence s.c.s.c.s and the word s.c was already in
                // the dictionary beforehand.
                if (code == Available)
                {
                    stack[stackIndex++] = rootCode;
                    code = codeLastAdded;
                }
                // Enumerate through the code word and push values to the stack.
                while (code > clearCode)
                {
                    stack[stackIndex++] = suffix[code];
                    code = prefix[code];
                }
                // Record and push the root character to the stack.
                rootCode = suffix[code];
                stack[stackIndex++] = rootCode;
            }
            /// <summary>
            /// Adds a code to the dictionary.
            /// </summary>
            /// <param name="code">The code to add.</param>
            private void Add(short code)
            {
                // Add words to the dictionary whilst it is not full.
                // If it is full, we keep the same dictionary until we are reset.
                if (Available < MaxCodeWords)
                {
                    // Add a new word to the dictionary.
                    prefix[Available] = codeLastAdded;
                    suffix[Available] = rootCode;
                    Available++;
                }
                codeLastAdded = code;
            }
        }
        #endregion

        #region LogicalScreenDescriptor class
        /// <summary>
        /// Provides a description of the image dimensions and global color table.
        /// </summary>
        private class LogicalScreenDescriptor
        {
            /// <summary>
            /// Gets the width of the logical screen, in pixels.
            /// </summary>
            public ushort Width { get; private set; }
            /// <summary>
            /// Gets the height of the logical screen, in pixels.
            /// </summary>
            public ushort Height { get; private set; }
            /// <summary>
            /// Gets a value indicating whether a global color table exists. If true, the
            /// <see cref="P:DesktopSprites.SpriteManagement.GifImage`1.LogicalScreenDescriptor.BackgroundIndex"/> property is meaningful.
            /// </summary>
            public bool GlobalTableExists { get; private set; }
            /// <summary>
            /// Gets the number of bits available for each color in the original image.
            /// </summary>
            public byte OriginalBitsPerColor { get; private set; }
            /// <summary>
            /// Gets a value indicating whether the global color table is sorted. If true, colors are listed in decreasing order of
            /// importance; otherwise, no ordering is defined.
            /// </summary>
            public bool GlobalTableSorted { get; private set; }
            /// <summary>
            /// Gets the number of colors in the global color table.
            /// </summary>
            public int GlobalTableSize { get; private set; }
            /// <summary>
            /// Gets the index of the background color in the global color table. Only meaningful if
            /// <see cref="P:DesktopSprites.SpriteManagement.GifImage`1.LogicalScreenDescriptor.GlobalTableExists"/> is true.
            /// </summary>
            public byte BackgroundIndex { get; private set; }
            /// <summary>
            /// Gets an approximation of the aspect ratio of the original image. If null, no information exists.
            /// </summary>
            public float? ApproximateAspectRatio { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.LogicalScreenDescriptor"/>
            /// class.
            /// </summary>
            /// <param name="logicalScreenWidth">The width of the logical screen.</param>
            /// <param name="logicalScreenHeight">The height of the logical screen.</param>
            /// <param name="globalColorTableFlag">Indicates if a global color table is present.</param>
            /// <param name="colorResolution">The original color resolution of the image.</param>
            /// <param name="sortFlag">Indicates if the global color table is sorted.</param>
            /// <param name="sizeOfGlobalColorTable">The number of colors in the global color table.</param>
            /// <param name="backgroundColorIndex">The index of the background color in the global color table.</param>
            /// <param name="pixelAspectRatio">The aspect ratio of the pixel dimensions of the image.</param>
            internal LogicalScreenDescriptor(ushort logicalScreenWidth, ushort logicalScreenHeight,
                bool globalColorTableFlag, byte colorResolution, bool sortFlag,
                int sizeOfGlobalColorTable, byte backgroundColorIndex, byte pixelAspectRatio)
            {
                Width = logicalScreenWidth;
                Height = logicalScreenHeight;
                GlobalTableExists = globalColorTableFlag;
                OriginalBitsPerColor = colorResolution;
                GlobalTableSorted = sortFlag;
                GlobalTableSize = sizeOfGlobalColorTable;
                BackgroundIndex = backgroundColorIndex;
                if (pixelAspectRatio == 0)
                    ApproximateAspectRatio = null;
                else
                    ApproximateAspectRatio = (pixelAspectRatio + 15) / 64f;
            }
        }
        #endregion

        #region GraphicControlExtension class
        /// <summary>
        /// Provides a description of how subframes should be layered, the use of transparency and frame delay.
        /// </summary>
        private class GraphicControlExtension
        {
            /// <summary>
            /// Gets the method by which pixels should be overwritten.
            /// </summary>
            public DisposalMethod DisposalMethod { get; private set; }
            /// <summary>
            /// Gets a value indicating whether user input is expected.
            /// </summary>
            public bool UserInputExpected { get; private set; }
            /// <summary>
            /// Gets a value indicating whether transparency is used. If true, indexes matching the
            /// <see cref="P:DesktopSprites.SpriteManagement.GifImage`1.GraphicControlExtension.TransparentIndex"/> should be ignored.
            /// </summary>
            public bool TransparencyUsed { get; private set; }
            /// <summary>
            /// Gets the delay for this frame, in milliseconds, which indicates how much time should pass before the next frame is
            /// rendered.
            /// </summary>
            public int Delay { get; private set; }
            /// <summary>
            /// Gets the index of the transparent color in the color table. Only meaningful if
            /// <see cref="P:DesktopSprites.SpriteManagement.GifImage`1.GraphicControlExtension.TransparentUsed"/> is true.
            /// </summary>
            public byte TransparentIndex { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.GraphicControlExtension"/>
            /// class.
            /// </summary>
            /// <param name="disposalMethod">The <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.DisposalMethod"/> to be used
            /// after this subframe is rendered.</param>
            /// <param name="userInputFlag">Indicates if user input is expected.</param>
            /// <param name="transparencyFlag">Indicates if transparency is used. If true the <paramref name="transparentColorIndex"/>
            /// should be treated as transparent.</param>
            /// <param name="delayTime">The delay, in hundredths of a second, before the next frame should be rendered.</param>
            /// <param name="transparentColorIndex">The index of the color to treat as transparent, if the
            /// <paramref name="transparencyFlag"/> is set.</param>
            /// <exception cref="System.ComponentModel.InvalidEnumArgumentException"><paramref name="disposalMethod"/> is invalid.
            /// </exception>
            internal GraphicControlExtension(
                DisposalMethod disposalMethod, bool userInputFlag, bool transparencyFlag, ushort delayTime, byte transparentColorIndex)
            {
                if (disposalMethod > DisposalMethod.RestorePrevious)
                    throw new System.ComponentModel.InvalidEnumArgumentException(
                        "disposalMethod", (byte)disposalMethod, typeof(DisposalMethod));

                DisposalMethod = disposalMethod;
                UserInputExpected = userInputFlag;
                TransparencyUsed = transparencyFlag;
                Delay = delayTime * 10;
                TransparentIndex = transparentColorIndex;
            }
        }
        #endregion

        #region ImageDescriptor class
        /// <summary>
        /// Provides a description of the subframe location and dimensions, and the local color table.
        /// </summary>
        private class ImageDescriptor
        {
            /// <summary>
            /// Gets the location and dimensions, in pixels, of the subframe area.
            /// </summary>
            public Rectangle Subframe { get; private set; }
            /// <summary>
            /// Gets a value indicating whether a local color table exists.
            /// </summary>
            public bool LocalTableExists { get; private set; }
            /// <summary>
            /// Gets a value indicating whether the subframe image is interlaced in a four-pass interlace pattern.
            /// </summary>
            public bool Interlaced { get; private set; }
            /// <summary>
            /// Gets a value indicating whether the local color table is sorted. If true, colors are listed in decreasing order of
            /// importance; otherwise, no ordering is defined.
            /// </summary>
            public bool LocalTableSorted { get; private set; }
            /// <summary>
            /// Gets the number of colors in the local color table.
            /// </summary>
            public int LocalTableSize { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.ImageDescriptor"/> class.
            /// </summary>
            /// <param name="imageLeftPosition">The left position of the subframe.</param>
            /// <param name="imageTopPosition">The top position of the subframe.</param>
            /// <param name="imageWidth">The width of the subframe.</param>
            /// <param name="imageHeight">The height of the subframe.</param>
            /// <param name="localColorTableFlag">Indicates if a local color table is present.</param>
            /// <param name="interlaceFlag">Indicates if the image is interlaced.</param>
            /// <param name="sortFlag">Indicates if the local color table is sorted.</param>
            /// <param name="sizeOfLocalColorTable">The number of colors in the local color table.</param>
            internal ImageDescriptor(ushort imageLeftPosition, ushort imageTopPosition, ushort imageWidth, ushort imageHeight,
                bool localColorTableFlag, bool interlaceFlag, bool sortFlag, int sizeOfLocalColorTable)
            {
                Subframe = new Rectangle(imageLeftPosition, imageTopPosition, imageWidth, imageHeight);
                LocalTableExists = localColorTableFlag;
                Interlaced = interlaceFlag;
                LocalTableSorted = sortFlag;
                LocalTableSize = sizeOfLocalColorTable;
            }
        }
        #endregion

        #region DisposalMethod enum
        /// <summary>
        /// Defines how pixels are to be overwritten.
        /// </summary>
        private enum DisposalMethod : byte
        {
            /// <summary>
            /// Undefined and should not be used. If encountered, treat as DoNotDispose.
            /// </summary>
            Undefined = 0,
            /// <summary>
            /// Keep the current buffer, later frames will layer above it.
            /// </summary>
            DoNotDispose = 1,
            /// <summary>
            /// Clear the background within the sub-frame that was just rendered.
            /// </summary>
            RestoreBackground = 2,
            /// <summary>
            /// Reset the background within the sub-frame that was just rendered to its previous values.
            /// </summary>
            RestorePrevious = 3,
        }
        #endregion

        #region Fields and Properties
        /// <summary>
        /// Gets the total duration of the image, in milliseconds.
        /// </summary>
        public int Duration { get; private set; }
        /// <summary>
        /// Gets the number of times this image plays. If 0, it loops indefinitely.
        /// </summary>
        public ushort Iterations { get; private set; }
        /// <summary>
        /// Gets the frames that make up this GIF image.
        /// </summary>
        public ImmutableArray<GifFrame<T>> Frames { get; private set; }
        /// <summary>
        /// Gets the size of the image.
        /// </summary>
        public Size Size
        {
            get { return new Size(Width, Height); }
        }
        /// <summary>
        /// Gets the width of the image.
        /// </summary>
        public ushort Width { get; private set; }
        /// <summary>
        /// Gets the height of the image.
        /// </summary>
        public ushort Height { get; private set; }

        /// <summary>
        /// The maximum number of codewords that can occur according to the GIF specification.
        /// </summary>
        private const int MaxCodeWords = 4096;
        /// <summary>
        /// Accesses the input stream being decoded.
        /// </summary>
        private readonly BinaryReader reader;
        /// <summary>
        /// Creates a frame of the desired type from the raw buffer.
        /// </summary>
        private readonly BufferToImage<T> createFrame;
        /// <summary>
        /// The set of valid bit depths for use within buffers.
        /// </summary>
        private readonly BitDepths validDepths;
        /// <summary>
        /// The description of the logical screen and global color table.
        /// </summary>
        private LogicalScreenDescriptor screenDescriptor;

        /// <summary>
        /// The frames that make up the image.
        /// </summary>
        private List<GifFrame<T>> frames = new List<GifFrame<T>>();
        /// <summary>
        /// Buffer holding the information for the frame.
        /// </summary>
        private DataBuffer frameBuffer;
        /// <summary>
        /// Buffer holding information for the previous frame.
        /// </summary>
        private DataBuffer previousFrameBuffer;
        /// <summary>
        /// The global color table.
        /// </summary>
        private RgbColor[] globalColorTable;
        /// <summary>
        /// The color table currently in use.
        /// </summary>
        private RgbColor[] colorTable;
        /// <summary>
        /// The index in the color table being used for transparency across all frames.
        /// </summary>
        private byte imageTransparentIndex;
        /// <summary>
        /// The backup index in the color table whose color value matches that of the image transparent index.
        /// </summary>
        private byte imageTransparentIndexRemap;
        /// <summary>
        /// Indicates if any frame so far has used transparency, and thus there might be a transparent region in the buffer.
        /// </summary>
        private bool anyTransparencyUsed = true;
        /// <summary>
        /// This array is a buffer to hold the data read in from an image data sub block.
        /// </summary>
        private readonly byte[] block = new byte[256];
        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1"/> class by decoding a GIF from
        /// the given stream.
        /// </summary>
        /// <param name="stream">A <see cref="T:System.IO.Stream"/> ready to read a GIF file.</param>
        /// <param name="imageFactory">The method used to construct an image of type <typeparamref name="T"/> from the decoded buffer.
        /// </param>
        /// <param name="allowableDepths">The allowable set of bit depths for the decoded buffer. Specify as many indexed formats as are
        /// supported by <typeparamref name="T"/>. If no such formats are supported, it is suggested you specify only the
        /// <see cref="F:DesktopSprites.SpriteManagement.BitDepths.Indexed8Bpp"/> format to make conversion easier. The
        /// <see cref="F:DesktopSprites.SpriteManagement.BitDepths.Indexed8Bpp"/> format must be specified.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="stream"/> is null.-or-<paramref name="imageFactory"/> is null.
        /// </exception>
        /// <exception cref="T:System.ComponentModel.InvalidEnumArgumentException"><paramref name="allowableDepths"/> is invalid.
        /// </exception>
        /// <exception cref="T:System.ArgumentException"><paramref name="stream"/> does not support reading.-or-
        /// <paramref name="allowableDepths"/> does not specify <see cref="F:DesktopSprites.SpriteManagement.BitDepths.Indexed8Bpp"/>.
        /// </exception>
        /// <exception cref="T:System.NotSupportedException">The file uses a feature of GIF that is not supported by the decoder.
        /// </exception>
        /// <exception cref="T:System.IO.InvalidDataException"><paramref name="stream"/> was not a GIF file, or was a badly formed GIF
        /// file.</exception>
        public GifDecoder(Stream stream, BufferToImage<T> imageFactory, BitDepths allowableDepths)
        {
            Argument.EnsureNotNull(stream, nameof(stream));
            Argument.EnsureNotNull(imageFactory, nameof(imageFactory));
            Argument.EnsureEnumIsValid(allowableDepths, nameof(allowableDepths));
            if (!stream.CanRead)
                throw new ArgumentException("stream must support reading.", nameof(stream));
            if (!allowableDepths.HasFlag(BitDepths.Indexed8Bpp))
                throw new ArgumentException("allowableDepths must support at least the Indexed8Bpp format.", nameof(allowableDepths));

            createFrame = imageFactory;
            validDepths = allowableDepths;
            try
            {
                reader = new BinaryReader(stream);
                DecodeGif();
            }
            finally
            {
                reader.Dispose();
                reader = null;
            }
        }
        /// <summary>
        /// Reads the color table and sets up the buffer and transparent indexes.
        /// </summary>
        /// <param name="tableSize">The size of the color table to read.</param>
        private void SetupColorTable(int tableSize)
        {
            colorTable = ReadColorTable(tableSize);
            if (frameBuffer == null)
            {
                // Create the initial buffers.
                DetermineTransparentIndexes();
                var targetBpp = TargetBitsPerPixel(colorTable.Length);
                frameBuffer = new DataBuffer(Width, Height, targetBpp, imageTransparentIndex);
                previousFrameBuffer = new DataBuffer(Width, Height, targetBpp, imageTransparentIndex);
            }
            else
            {
                AdjustForChangedColorTable();
            }
        }
        /// <summary>
        /// Reads a color table block containing the given number of colors. These are optional blocks. Up to one global table may exist
        /// per data stream. Up to one local table may exist per image.
        /// </summary>
        /// <param name="colorCount">The number of colors in the table.</param>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.RgbColor[]"/> containing the colors read in from the block.
        /// </returns>
        private RgbColor[] ReadColorTable(int colorCount)
        {
            var colors = new RgbColor[colorCount];
            for (var i = 0; i < colors.Length; i++)
                colors[i] = new RgbColor(reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            return colors;
        }
        /// <summary>
        /// Determines the indexes to use for transparency in the color table.
        /// </summary>
        private void DetermineTransparentIndexes()
        {
            // Find a pair of indexes in the color table with the same lookup value.
            // Start from the end of the table, since that's usually where all the uninitialized values live. Usually this means the loop
            // will return in the first iteration.
            var found = false;
            for (var i = colorTable.Length - 1; i >= 1 && !found; i--)
                for (var j = i - 1; j >= 0 && !found; j--)
                    if (colorTable[i] == colorTable[j])
                    {
                        imageTransparentIndex = (byte)i;
                        imageTransparentIndexRemap = (byte)j;
                        found = true;
                    }

            if (!found)
            {
                // There were no duplicate indexes. But there is room to make the color table bigger and use the extra slot.
                if (colorTable.Length != 256)
                {
                    var newTable = new RgbColor[colorTable.Length + 1];
                    Array.Copy(colorTable, newTable, colorTable.Length);
                    colorTable = newTable;
                }

                // Use the last index in the new table, or if there was no room to expand the table, use the last index and just hope that
                // index was reserved for transparency or that transparency is not required.
                imageTransparentIndex = (byte)(colorTable.Length - 1);
                imageTransparentIndexRemap = (byte)(colorTable.Length - 1);
            }
        }
        /// <summary>
        /// Returns the number of bits per pixel that should be used in an indexed bitmap in order to accommodate the number of entries in
        /// the color table, whilst only using valid bit depths.
        /// </summary>
        /// <param name="colorCount">The number of colors in the color table.</param>
        /// <returns>The lowest number of bits that is an acceptable indexing format that can represent sufficient indexes for the color
        /// table.</returns>
        /// <exception cref="T:System.ArgumentException">The size of the color table is too large to index for any acceptable bit depth.
        /// </exception>
        private byte TargetBitsPerPixel(int colorCount)
        {
            var minBbp = (byte)Math.Ceiling(Math.Log(colorCount, 2));
            byte candidateBbp = 1;
            while (candidateBbp < minBbp || (candidateBbp & (int)validDepths) == 0)
            {
                candidateBbp *= 2;
                if (candidateBbp > 8)
                    throw new ArgumentException("The size of the color table is too large to be indexed by any acceptable bit depth.");
            }
            return candidateBbp;
        }
        /// <summary>
        /// Adjusts the buffer sizes and transparent indexes to handle a change of color table.
        /// </summary>
        private void AdjustForChangedColorTable()
        {
            int oldImageTransparentIndex = imageTransparentIndex;
            DetermineTransparentIndexes();
            var targetBpp = TargetBitsPerPixel(colorTable.Length);

            // Ensure the buffers can handle the new table size.
            while (frameBuffer.BitsPerValue < targetBpp)
                frameBuffer.UpsizeBuffer();
            while (previousFrameBuffer.BitsPerValue < targetBpp)
                previousFrameBuffer.UpsizeBuffer();

            // Remap any old transparent indexes to their new values.
            if (oldImageTransparentIndex != imageTransparentIndex)
                for (var x = 0; x < frameBuffer.Width; x++)
                    for (var y = 0; y < frameBuffer.Height; y++)
                    {
                        if (frameBuffer.GetValue(x, y) == oldImageTransparentIndex)
                            frameBuffer.SetValue(x, y, imageTransparentIndex);
                        if (previousFrameBuffer.GetValue(x, y) == oldImageTransparentIndex)
                            previousFrameBuffer.SetValue(x, y, imageTransparentIndex);
                    }
        }
        /// <summary>
        /// Decodes a GIF file from an input stream.
        /// </summary>
        private void DecodeGif()
        {
            Iterations = 1;
            ReadGifDataStream();
            Frames = frames.ToImmutableArray();
            frames = null;
        }
        /// <summary>
        /// Reads the GIF data stream. This contains the header block, logical screen section, data sections, and trailer.
        /// </summary>
        private void ReadGifDataStream()
        {
            // <GIF Data Stream> ::= Header <Logical Screen> <Data>* Trailer
            ReadHeader();
            ReadLogicalScreen();
            ReadDataAndTrailer();
        }
        /// <summary>
        /// Reads the header block (required - one per stream).
        /// </summary>
        private void ReadHeader()
        {
            const string SignatureExpected = "GIF";
            var signature = new string(reader.ReadCharsExact(3));
            if (signature != SignatureExpected)
                throw new InvalidDataException(
                    "Invalid signature in header. Expected '{0}'. Read '{1}'.".FormatWith(SignatureExpected, signature));

            var version = new string(reader.ReadCharsExact(3));
            if (!char.IsDigit(version, 0) || !char.IsDigit(version, 1) || !char.IsLetter(version, 2))
                throw new InvalidDataException("Invalid version in header. Read '{0}'.".FormatWith(version));
        }
        /// <summary>
        /// Reads the logical screen section. This contains the logical screen descriptor and global color table.
        /// </summary>
        private void ReadLogicalScreen()
        {
            // <Logical Screen> ::= Logical Screen Descriptor [Global Color Table]
            screenDescriptor = ReadLogicalScreenDescriptor();
            Width = screenDescriptor.Width;
            Height = screenDescriptor.Height;
            ReadGlobalColorTable();
        }
        /// <summary>
        /// Reads the logical screen descriptor block. (required - one per stream).
        /// </summary>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GifLogicalScreenDescriptor"/> describing the logical screen.
        /// </returns>
        private LogicalScreenDescriptor ReadLogicalScreenDescriptor()
        {
            var logicalScreenWidth = reader.ReadUInt16();
            var logicalScreenHeight = reader.ReadUInt16();

            var packedFields = reader.ReadByte();
            var globalColorTableFlag = (packedFields & 0x80) != 0;
            var colorResolution = (byte)(((packedFields & 0x70) >> 4) + 1);
            var sortFlag = (packedFields & 0x08) >> 3 == 1;
            var globalBitsPerPixel = (packedFields & 0x07) + 1;
            var sizeOfGlobalColorTable = 1 << globalBitsPerPixel;
            var backgroundColorIndex = reader.ReadByte();
            var pixelAspectRatio = reader.ReadByte();

            return new LogicalScreenDescriptor(logicalScreenWidth, logicalScreenHeight, globalColorTableFlag, colorResolution,
                sortFlag, sizeOfGlobalColorTable, backgroundColorIndex, pixelAspectRatio);
        }
        /// <summary>
        /// Reads the global color table block. (optional - max one per stream).
        /// </summary>
        private void ReadGlobalColorTable()
        {
            if (screenDescriptor.GlobalTableExists)
            {
                SetupColorTable(screenDescriptor.GlobalTableSize);
                globalColorTable = colorTable;
            }
        }
        /// <summary>
        /// Reads the data sections (optional - no limits) and the trailer (required - one per stream).
        /// </summary>
        private void ReadDataAndTrailer()
        {
            // <GIF Data Stream> ::= Header <Logical Screen> <Data>* Trailer
            // <Data> ::= <Graphic Block> | <Special-Purpose Block>

            BlockCode blockCode;

            // <Graphic Block> ::= [Graphic Control Extension] <Graphic-Rendering Block>
            // <Special-Purpose Block> ::= Application Extension | Comment Extension
            // <Graphic-Rendering Block> ::= <Table-Based Image> | Plain Text Extension
            // <Table-Based Image> ::= Image Descriptor [Local Color Table] Image Data

            do
            {
                blockCode = (BlockCode)reader.ReadByte();
                switch (blockCode)
                {
                    case BlockCode.ImageDescriptor:
                        ReadTableBasedImage(null);
                        break;
                    case BlockCode.Extension:
                        var extensionLabel = (ExtensionLabel)reader.ReadByte();
                        switch (extensionLabel)
                        {
                            case ExtensionLabel.GraphicControl:
                                ReadGraphicBlock();
                                break;
                            case ExtensionLabel.Application:
                                ReadApplicationExtension();
                                break;
                            default:
                                SkipExtensionBlock();
                                break;
                        }
                        break;
                    case BlockCode.Trailer:
                        break;
                    default:
                        throw new InvalidDataException(
                            "Expected the start of a data section but block code did not match any known values.");
                }
            }
            while (blockCode != BlockCode.Trailer);
        }
        /// <summary>
        /// Reads a graphic block section. This contains a graphic control extension and a graphic-rendering block.
        /// </summary>
        private void ReadGraphicBlock()
        {
            // <Graphic Block> ::= [Graphic Control Extension] <Graphic-Rendering Block>
            // <Graphic-Rendering Block> ::= <Table-Based Image> | Plain Text Extension
            // <Table-Based Image> ::= Image Descriptor [Local Color Table] Image Data
            GraphicControlExtension graphicControl = ReadGraphicControlExtension();

            var blockCode = (BlockCode)reader.ReadByte();
            ExtensionLabel extensionLabel = ExtensionLabel.GraphicControl;
            if (blockCode == BlockCode.Extension)
            {
                extensionLabel = (ExtensionLabel)reader.ReadByte();
                if (extensionLabel != ExtensionLabel.PlainText)
                {
                    // We have some other extension here, we need to skip them until we meet something valid.
                    do
                    {
                        // Read block.
                        if (extensionLabel == ExtensionLabel.Application)
                            ReadApplicationExtension();
                        else
                            SkipExtensionBlock();

                        // Determine next block.
                        blockCode = (BlockCode)reader.ReadByte();
                        if (blockCode == BlockCode.Extension)
                            extensionLabel = (ExtensionLabel)reader.ReadByte();

                        // Continue until we have a valid start for a graphic-rendering block.
                    }
                    while (blockCode != BlockCode.ImageDescriptor && extensionLabel != ExtensionLabel.PlainText);
                }
            }

            // Read graphic rendering block.
            if (blockCode == BlockCode.ImageDescriptor)
            {
                // Read table based image data to finish the block.
                ReadTableBasedImage(graphicControl);
            }
            else if (extensionLabel == ExtensionLabel.PlainText)
            {
                // "Read" a plain text extension to finish the block.
                SkipExtensionBlock();
            }
            else
            {
                throw new InvalidDataException(
                    "Encountered an unexpected block code after reading the graphic control extension in a graphic block.");
            }
        }
        /// <summary>
        /// Reads a graphic control extension block (optional - max one preceding a graphic-rendering block).
        /// </summary>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.GraphicControlExtension"/> describing how a
        /// graphic-rendering block section is to be modified.</returns>
        private GraphicControlExtension ReadGraphicControlExtension()
        {
            // Read block size.
            if (reader.ReadByte() != 4)
                throw new InvalidDataException("Unexpected block length for a graphic control extension.");

            var packedFields = reader.ReadByte();
            var disposalMethod = (DisposalMethod)((packedFields & 0x1C) >> 2);
            var userInputFlag = (packedFields & 0x02) != 0;
            var transparencyFlag = (packedFields & 0x01) != 0;

            var delayTime = reader.ReadUInt16();
            var transparentColorIndex = reader.ReadByte();

            // Read block terminator.
            reader.ReadByte();

            return new GraphicControlExtension(disposalMethod, userInputFlag, transparencyFlag, delayTime, transparentColorIndex);
        }
        /// <summary>
        /// Reads an application extension block.
        /// </summary>
        private void ReadApplicationExtension()
        {
            // Read block size.
            if (reader.ReadByte() != 11)
                throw new InvalidDataException("Unexpected block length for an application extension.");

            var applicationIdentifier = new string(reader.ReadCharsExact(8));
            var applicationAuthenticationCode = new string(reader.ReadCharsExact(3));

            if (applicationIdentifier == "NETSCAPE" && applicationAuthenticationCode == "2.0")
                ReadNetscapeApplicationExtension();
            else
                SkipDataSubBlocks();
        }
        /// <summary>
        /// Reads the Netscape application extension sub-block, which defines an iteration count for the file.
        /// </summary>
        private void ReadNetscapeApplicationExtension()
        {
            // Read block size.
            if (reader.ReadByte() != 3)
                throw new InvalidDataException("Unexpected block length for the Netscape application extension.");

            // Read empty byte.
            reader.ReadByte();

            // Read iteration count.
            Iterations = reader.ReadUInt16();

            // Read sub-block terminator.
            reader.ReadByte();
        }
        /// <summary>
        /// Reads a series of data sub blocks until a terminator is encountered.
        /// </summary>
        private void SkipDataSubBlocks()
        {
            byte blockSize;
            do
            {
                blockSize = reader.ReadByte();
                reader.ReadExact(block, 0, blockSize);
            }
            while (blockSize > 0);
        }
        /// <summary>
        /// Reads an extension block.
        /// </summary>
        private void SkipExtensionBlock()
        {
            // Read block size.
            var blockSize = reader.ReadByte();

            if (blockSize == 0)
                return;

            // Read block data.
            reader.ReadExact(block, 0, blockSize);

            // Skip data sub blocks.
            SkipDataSubBlocks();
        }
        /// <summary>
        /// Reads the image descriptor block (required - one per image in the stream).
        /// </summary>
        /// <returns>A new <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.ImageDescriptor"/> describing the subframe.</returns>
        private ImageDescriptor ReadImageDescriptor()
        {
            var imageLeftPosition = reader.ReadUInt16();
            var imageTopPosition = reader.ReadUInt16();
            var imageWidth = reader.ReadUInt16();
            var imageHeight = reader.ReadUInt16();

            var packedFields = reader.ReadByte();
            var localColorTableFlag = (packedFields & 0x80) != 0;
            var interlaceFlag = (packedFields & 0x40) != 0;
            var sortFlag = (packedFields & 0x20) != 0;
            var localBitsPerPixel = (packedFields & 0x07) + 1;
            var sizeOfLocalColorTable = 1 << localBitsPerPixel;

            return new ImageDescriptor(imageLeftPosition, imageTopPosition, imageWidth, imageHeight,
                localColorTableFlag, interlaceFlag, sortFlag, sizeOfLocalColorTable);
        }
        /// <summary>
        /// Reads a table based image section. This contains an image descriptor, local color table and image data.
        /// </summary>
        /// <param name="graphicControl">A <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void ReadTableBasedImage(GraphicControlExtension graphicControl)
        {
            // <Table-Based Image> ::= Image Descriptor [Local Color Table] Image Data
            ImageDescriptor imageDescriptor = ReadImageDescriptor();
            var frame = new Rectangle(Point.Empty, Size);
            if (!frame.Contains(imageDescriptor.Subframe))
                throw new InvalidDataException("Subframe area extends outside the logical screen area.");
            if (frame == imageDescriptor.Subframe && (graphicControl == null || !graphicControl.TransparencyUsed))
                anyTransparencyUsed = false;

            if (imageDescriptor.LocalTableExists)
            {
                // Use the local color table.
                ReadLocalColorTable(imageDescriptor);
            }
            else if (globalColorTable != null && colorTable != globalColorTable)
            {
                // Use the global color table.
                colorTable = globalColorTable;
                AdjustForChangedColorTable();
            }
            else if (frameBuffer == null)
            {
                // No color tables have been defined. The image might be all-transparent. Set up a minimal buffer to start.
                imageTransparentIndex = 0;
                imageTransparentIndexRemap = 0;
                var targetBpp = TargetBitsPerPixel(1);
                frameBuffer = new DataBuffer(Width, Height, targetBpp, imageTransparentIndex);
                previousFrameBuffer = new DataBuffer(Width, Height, targetBpp, imageTransparentIndex);
            }

            // Read the image data onto the frame buffer, then create the resulting image.
            ReadImageData(imageDescriptor, graphicControl);
            CreateFrame(graphicControl);

            // Apply the disposal method.
            if (graphicControl != null)
            {
                // DisposalMethod.Undefined is undefined, if encountered it is treated as DoNotDispose.
                // DisposalMethod.DoNotDispose indicates that the buffer should remain as it is, so do nothing.
                if (graphicControl.DisposalMethod == DisposalMethod.RestoreBackground)
                {
                    // Clear the background of the subframe area.
                    frameBuffer.FillBuffer(imageTransparentIndex, imageDescriptor.Subframe);
                    anyTransparencyUsed = true;
                }
                else if (graphicControl.DisposalMethod == DisposalMethod.RestorePrevious)
                {
                    // Fill the background with the previous frame.
                    frameBuffer.FillBuffer(previousFrameBuffer, imageDescriptor.Subframe);
                }
            }

            // The previous frame is now this frame.
            previousFrameBuffer.MakeEqual(frameBuffer);
        }
        /// <summary>
        /// Reads a local color table block. (optional - max one per image descriptor).
        /// </summary>
        /// <param name="imageDescriptor">The <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.ImageDescriptor"/> which describes
        /// the table, and to which any table will belong.</param>
        private void ReadLocalColorTable(ImageDescriptor imageDescriptor)
        {
            if (imageDescriptor.LocalTableExists)
                SetupColorTable(imageDescriptor.LocalTableSize);
        }
        /// <summary>
        /// Reads image data onto the frame buffer. This is the LZW compressed information about the pixels in the subframe.
        /// </summary>
        /// <param name="imageDescriptor">An <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.ImageDescriptor"/> describing the
        /// subframe.</param>
        /// <param name="graphicControl">A <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void ReadImageData(ImageDescriptor imageDescriptor, GraphicControlExtension graphicControl)
        {
            var lzwMinimumCodeSize = reader.ReadByte();

            #region Initialize decoder.
            // Image pixel position data.
            var left = imageDescriptor.Subframe.Left;
            var top = imageDescriptor.Subframe.Top;
            // Iterator that will allow the subframe region to be traversed efficiently.
            DataBuffer.Iterator iterator = frameBuffer.GetIterator(left, top);
            // Index used for the transparent pixel, or -1 is transparency is not in use.
            var transparentIndex = graphicControl != null && graphicControl.TransparencyUsed ? graphicControl.TransparentIndex : -1;
            // Interlacing fields.
            var interlacePass = 1;
            var yIncrement = imageDescriptor.Interlaced ? 8 : 1;

            // This array is used as a stack to store resulting pixel values. These values are the indexes in the color palette.
            var pixelStack = new byte[MaxCodeWords];
            // Variable for the next available index in the stack.
            var stackIndex = 0;

            // Dictionary maintaining codewords.
            var dictionary = new LzwDictionary(lzwMinimumCodeSize);
            // The size of codes being read in, in bits.
            var codeSize = lzwMinimumCodeSize + 1;
            // The mask used to get the current code from the bit buffer.
            var codeMask = (1 << codeSize) - 1;

            // Variables managing the data block buffer from which data is read in.
            var bitsBuffered = 0;
            var bitBuffer = 0;
            var blockIndex = 0;
            var bytesLeftInBlock = 0;
            #endregion

            #region Decode GIF pixel stream.
            var skipDataSubBlocks = true;
            var subframeComplete = false;
            while (true)
            {
                // Buffer enough bits to read a code.
                if (BufferBits(codeSize, ref bitsBuffered, ref bitBuffer, ref bytesLeftInBlock, ref blockIndex))
                {
                    // No need to skip remaining blocks, since we happened to read the terminator ourselves.
                    skipDataSubBlocks = false;
                    break;
                }

                // Get the next code from our bit buffer.
                var code = (short)(bitBuffer & codeMask);
                bitBuffer >>= codeSize;
                bitsBuffered -= codeSize;

                // Process the code, pushing the codeword onto the pixel stack.
                switch (dictionary.ProcessCode(code, pixelStack, ref stackIndex))
                {
                    case LzwDictionary.Result.EndOfInformation:
                        subframeComplete = true;
                        break;
                    case LzwDictionary.Result.Cleared:
                        codeSize = lzwMinimumCodeSize + 1;
                        codeMask = (1 << codeSize) - 1;
                        break;
                    case LzwDictionary.Result.FirstCode:
                        continue;
                    case LzwDictionary.Result.PushedCodeword:
                        // Increase the code size if the number of available codes now exceeds the number addressable by the current size.
                        if (dictionary.Available < MaxCodeWords && (dictionary.Available & codeMask) == 0)
                        {
                            codeSize++;
                            codeMask += dictionary.Available;
                        }
                        break;
                }

                // TODO: A possible performance improvement here is to remove the need for the intermediate pixel stack.
                // If the length of the codewords was stored, then the index into the frame buffer could be advanced by the length of the
                // codeword and then the codeword enumerated with values being directly processed by the ApplyStackToFrame method. The
                // method would have to be able to traverse the frame buffer backwards, and in particular reverse the interlacing too.
                // Fairly complex and not vital, but might shave 5-15% off decoding time.

                // Flush the pixel stack and write resulting pixel values to the frame buffer.
                if (!subframeComplete)
                    subframeComplete = ApplyStackToFrame(pixelStack, ref stackIndex,
                        imageDescriptor, ref left, ref top, ref interlacePass, ref yIncrement,
                        iterator, transparentIndex);
            }
            #endregion

            // Read block terminator (and any trailing sub-blocks, though there should not be any).
            if (skipDataSubBlocks)
                SkipDataSubBlocks();
        }
        /// <summary>
        /// Buffers bit until sufficient bits are available to process a code. If required, will read the next data block from the stream.
        /// </summary>
        /// <param name="codeSize">The number of bits currently required for codes.</param>
        /// <param name="bitsBuffered">The number of bits currently buffered.</param>
        /// <param name="bitBuffer">The bit buffer.</param>
        /// <param name="bytesLeftInBlock">The number of bytes left in the data block.</param>
        /// <param name="blockIndex">The index into the current data block from which bits are being streamed.</param>
        /// <returns>Returns true if the block terminator was read, and the image stream has therefore ended; otherwise, false.</returns>
        private bool BufferBits(int codeSize, ref int bitsBuffered, ref int bitBuffer, ref int bytesLeftInBlock, ref int blockIndex)
        {
            while (bitsBuffered < codeSize)
            {
                // Read in a new data block if we exhausted the block buffer.
                if (bytesLeftInBlock == 0)
                {
                    // Get size of the next data block.
                    bytesLeftInBlock = reader.ReadByte();
                    // If we happen to read the block terminator, we are done reading image data.
                    if (bytesLeftInBlock == 0)
                        return true;
                    // Read the data block of given size.
                    reader.ReadExact(block, 0, bytesLeftInBlock);
                    blockIndex = 0;
                }
                bitBuffer += block[blockIndex++] << bitsBuffered;
                bitsBuffered += 8;
                bytesLeftInBlock--;
            }
            return false;
        }
        /// <summary>
        /// Applies the current stack of pixels onto the frame buffer.
        /// </summary>
        /// <param name="pixelStack">A stack of pixel indexes to apply to the frame buffer.</param>
        /// <param name="stackIndex">The index to the first free element in the stack.</param>
        /// <param name="imageDescriptor">An <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.ImageDescriptor"/> describing the
        /// subframe.</param>
        /// <param name="left">A variable tracking the x-coordinate on the frame.</param>
        /// <param name="top">A variable tracking the y-coordinate on the frame.</param>
        /// <param name="interlacePass">A variable tracking the current interlacing pass.</param>
        /// <param name="yIncrement">A variable tracking the amount to increment the y-coordinate by for interlaced passes.</param>
        /// <param name="iterator">An iterator for setting values within the frame buffer.</param>
        /// <param name="transparentIndex">The index for transparent pixels.</param>
        /// <returns>Returns true if the frame buffer has been filled.</returns>
        private bool ApplyStackToFrame(byte[] pixelStack, ref int stackIndex,
            ImageDescriptor imageDescriptor, ref int left, ref int top, ref int interlacePass, ref int yIncrement,
            DataBuffer.Iterator iterator, int transparentIndex)
        {
            var right = imageDescriptor.Subframe.Right;
            while (stackIndex > 0)
            {
                // Apply pixel to our buffers.
                var pixel = pixelStack[--stackIndex];
                if (pixel != transparentIndex)
                    ApplyPixelToFrame(iterator, pixel);

                if (++left < right)
                {
                    // Move right one pixel.
                    iterator.IncrementX();
                }
                else
                {
                    // Move to next row. If we're not interlacing this just means the next row.
                    // If interlacing, we must fill in every 8th row, then every 4th, then every 2nd then every other row.
                    left = imageDescriptor.Subframe.Left;
                    top += yIncrement;
                    iterator.SetPosition(left, top);

                    // If we reached the end of this interlacing pass, go back to the top and fill in every row between the current rows.
                    var bottom = imageDescriptor.Subframe.Bottom;
                    if (imageDescriptor.Interlaced && top >= bottom)
                    {
                        #region Choose next interlacing line.
                        do
                        {
                            interlacePass++;
                            switch (interlacePass)
                            {
                                case 2:
                                    top = 4;
                                    break;
                                case 3:
                                    top = 2;
                                    yIncrement = 4;
                                    break;
                                case 4:
                                    top = 1;
                                    yIncrement = 2;
                                    break;
                                case 5:
                                    return true;
                            }
                        }
                        while (top >= bottom);
                        #endregion
                    }

                    if (top >= bottom)
                        return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Uses a value from the subframe and applies it onto the frame buffer.
        /// </summary>
        /// <param name="iterator">An iterator for the frameBuffer via which the value will be set.</param>
        /// <param name="pixel">The value to be applied.</param>
        private void ApplyPixelToFrame(DataBuffer.Iterator iterator, byte pixel)
        {
            // Check the pixel does not conflict with the choice of transparency index.
            if (pixel != imageTransparentIndex)
            {
                // No conflict, so check the value is in range and set the pixel.
                if (pixel >= colorTable.Length)
                    throw new InvalidDataException(
                        "Indexed value of {0} was larger that the maximum of {1}.".FormatWith(pixel, colorTable.Length - 1));
                iterator.SetValue(pixel);
            }
            else if (imageTransparentIndex != imageTransparentIndexRemap)
            {
                // Use the backup index (whose color value is the same) if one exists.
                iterator.SetValue(imageTransparentIndexRemap);
            }
            else
            {
                // There is no backup index. This happens when the table was at size 256 and there were no duplicate colors.
                throw new NotSupportedException(
                    "Attempted to decode an image using 256 RGB colors and transparency. " +
                    "If transparency is required, this decoder only supports 255 RGB colors.");
            }
        }
        /// <summary>
        /// Creates the frame image using the current buffer.
        /// </summary>
        /// <param name="graphicControl">A <see cref="T:DesktopSprites.SpriteManagement.GifDecoder`1.GraphicControlExtension"/> specifying
        /// how the value from the subframe is to be applied. This is optional.</param>
        private void CreateFrame(GraphicControlExtension graphicControl)
        {
            // Make a copy of the current frame buffer and color table, since we'll be reusing those arrays for later frames.
            var bufferCopy = new byte[frameBuffer.Buffer.Length];
            Array.Copy(frameBuffer.Buffer, bufferCopy, frameBuffer.Buffer.Length);
            RgbColor[] tableCopy;
            if (colorTable != null)
            {
                tableCopy = new RgbColor[colorTable.Length];
                Array.Copy(colorTable, tableCopy, colorTable.Length);
            }
            else
            {
                tableCopy = new RgbColor[0];
            }

            // Create the frame image, and then the frame itself.
            var frameTransparentIndex = anyTransparencyUsed ? imageTransparentIndex : (byte?)null;
            T frame =
                createFrame(bufferCopy, tableCopy, frameTransparentIndex,
                frameBuffer.Stride, frameBuffer.Width, frameBuffer.Height, frameBuffer.BitsPerValue);
            var delay = graphicControl != null ? graphicControl.Delay : 0;
            var newFrame = new GifFrame<T>(frame, delay, tableCopy, frameTransparentIndex);
            frames.Add(newFrame);
            Duration += newFrame.Duration;
        }
    }

    /// <summary>
    /// Defines a single frame in a GIF animation.
    /// </summary>
    /// <typeparam name="T">The type of the frame image.</typeparam>
    public sealed class GifFrame<T>
    {
        /// <summary>
        /// Gets the image for this frame.
        /// </summary>
        public T Image { get; private set; }
        /// <summary>
        /// Gets the duration of this frame, in milliseconds.
        /// </summary>
        public int Duration { get; private set; }
        /// <summary>
        /// Gets the size of the color table.
        /// </summary>
        public int ColorTableSize
        {
            get { return colorTable.Length; }
        }
        /// <summary>
        /// The color table used for the image.
        /// </summary>
        private readonly RgbColor[] colorTable;
        /// <summary>
        /// The index of the transparent color in the <see cref="F:DesktopSprites.SpriteManagement.GifFrame`1.colorTable"/>, or null to
        /// indicate no transparent color.
        /// </summary>
        private readonly byte? transparentIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.SpriteManagement.GifFrame`1"/> class.
        /// </summary>
        /// <param name="frame">The image for this frame.</param>
        /// <param name="duration">The duration of this frame, in milliseconds.</param>
        /// <param name="colorTable">The color table used for the image.</param>
        /// <param name="transparentIndex">The index of the transparent color in <paramref name="colorTable"/>, or null if there is no
        /// transparent color.</param>
        internal GifFrame(T frame, int duration, RgbColor[] colorTable, byte? transparentIndex)
        {
            Image = frame;
            Duration = duration;
            this.colorTable = colorTable;
            this.transparentIndex = transparentIndex;
        }
        /// <summary>
        /// Gets a copy of the color table used for the image.
        /// </summary>
        /// <returns>A copy of the color table used for the image.</returns>
        public ArgbColor[] GetColorTable()
        {
            var colors = new ArgbColor[ColorTableSize];
            for (var i = 0; i < ColorTableSize; i++)
                colors[i] = new ArgbColor(255, colorTable[i]);

            if (transparentIndex != null)
                colors[transparentIndex.Value] = new ArgbColor(0, colorTable[transparentIndex.Value]);

            return colors;
        }
    }
}
