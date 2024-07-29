#region Copyright 2021-2022 C. Augusto Proiete & Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

using System.Collections;
using System.Collections.Generic;

// ReSharper disable once CheckNamespace
namespace DevLocker.Tools.AssetManagement.NaturalStringExtensions
{
    /// <summary>
    /// A comparer to compare any two strings using natural sorting.
    /// </summary>
    public interface INaturalStringComparer : IComparer<string>, IEqualityComparer<string>, IComparer, IEqualityComparer
    {
        /// <summary>
        /// Compares two strings and returns a value indicating whether one is less than the other.
        /// </summary>
        /// <param name="left">The first string to compare.</param>
        /// <param name="right">The second string to compare.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="left" /> is less than <paramref name="right" />. Otherwise <see langword="false"/>.
        /// </returns>
        bool IsLessThan(string left, string right);

        /// <summary>
        /// Compares two strings and returns a value indicating whether one is less than or equal to the other.
        /// </summary>
        /// <param name="left">The first string to compare.</param>
        /// <param name="right">The second string to compare.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="left" /> is less or equal than <paramref name="right" />. Otherwise <see langword="false"/>.
        /// </returns>
        bool IsLessThanOrEqual(string left, string right);

        /// <summary>
        /// Compares two strings and returns a value indicating whether they are equal.
        /// </summary>
        /// <param name="left">The first string to compare.</param>
        /// <param name="right">The second string to compare.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="left" /> is equal to <paramref name="right" />. Otherwise <see langword="false"/>.
        /// </returns>
        bool IsEqual(string left, string right);

        /// <summary>
        /// Compares two strings and returns a value indicating whether one is greater than the other.
        /// </summary>
        /// <param name="left">The first string to compare.</param>
        /// <param name="right">The second string to compare.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="left" /> is greater than <paramref name="right" />. Otherwise <see langword="false"/>.
        /// </returns>
        bool IsGreaterThan(string left, string right);

        /// <summary>
        /// Compares two strings and returns a value indicating whether one is greater than or equal to the other.
        /// </summary>
        /// <param name="left">The first string to compare.</param>
        /// <param name="right">The second string to compare.</param>
        /// <returns>
        /// <see langword="true"/> when <paramref name="left" /> is less or equal than <paramref name="right" />. Otherwise <see langword="false"/>.
        /// </returns>
        bool IsGreaterThanOrEqual(string left, string right);
    }
}
