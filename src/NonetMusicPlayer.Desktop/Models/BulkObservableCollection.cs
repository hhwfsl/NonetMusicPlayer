using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NonetMusicPlayer.Desktop.Models;

/// <summary>替换快照时只发送一次重置事件，避免逐首通知造成界面卡顿。</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
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
