using System.Collections.ObjectModel;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace OMniX.WinUI.Helpers;

/// <summary>
///     二开：线程安全的 ObservableCollection。
///
///     这个应用的 VM 层大量使用 ConfigureAwait(false) + 文件监听/定时刷新，
///     导致绑定界面的集合经常在线程池线程上被改动，
///     WinUI 遇到这种情况不走托管异常通道，直接把进程杀掉（0xc000027b / 0x8001010E）。
///
///     用这个类之后，不管谁在哪个线程增删，集合变更通知一定在 UI 线程发出。
/// </summary>
public class UiObservableCollection<T> : ObservableCollection<T>
{
    private DispatcherQueue? _dispatcherQueue;

    public UiObservableCollection()
    {
    }

    public UiObservableCollection(DispatcherQueue? dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
    }

    /// <summary>
    ///     UI 队列。VM 在 UI 线程初始化后设置即可，设置前的行为和普通集合一样。
    /// </summary>
    public DispatcherQueue? DispatcherQueue
    {
        get => _dispatcherQueue;
        set => _dispatcherQueue = value;
    }

    private bool ShouldDispatch => _dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess;

    private void RunOnUi(Action action)
    {
        if (ShouldDispatch)
            _dispatcherQueue!.TryEnqueue(() => action());
        else
            action();
    }

    protected override void InsertItem(int index, T item)
    {
        if (!ShouldDispatch)
        {
            base.InsertItem(index, item);
            return;
        }

        _dispatcherQueue!.TryEnqueue(() => base.InsertItem(index, item));
    }

    protected override void RemoveItem(int index)
    {
        if (!ShouldDispatch)
        {
            base.RemoveItem(index);
            return;
        }

        _dispatcherQueue!.TryEnqueue(() =>
        {
            if (index < Count)
                base.RemoveItem(index);
        });
    }

    protected override void SetItem(int index, T item)
    {
        if (!ShouldDispatch)
        {
            base.SetItem(index, item);
            return;
        }

        _dispatcherQueue!.TryEnqueue(() =>
        {
            if (index < Count)
                base.SetItem(index, item);
        });
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        if (!ShouldDispatch)
        {
            base.MoveItem(oldIndex, newIndex);
            return;
        }

        _dispatcherQueue!.TryEnqueue(() =>
        {
            if (oldIndex < Count && newIndex < Count)
                base.MoveItem(oldIndex, newIndex);
        });
    }

    protected override void ClearItems()
    {
        if (!ShouldDispatch)
        {
            base.ClearItems();
            return;
        }

        _dispatcherQueue!.TryEnqueue(base.ClearItems);
    }

    /// <summary>
    ///     批量添加。整体在一次 UI 调度里完成，避免逐项排队导致顺序错乱。
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        var array = items as IList<T> ?? items.ToList();
        if (array.Count == 0)
            return;

        if (!ShouldDispatch)
        {
            foreach (var item in array)
                Items.Add(item);

            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            return;
        }

        _dispatcherQueue!.TryEnqueue(() =>
        {
            foreach (var item in array)
                Items.Add(item);

            OnCollectionChanged(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
                System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
        });
    }
}
