using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NonetMusicPlayer.Desktop.Models;

/// <summary>替换快照时只发送一次重置事件，避免逐首通知造成界面卡顿。</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>追加分页只发送 Add，不发送 Reset，保留虚拟列表的滚动锚点和现有容器。</summary>
    public void AddRange(IEnumerable<T> source)
    {
        var added = source.ToArray(); if (added.Length == 0) return;
        var index = Count; foreach (var item in added) Items.Add(item);
        OnPropertyChanged(new(nameof(Count))); OnPropertyChanged(new("Item[]"));
        OnCollectionChanged(new(NotifyCollectionChangedAction.Add, added, index));
    }
    /// <summary>小范围顺序变化保留现有容器；大量增删或移动回退到一次快照重置。</summary>
    public void SynchronizeOrder(IEnumerable<T> source)
    {
        var snapshot = source.Distinct().ToArray();
        if (Math.Abs(snapshot.Length - Count) > 64) { ReplaceAll(snapshot); return; }
        var retained = snapshot.ToHashSet(); var changes = 0;
        for (var i = Count - 1; i >= 0; i--) if (!retained.Contains(this[i]))
        {
            if (++changes > 64) { ReplaceAll(snapshot); return; }
            RemoveAt(i);
        }
        for (var i = 0; i < snapshot.Length; i++)
        {
            if (i < Count && EqualityComparer<T>.Default.Equals(this[i], snapshot[i])) continue;
            if (++changes > 64) { ReplaceAll(snapshot); return; }
            var oldIndex = IndexOf(snapshot[i]);
            if (oldIndex >= 0) Move(oldIndex, i); else Insert(i, snapshot[i]);
        }
        while (Count > snapshot.Length) RemoveAt(Count - 1);
    }
    public void ReplaceAll(IEnumerable<T> source)
    {
        var snapshot = source.ToArray();
        // 相同数据不发送 Reset；无关的侧栏操作不能令虚拟化列表丢失滚动锚点。
        if (snapshot.Length == Count && snapshot.SequenceEqual(this)) return;
        Items.Clear();
        foreach (var item in snapshot) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
