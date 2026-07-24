namespace FluxRoute.Services;

public readonly record struct TrayPopupBounds(double Left, double Top, double Width, double Height);

public static class TrayPopupPlacement
{
    private const double EdgeMargin = 6;
    private const double CursorGap = 8;

    public static TrayPopupBounds Calculate(
        double cursorX,
        double cursorY,
        double popupWidth,
        double popupHeight,
        double workLeft,
        double workTop,
        double workRight,
        double workBottom)
    {
        var left = cursorX - popupWidth;
        var top = cursorY - popupHeight - CursorGap;

        left = Math.Clamp(
            left,
            workLeft + EdgeMargin,
            Math.Max(workLeft + EdgeMargin, workRight - popupWidth - EdgeMargin));
        top = Math.Clamp(
            top,
            workTop + EdgeMargin,
            Math.Max(workTop + EdgeMargin, workBottom - popupHeight - EdgeMargin));

        return new TrayPopupBounds(left, top, popupWidth, popupHeight);
    }
}
