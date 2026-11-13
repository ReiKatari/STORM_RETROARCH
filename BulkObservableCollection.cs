using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace StormRetroArch;

/// <summary>
/// High-performance ObservableCollection that supports bulk ReplaceAll and AddRange
/// with a single Reset notification instead of thousands of per-item layout invalidations.
/// </summary>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection() : base()
    {
    }

    public BulkObservableCollection(IEnumerable<T> collection) : base(collection)
    {
    }

    /// <summary>
    /// Clears existing items and populates from the given sequence, raising a single Reset notification.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Appends multiple items and raises a single Reset notification if any items were added.
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        CheckReentrancy();
        int countBefore = Items.Count;
        foreach (var item in items)
        {
            Items.Add(item);
        }

        if (Items.Count == countBefore) return;

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
