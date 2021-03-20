﻿namespace DesktopSprites.Core
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Defines extension methods for <see cref="T:System.String"/>.
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Replaces the format item in a specified string with the string representation of a corresponding object in a specified array.
        /// The culture for the current thread is used to apply culture-specific formatting information.
        /// </summary>
        /// <param name="format">A composite format string.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <returns>A copy of <paramref name="format"/> in which the format items have been replaced by the string representation of the
        /// corresponding objects in <paramref name="args"/>.</returns>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="format"/> is null.-or-<paramref name="args"/> is null.
        /// </exception>
        /// <exception cref="T:System.FormatException"><paramref name="format"/> is invalid.-or-The index of a format item is less than
        /// zero, or greater than or equal to the length of the <paramref name="args"/> array.</exception>
        public static string FormatWith(this string format, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, format, args);
        }

        /// <summary>
        /// Returns a string array that contains the substrings in this string that are delimited by elements of a specified string array.
        /// Text elements can be qualified so their content is treated as plain text (i.e. the delimiter is ignored).
        /// </summary>
        /// <param name="source">The source string to separate.</param>
        /// <param name="separators">The characters on which strings are to be separated.</param>
        /// <param name="qualifiers">An array of opening and closing qualifier character pairs. The array must be of dimensions [n,2],
        /// where n is the number of pairs.</param>
        /// <param name="options"><see cref="T:System.StringSplitOptions.RemoveEmptyEntries"/> to omit empty array elements from the array
        /// returned; or <see cref="T:System.StringSplitOptions.None"/> to include empty array elements in the array returned.</param>
        /// <returns>An array whose elements contain the substrings in this string that are delimited by the separator. Qualified text
        /// substrings do not retain their enclosing qualifiers.</returns>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="source"/> is null.</exception>
        /// <exception cref="T:System.ComponentModel.InvalidEnumArgumentException"><paramref name="options"/> is invalid.</exception>
        /// <exception cref="T:System.ArgumentException">The dimensions of the <paramref name="qualifiers"/> array are invalid.-or-The
        /// characters used as separators and opening qualifiers as a combined group are not unique from each other.-or-
        /// <paramref name="source"/> contains an opening qualifier character with no matching closing qualifier.</exception>
        /// <remarks>This method is not suitable for parsing CSV files, which generally require that two consecutive instances of a
        /// qualifying character be treated as a literal of that character, which this method does not support. If only one qualifying pair
        /// in used, the opening qualifier character cannot appear inside a qualified string. If multiple pairs are used, one can use an
        /// opening qualifier character within a qualified string by qualifying it with a different pair.</remarks>
        public static string[] SplitQualified(this string source, char[] separators, char[,] qualifiers, StringSplitOptions options)
        {
            Argument.EnsureNotNull(source, nameof(source));
            if (options < StringSplitOptions.None || options > StringSplitOptions.RemoveEmptyEntries)
                throw new InvalidEnumArgumentException("options", (int)options, typeof(StringSplitOptions));

            if (separators == null)
                separators = new char[0];

            if (qualifiers == null)
                qualifiers = new char[0, 2];

            if (qualifiers.GetLength(1) != 2)
                throw new ArgumentException("The dimensions of the qualifiers array must be [n,2]. " +
                    "The two characters are the opening and closing qualifier pair. You may have n pairs of qualifiers.", nameof(qualifiers));

            var openingQualifiers = new char[qualifiers.GetLength(0)];
            for (var i = 0; i < qualifiers.GetLength(0); i++)
                openingQualifiers[i] = qualifiers[i, 0];

            if (separators.Concat(openingQualifiers).Count() != separators.Union(openingQualifiers).Count())
                throw new ArgumentException("Separator and opening qualifier characters must all be distinct from each other.");

            // Handle the empty string (as a StringBuilder cannot be initialized with zero capacity).
            if (source.Length == 0)
                if (options == StringSplitOptions.None)
                    return new string[1] { source };
                else
                    return new string[0];

            // Default capacity is the larger of 16 or 1/8th of the source length, but no more than the source length.
            var capacity = Math.Min(source.Length, Math.Max(16, (int)Math.Ceiling(source.Length / 8f)));
            var segment = new StringBuilder(capacity, source.Length);
            var segments = new List<string>();
            var index = 0;

            while (index <= source.Length)
            {
                // Determine the positions of the next separator and qualifier.
                var seperatorIndex = source.IndexOfAny(separators, index);
                var qualifierIndex = source.IndexOfAny(openingQualifiers, index);
                // Specify the index of characters that couldn't be located to be the end of the source string.
                if (seperatorIndex == -1)
                    seperatorIndex = source.Length;
                if (qualifierIndex == -1)
                    qualifierIndex = source.Length;

                if (seperatorIndex <= qualifierIndex)
                {
                    // If seperatorIndex < qualifierIndex, we encountered a separator in the source.
                    // If seperatorIndex == qualifierIndex, we reached the end of the source.
                    // We can complete the next segment by taking the substring from our last position up to seperatorIndex, which is the
                    // location of the next separator or else the end of the string.
                    segment.Append(source, index, seperatorIndex - index);
                    index = seperatorIndex + 1;

                    // If the segment is empty, we only save it if requested.
                    if (segment.Length > 0 || options == StringSplitOptions.None)
                        segments.Add(segment.ToString());

                    segment.Clear();
                }
                else
                {
                    // We encountered a qualifier, we need to find the matching closing qualifier.
                    var openingQualifier = source[qualifierIndex];
                    var closingQualifier = '\0';
                    // Get the qualifier that closes this pair.
                    for (var i = 0; i < qualifiers.Length; i++)
                        if (openingQualifier == qualifiers[i, 0])
                        {
                            closingQualifier = qualifiers[i, 1];
                            break;
                        }

                    // Append the text up to the opening qualifier.
                    segment.Append(source, index, qualifierIndex - index);
                    // Skip over opening qualifier character.
                    index = qualifierIndex + 1;
                    // Find closing qualifier.
                    qualifierIndex = source.IndexOf(closingQualifier, index);
                    if (qualifierIndex == -1)
                        throw new ArgumentException(
                            "Source string contains qualified text with no closing qualifier. The opening qualifier '" + openingQualifier +
                            "' was encountered at position " + (index - 1) + ", but the closing qualifier '" + closingQualifier +
                            "' could not found at a subsequent position.");
                    // Append the text up to the closing qualifier.
                    segment.Append(source, index, qualifierIndex - index);
                    // Skip over closing qualifier character.
                    index = qualifierIndex + 1;
                }
            }

            return segments.ToArray();
        }
    }
}
