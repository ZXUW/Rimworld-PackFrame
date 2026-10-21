using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace PackFrame;

public partial class MainWindow
{
    private void UpdateAutoScroll(PointerEventArgs e)
    {
        var target = GetTargetListAt(e);
        if (target is null)
        {
            StopAutoScroll();
            return;
        }
        var position = e.GetPosition(target);
        const double margin = 32;
        var direction = position.Y < margin ? -1 : position.Y > target.Bounds.Height - margin ? 1 : 0;
        if (direction == 0)
        {
            StopAutoScroll();
            return;
        }
        _autoScrollList = target;
        _autoScrollDirection = direction;
        if (!_autoScrollTimer.IsEnabled) _autoScrollTimer.Start();
    }

    private void StopAutoScroll()
    {
        _autoScrollTimer.Stop();
        _autoScrollList = null;
        _autoScrollDirection = 0;
    }

    private void OnAutoScrollTick()
    {
        if (_autoScrollList is null || _autoScrollDirection == 0)
        {
            StopAutoScroll();
            return;
        }
        var viewer = _autoScrollList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer is null)
        {
            StopAutoScroll();
            return;
        }
        var maximum = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        var offset = Math.Clamp(viewer.Offset.Y + _autoScrollDirection * 24, 0, maximum);
        if (Math.Abs(offset - viewer.Offset.Y) < 0.5) return;
        viewer.Offset = new Vector(viewer.Offset.X, offset);
    }

    private void UpdateDragGhost(PointerEventArgs e)
    {
        var position = e.GetPosition(_dragOverlay);
        var maxX = Math.Max(0, _dragOverlay.Bounds.Width - 240);
        var ghostHeight = _dragGhost.Bounds.Height > 0 ? _dragGhost.Bounds.Height : 34;
        var maxY = Math.Max(0, _dragOverlay.Bounds.Height - ghostHeight);
        Canvas.SetLeft(_dragGhost, Math.Clamp(position.X + 16, 0, maxX));
        Canvas.SetTop(_dragGhost, Math.Clamp(position.Y + 14, 0, maxY));
    }
}
