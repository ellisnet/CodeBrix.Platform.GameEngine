using System;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.System;
using CorePointerEventArgs = Windows.UI.Core.PointerEventArgs;
using WuxPointerPoint = Windows.UI.Input.PointerPoint;
using WuxPointerPointProperties = Windows.UI.Input.PointerPointProperties;
using WuxPointerUpdateKind = Windows.UI.Input.PointerUpdateKind;

namespace CodeBrix.Platform.GameEngine.Host.Tests;

/// <summary>
/// Builds pointer events the way a head does and raises them through the framework's own routed-event
/// path, so handler order, the Handled flag and bubbling to the parent behave as they do in an app.
/// The constructors and the raise method are internal to the framework, hence the reflection.
/// </summary>
internal static class TestPointerEvents
{
    private static uint _frameId;

    /// <summary>
    /// Creates the routed args for one pointer event whose original source is <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The element the pointer is over.</param>
    /// <param name="updateKind">What changed (a button pressed or released, or nothing for a move).</param>
    /// <param name="leftPressed">Whether the left button is down after this event.</param>
    /// <param name="rightPressed">Whether the right button is down after this event.</param>
    /// <param name="deviceType">The kind of pointer.</param>
    /// <param name="pointerId">The pointer id.</param>
    /// <param name="wheelDelta">The wheel delta, for a wheel event.</param>
    internal static PointerRoutedEventArgs Create(
        UIElement source,
        WuxPointerUpdateKind updateKind,
        bool leftPressed = false,
        bool rightPressed = false,
        PointerDeviceType deviceType = PointerDeviceType.Mouse,
        uint pointerId = 1,
        int wheelDelta = 0)
    {
        var properties = (WuxPointerPointProperties)Activator.CreateInstance(typeof(WuxPointerPointProperties), nonPublic: true)!;
        SetProperty(properties, nameof(WuxPointerPointProperties.IsLeftButtonPressed), leftPressed);
        SetProperty(properties, nameof(WuxPointerPointProperties.IsRightButtonPressed), rightPressed);
        SetProperty(properties, nameof(WuxPointerPointProperties.IsInRange), true);
        SetProperty(properties, nameof(WuxPointerPointProperties.IsPrimary), true);
        SetProperty(properties, nameof(WuxPointerPointProperties.PointerUpdateKind), updateKind);
        SetProperty(properties, nameof(WuxPointerPointProperties.MouseWheelDelta), wheelDelta);

        var device = typeof(PointerDevice)
            .GetMethod("For", BindingFlags.NonPublic | BindingFlags.Static, [typeof(PointerDeviceType)])!
            .Invoke(null, [deviceType]);

        var position = new Point(10, 10);
        var point = (WuxPointerPoint)CreateInstance(
            typeof(WuxPointerPoint),
            ++_frameId,
            (ulong)_frameId * 1000,
            device!,
            pointerId,
            position,
            position,
            leftPressed || rightPressed,
            properties);

        var coreArgs = (CorePointerEventArgs)CreateInstance(typeof(CorePointerEventArgs), point, VirtualKeyModifiers.None);

        return (PointerRoutedEventArgs)CreateInstance(typeof(PointerRoutedEventArgs), coreArgs, source);
    }

    /// <summary>
    /// Raises <paramref name="routedEvent"/> on <paramref name="source"/> and lets it bubble to its parents.
    /// </summary>
    /// <param name="source">The element the event starts on.</param>
    /// <param name="routedEvent">The pointer routed event.</param>
    /// <param name="args">The args from <see cref="Create"/>.</param>
    internal static void Raise(UIElement source, RoutedEvent routedEvent, PointerRoutedEventArgs args)
    {
        var raise = typeof(UIElement)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(m => m.Name == "RaiseEvent" && m.GetParameters().Length == 3);
        var context = Activator.CreateInstance(raise.GetParameters()[2].ParameterType);

        raise.Invoke(source, [routedEvent, args, context]);
    }

    private static void SetProperty(object target, string name, object value)
        => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);

    private static object CreateInstance(Type type, params object[] arguments)
    {
        var constructor = type
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == arguments.Length
                         && c.GetParameters().Select(p => p.ParameterType)
                             .Zip(arguments, (parameter, argument) => parameter.IsInstanceOfType(argument))
                             .All(match => match));

        return constructor.Invoke(arguments);
    }
}
