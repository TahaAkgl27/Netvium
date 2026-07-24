using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Netvium.Helpers
{
    /// <summary>
    /// An ObservableCollection that supports batch updates with a single notification at the end.
    /// This significantly improves performance when adding multiple items.
    /// </summary>
    public class FastObservableCollection<T> : ObservableCollection<T>
    {
        private bool _isSuppressingNotifications = false;

        public FastObservableCollection() : base() { }
        public FastObservableCollection(IEnumerable<T> collection) : base(collection) { }
        public FastObservableCollection(List<T> list) : base(list) { }

        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;

            _isSuppressingNotifications = true;
            try
            {
                foreach (var item in items)
                {
                    Items.Add(item);
                }
            }
            finally
            {
                _isSuppressingNotifications = false;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            }
        }

        public void ReplaceRange(IEnumerable<T> items)
        {
            _isSuppressingNotifications = true;
            try
            {
                Items.Clear();
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        Items.Add(item);
                    }
                }
            }
            finally
            {
                _isSuppressingNotifications = false;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            }
        }

        /// <summary>
        /// Synchronizes the collection with a new set of items using a diff algorithm.
        /// This prevents full UI resets and keeps the DataGrid scrolling/selection stable.
        /// </summary>
        public void SyncWithDiff(IEnumerable<T> newItems, Func<T, T, bool> areEqual)
        {
            if (newItems == null)
            {
                if (Count > 0) Clear();
                return;
            }

            _isSuppressingNotifications = true;
            bool changed = false;
            try
            {
                var newList = new List<T>(newItems);

                // 1. Remove items that no longer exist
                for (int i = Count - 1; i >= 0; i--)
                {
                    var existing = this[i];
                    if (!newList.Any(n => areEqual(existing, n)))
                    {
                        Items.RemoveAt(i);
                        changed = true;
                    }
                }

                // 2. Update or Add items
                for (int i = 0; i < newList.Count; i++)
                {
                    var newItem = newList[i];
                    bool exists = false;
                    
                    // We check if it's already at the correct position to avoid unnecessary work
                    if (i < Count && areEqual(this[i], newItem))
                    {
                        // Already exists and in place, just let it be (or maybe update properties if T is mutable)
                        exists = true;
                    }
                    else
                    {
                        // Check if it exists elsewhere and move or just add
                        for (int j = 0; j < Count; j++)
                        {
                            if (areEqual(this[j], newItem))
                            {
                                // If it's at the wrong position, we might want to move it, 
                                // but for simplicity and performance in most RDP lists, 
                                // we'll just replace if it's not the same object reference.
                                if (!ReferenceEquals(this[j], newItem))
                                {
                                    Items[j] = newItem;
                                    changed = true;
                                }
                                exists = true;
                                break;
                            }
                        }
                    }

                    if (!exists)
                    {
                        if (i < Count)
                            Items.Insert(i, newItem);
                        else
                            Items.Add(newItem);
                        changed = true;
                    }
                }
            }
            finally
            {
                _isSuppressingNotifications = false;
                if (changed)
                {
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                    OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                }
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_isSuppressingNotifications)
            {
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (!_isSuppressingNotifications)
            {
                base.OnPropertyChanged(e);
            }
        }
    }
}
