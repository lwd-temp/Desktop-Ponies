﻿namespace DesktopSprites.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DesktopSprites.Core;

    /// <summary>
    /// Wraps a mutable collection in order to provide a read-only interface.
    /// </summary>
    public static class ReadOnlyList
    {
        /// <summary>
        /// Creates a read-only wrapper around a collection.
        /// </summary>
        /// <typeparam name="T">The type of the elements.</typeparam>
        /// <param name="list">The mutable collection to wrap.</param>
        /// <returns>A <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> that wraps the specified collection.</returns>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="list"/> is null.</exception>
        public static ReadOnlyList<T> AsReadOnly<T>(this IList<T> list)
        {
            if (list is ReadOnlyList<T>)
                return (ReadOnlyList<T>)list;
            return new ReadOnlyList<T>(list);
        }
    }

    /// <summary>
    /// Wraps a mutable collection in order to provide a read-only interface.
    /// </summary>
    /// <typeparam name="T">The type of the elements.</typeparam>
    [System.Diagnostics.DebuggerDisplay("Count = {Count}")]
    [System.Diagnostics.DebuggerTypeProxy(typeof(ReadOnlyList<>.DebugView))]
    public struct ReadOnlyList<T> : IList<T>
    {
        /// <summary>
        /// The wrapped list.
        /// </summary>
        private IList<T> list;
        /// <summary>
        /// Initializes a new instance of the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structure around the specified
        /// collection.
        /// </summary>
        /// <param name="list">The mutable collection to wrap.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="list"/> is null.</exception>
        public ReadOnlyList(IList<T> list)
        {
            this.list = Argument.EnsureNotNull(list, nameof(list));
        }
        /// <summary>
        /// Gets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <returns>The element at the specified index.</returns>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the
        /// <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.</exception>
        public T this[int index]
        {
            get { return list[index]; }
        }
        /// <summary>
        /// Gets the number of elements contained in the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        public int Count
        {
            get { return list.Count; }
        }
        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<T> GetEnumerator()
        {
            return list.GetEnumerator();
        }
        /// <summary>
        /// Determines the index of a specific item in the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.</param>
        /// <returns>The index of item if found in the list; otherwise, -1.</returns>
        public int IndexOf(T item)
        {
            return list.IndexOf(item);
        }
        /// <summary>
        /// Determines whether the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.</param>
        /// <returns>Returns true if item is found in the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>; otherwise, false.
        /// </returns>
        public bool Contains(T item)
        {
            return list.Contains(item);
        }
        /// <summary>
        /// Copies the elements of the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> to an <see cref="T:System.Array"/>,
        /// starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from
        /// <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>. The <see cref="T:System.Array"/> must have zero-based indexing.
        /// </param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="array"/> is null.</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is less than 0.</exception>
        /// <exception cref="T:System.ArgumentException">The number of elements in the source
        /// <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> is greater than the available space from
        /// <paramref name="arrayIndex"/> to the end of the destination array.</exception>
        public void CopyTo(T[] array, int arrayIndex)
        {
            list.CopyTo(array, arrayIndex);
        }
        /// <summary>
        /// Tests whether two specified <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structures are equivalent.
        /// </summary>
        /// <param name="left">The <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> that is to the left of the equality operator.
        /// </param>
        /// <param name="right">The <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> that is to the right of the equality
        /// operator.</param>
        /// <returns>Returns true if the two <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structures are equal; otherwise,
        /// false.</returns>
        public static bool operator ==(ReadOnlyList<T> left, ReadOnlyList<T> right)
        {
            return Equals(left.list, right.list);
        }
        /// <summary>
        /// Tests whether two specified <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structures are different.
        /// </summary>
        /// <param name="left">The <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> that is to the left of the inequality
        /// operator.</param>
        /// <param name="right">The <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> that is to the right of the inequality
        /// operator.</param>
        /// <returns>Returns true if the two <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structures are different;
        /// otherwise, false.</returns>
        public static bool operator !=(ReadOnlyList<T> left, ReadOnlyList<T> right)
        {
            return !(left == right);
        }
        /// <summary>
        /// Tests whether the specified object is a <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structure and is equivalent
        /// to this <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structure.
        /// </summary>
        /// <param name="obj">The object to test.</param>
        /// <returns>Returns true if <paramref name="obj"/> is a <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structure
        /// equivalent to this <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structure; otherwise, false.</returns>
        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is ReadOnlyList<T>))
                return false;
            return this == (ReadOnlyList<T>)obj;
        }
        /// <summary>
        /// Returns a hash code for this <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> structure.
        /// </summary>
        /// <returns>An integer value that specifies the hash code for this <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </returns>
        public override int GetHashCode()
        {
            return unchecked(list.GetHashCode() * 7);
        }
        /// <summary>
        /// Returns a new <see cref="T:System.NotSupportedException"/> with a message about the collection being read-only.
        /// </summary>
        /// <returns>A new <see cref="T:System.NotSupportedException"/> with a message about the collection being read-only.</returns>
        private static NotSupportedException ReadOnlyException()
        {
            return new NotSupportedException("Collection is read-only.");
        }
        /// <summary>
        /// Not supported by <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        /// <param name="index">The parameter is not used.</param>
        /// <param name="item">The parameter is not used.</param>
        /// <exception cref="T:System.NotSupportedException">Thrown when the method is invoked.</exception>
        void IList<T>.Insert(int index, T item)
        {
            throw ReadOnlyException();
        }
        /// <summary>
        /// Not supported by <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        /// <param name="index">The parameter is not used.</param>
        /// <exception cref="T:System.NotSupportedException">Thrown when the method is invoked.</exception>
        void IList<T>.RemoveAt(int index)
        {
            throw ReadOnlyException();
        }
        /// <summary>
        /// Gets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <returns>The element at the specified index. A set operation throws a <see cref="T:System.NotSupportedException"/>.</returns>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the
        /// <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.</exception>
        /// <exception cref="T:System.NotSupportedException">The operation is set.</exception>
        T IList<T>.this[int index]
        {
            get { return this[index]; }
            set { throw ReadOnlyException(); }
        }
        /// <summary>
        /// Not supported by <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        /// <param name="item">The parameter is not used.</param>
        /// <exception cref="T:System.NotSupportedException">Thrown when the method is invoked.</exception>
        void ICollection<T>.Add(T item)
        {
            throw ReadOnlyException();
        }
        /// <summary>
        /// Not supported by <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException">Thrown when the method is invoked.</exception>
        void ICollection<T>.Clear()
        {
            throw ReadOnlyException();
        }
        /// <summary>
        /// Gets a value indicating whether the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/> is read-only. Returns true.
        /// </summary>
        bool ICollection<T>.IsReadOnly
        {
            get { return true; }
        }
        /// <summary>
        /// Not supported by <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        /// <param name="item">The parameter is not used.</param>
        /// <returns>The method does not return.</returns>
        /// <exception cref="T:System.NotSupportedException">Thrown when the method is invoked.</exception>
        bool ICollection<T>.Remove(T item)
        {
            throw ReadOnlyException();
        }
        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #region DebugView class
        /// <summary>
        /// Provides a debugger view for a <see cref="T:DesktopSprites.Collections.ReadOnlyList`1"/>.
        /// </summary>
        private sealed class DebugView
        {
            /// <summary>
            /// The list for which an alternate view is being provided.
            /// </summary>
            private ReadOnlyList<T> readOnlyList;

            /// <summary>
            /// Initializes a new instance of the <see cref="T:DesktopSprites.Collections.ReadOnlyList`1.DebugView"/> class.
            /// </summary>
            /// <param name="readOnlyList">The list to proxy.</param>
            public DebugView(ReadOnlyList<T> readOnlyList)
            {
                this.readOnlyList = Argument.EnsureNotNull(readOnlyList, nameof(readOnlyList));
            }

            /// <summary>
            /// Gets a view of the items in the list.
            /// </summary>
            [System.Diagnostics.DebuggerBrowsable(System.Diagnostics.DebuggerBrowsableState.RootHidden)]
            public T[] Items
            {
                get { return readOnlyList.list as T[] ?? readOnlyList.list.ToArray(); }
            }
        }
        #endregion
    }
}
