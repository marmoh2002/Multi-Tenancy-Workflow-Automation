using System.Collections.Generic;

namespace WorkflowAutomation.Workflows.Core.Tests;

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> list, T item)
    {
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < list.Count; i++)
        {
            if (comparer.Equals(list[i], item))
                return i;
        }
        return -1;
    }
}