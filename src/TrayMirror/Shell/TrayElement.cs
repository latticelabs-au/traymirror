using TrayMirror.Core.Geometry;

namespace TrayMirror.Shell;

/// <summary>
/// One element read out of a taskbar's UI Automation tree.
/// </summary>
/// <param name="Name">The element name, which for a tray icon is its tooltip text.</param>
/// <param name="AutomationId">The automation identifier, often empty on XAML shell elements.</param>
/// <param name="ClassName">The element class name.</param>
/// <param name="ControlType">The control type's programmatic name.</param>
/// <param name="Bounds">The bounding rectangle in physical screen pixels.</param>
internal sealed record TrayElement(
    string Name,
    string AutomationId,
    string ClassName,
    string ControlType,
    PixelRect Bounds);
