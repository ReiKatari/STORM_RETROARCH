using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace StormRetroArch;

/// <summary>
/// Высокопроизводительная виртуализирующая панель плиточного отображения карточек (Bento/Poster Grid).
/// Реализует IScrollInfo, гарантируя мгновенную прокрутку библиотек от 50 000+ игр без выделения памяти под невидимые элементы.
/// </summary>
public class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    private const double CardGap = 12.0;

    public VirtualizingTilePanel()
    {
        ClipToBounds = true;
    }

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingTilePanel),
            new FrameworkPropertyMetadata(380.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange, OnItemDimensionChanged));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingTilePanel),
            new FrameworkPropertyMetadata(206.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange, OnItemDimensionChanged));

    private static void OnItemDimensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VirtualizingTilePanel panel)
        {
            panel.InvalidateMeasure();
            panel.ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);
    private Point _offset = new(0, 0);
    private ScrollViewer? _scrollOwner;
    private bool _canVerticallyScroll = true;
    private bool _canHorizontallyScroll = false;

    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public ScrollViewer? ScrollOwner
    {
        get => _scrollOwner;
        set
        {
            if (!ReferenceEquals(_scrollOwner, value))
            {
                _scrollOwner = value;
                _scrollOwner?.InvalidateScrollInfo();
            }
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        base.OnItemsChanged(sender, args);
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Remove:
            case NotifyCollectionChangedAction.Replace:
                if (args.Position.Index >= 0 && args.ItemUICount > 0 && args.Position.Index < InternalChildren.Count)
                {
                    int removeCount = Math.Min(args.ItemUICount, InternalChildren.Count - args.Position.Index);
                    RemoveInternalChildRange(args.Position.Index, removeCount);
                }
                break;
            case NotifyCollectionChangedAction.Move:
                if (args.OldPosition.Index >= 0 && args.ItemUICount > 0 && args.OldPosition.Index < InternalChildren.Count)
                {
                    int moveCount = Math.Min(args.ItemUICount, InternalChildren.Count - args.OldPosition.Index);
                    RemoveInternalChildRange(args.OldPosition.Index, moveCount);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                if (InternalChildren.Count > 0)
                {
                    RemoveInternalChildRange(0, InternalChildren.Count);
                }
                _offset = new Point(0, 0);
                break;
        }
        InvalidateMeasure();
        ScrollOwner?.InvalidateScrollInfo();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // CRITICAL WPF REQUIREMENT: Touch InternalChildren FIRST so Panel.EnsureGenerator() initializes ItemContainerGenerator!
        UIElementCollection children = InternalChildren;

        var itemsControl = ItemsControl.GetItemsOwner(this);
        int itemCount = itemsControl != null ? itemsControl.Items.Count : children.Count;

        double w = (ItemWidth > 1.0 ? ItemWidth : 380.0) + CardGap;
        double h = (ItemHeight > 1.0 ? ItemHeight : 206.0) + CardGap;

        double availWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? (ActualWidth > 0 ? ActualWidth : 1200.0)
            : availableSize.Width;

        int columns = Math.Max(1, (int)Math.Floor((availWidth + CardGap) / w));
        int totalRows = itemCount > 0 ? (itemCount + columns - 1) / columns : 0;

        double totalExtentHeight = totalRows * h;
        double viewportHeight = double.IsInfinity(availableSize.Height) || availableSize.Height <= 0
            ? (ActualHeight > 0 ? ActualHeight : 800.0)
            : availableSize.Height;

        _extent = new Size(columns * w, totalExtentHeight);
        _viewport = new Size(availWidth, viewportHeight);

        double maxOffsetY = Math.Max(0.0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxOffsetY)
        {
            _offset.Y = maxOffsetY;
        }

        ScrollOwner?.InvalidateScrollInfo();

        if (itemCount == 0)
        {
            if (children.Count > 0)
            {
                RemoveInternalChildRange(0, children.Count);
            }
            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        int firstVisibleRow = Math.Max(0, (int)Math.Floor(_offset.Y / h) - 1);
        int lastVisibleRow = Math.Min(totalRows - 1, (int)Math.Ceiling((_offset.Y + _viewport.Height) / h) + 1);

        int firstItemIndex = Math.Max(0, firstVisibleRow * columns);
        int lastItemIndex = Math.Min(itemCount - 1, (lastVisibleRow + 1) * columns - 1);

        IItemContainerGenerator generator = ItemContainerGenerator;
        if (generator == null)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height);
        }

        CleanUpContainers(generator, children, firstItemIndex, lastItemIndex);

        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstItemIndex);
        int childIndex = (startPos.Offset == 0) ? startPos.Index : startPos.Index + 1;

        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
        {
            for (int itemIdx = firstItemIndex; itemIdx <= lastItemIndex; itemIdx++, childIndex++)
            {
                var child = generator.GenerateNext(out bool isNewlyRealized) as UIElement;
                if (child == null) continue;

                if (isNewlyRealized)
                {
                    if (childIndex >= children.Count)
                    {
                        AddInternalChild(child);
                    }
                    else
                    {
                        InsertInternalChild(childIndex, child);
                    }
                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(w, h));
            }
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height
        );
    }

    private void CleanUpContainers(IItemContainerGenerator generator, UIElementCollection children, int firstVisibleItemIndex, int lastVisibleItemIndex)
    {
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var genPos = new GeneratorPosition(i, 0);
            int itemIndex = generator.IndexFromGeneratorPosition(genPos);
            if (itemIndex < 0 || itemIndex < firstVisibleItemIndex || itemIndex > lastVisibleItemIndex)
            {
                generator.Remove(genPos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UIElementCollection children = InternalChildren;
        IItemContainerGenerator generator = ItemContainerGenerator;

        double w = (ItemWidth > 1.0 ? ItemWidth : 380.0) + CardGap;
        double h = (ItemHeight > 1.0 ? ItemHeight : 206.0) + CardGap;

        double availWidth = finalSize.Width > 0 ? finalSize.Width : (ActualWidth > 0 ? ActualWidth : 1200.0);
        int columns = Math.Max(1, (int)Math.Floor((availWidth + CardGap) / w));

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            int itemIndex = generator != null
                ? generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0))
                : i;

            if (itemIndex < 0) continue;

            int row = itemIndex / columns;
            int col = itemIndex % columns;

            double x = col * w - _offset.X;
            double y = row * h - _offset.Y;

            child.Arrange(new Rect(x, y, w, h));
        }

        return finalSize;
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - (ItemHeight + CardGap) * 0.5);
    public void LineDown() => SetVerticalOffset(_offset.Y + (ItemHeight + CardGap) * 0.5);
    public void LineLeft() => SetHorizontalOffset(_offset.X - 50.0);
    public void LineRight() => SetHorizontalOffset(_offset.X + 50.0);

    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);

    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - (ItemHeight + CardGap) * 0.65);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + (ItemHeight + CardGap) * 0.65);
    public void MouseWheelLeft() => SetHorizontalOffset(_offset.X - 50.0);
    public void MouseWheelRight() => SetHorizontalOffset(_offset.X + 50.0);

    public void SetHorizontalOffset(double offset)
    {
        double maxOffsetX = Math.Max(0.0, _extent.Width - _viewport.Width);
        double clamped = Math.Clamp(offset, 0.0, maxOffsetX);
        if (Math.Abs(clamped - _offset.X) > 0.001)
        {
            _offset.X = clamped;
            InvalidateArrange();
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public void SetVerticalOffset(double offset)
    {
        double maxOffsetY = Math.Max(0.0, _extent.Height - _viewport.Height);
        double clamped = Math.Clamp(offset, 0.0, maxOffsetY);
        if (Math.Abs(clamped - _offset.Y) > 0.001)
        {
            _offset.Y = clamped;
            InvalidateMeasure();
            ScrollOwner?.InvalidateScrollInfo();
        }
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement element)
        {
            int childIdx = InternalChildren.IndexOf(element);
            if (childIdx >= 0 && ItemContainerGenerator != null)
            {
                int itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(childIdx, 0));
                if (itemIndex >= 0)
                {
                    double w = (ItemWidth > 1.0 ? ItemWidth : 380.0) + CardGap;
                    double h = (ItemHeight > 1.0 ? ItemHeight : 206.0) + CardGap;
                    double availWidth = _viewport.Width > 0 ? _viewport.Width : 1200.0;
                    int columns = Math.Max(1, (int)Math.Floor((availWidth + CardGap) / w));
                    int row = itemIndex / columns;
                    double top = row * h;
                    double bottom = top + h;
                    if (top < _offset.Y)
                        SetVerticalOffset(top);
                    else if (bottom > _offset.Y + _viewport.Height)
                        SetVerticalOffset(bottom - _viewport.Height);
                }
            }
        }
        return rectangle;
    }
}
