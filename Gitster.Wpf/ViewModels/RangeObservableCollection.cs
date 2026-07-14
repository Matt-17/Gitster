using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Gitster.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be replaced wholesale with a single
/// Reset notification instead of N per-item events — essential for rebuilding a 7,000-row
/// commit list without flooding the dispatcher (plan A0).
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        var replacement = items is IList<T> list ? list : items.ToList();
        if (Items.Count == replacement.Count)
        {
            var comparer = EqualityComparer<T>.Default;
            var unchanged = true;
            for (var i = 0; i < replacement.Count; i++)
            {
                if (!comparer.Equals(Items[i], replacement[i]))
                {
                    unchanged = false;
                    break;
                }
            }

            if (unchanged)
                return;
        }

        Items.Clear();
        foreach (var item in replacement)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void AddRange(IEnumerable<T> items)
    {
        var additions = items is IList<T> list ? list : items.ToList();
        if (additions.Count == 0)
            return;

        foreach (var item in additions)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
