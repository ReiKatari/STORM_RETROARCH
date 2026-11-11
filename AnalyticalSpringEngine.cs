using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace StormRetroArch;

/// <summary>
/// 1D Analytical Damped Spring using Hooke's Law with semi-implicit Euler integration and 2x sub-stepping.
/// Frame-rate independent physics across 60-120+ Hz displays.
/// </summary>
public struct AnalyticalSpring1D
{
    public float Position;
    public float Velocity;
    public float Target;
    public float Stiffness;
    public float Damping;

    public AnalyticalSpring1D(float position, float target, float stiffness = 380f, float damping = 30f)
    {
        Position = position;
        Velocity = 0f;
        Target = target;
        Stiffness = stiffness;
        Damping = damping;
    }

    /// <summary>
    /// Steps the spring physics by dtSeconds.
    /// Returns true while active, false when cleanly settled within tolerance.
    /// </summary>
    public bool Step(float dtSeconds)
    {
        if (Math.Abs(Position - Target) < 0.0008f && Math.Abs(Velocity) < 0.005f)
        {
            Position = Target;
            Velocity = 0f;
            return false;
        }

        // Clamp delta-time to protect against large frame drops (max 33ms)
        float clampedDt = Math.Min(dtSeconds, 0.0333f);
        float subDt = clampedDt * 0.5f;

        for (int i = 0; i < 2; i++)
        {
            float displacement = Position - Target;
            float springForce = -Stiffness * displacement;
            float dampingForce = -Damping * Velocity;
            float acceleration = springForce + dampingForce;

            Velocity += acceleration * subDt;
            Position += Velocity * subDt;
        }

        if (Math.Abs(Position - Target) < 0.0008f && Math.Abs(Velocity) < 0.005f)
        {
            Position = Target;
            Velocity = 0f;
            return false;
        }

        return true;
    }
}

/// <summary>
/// Choreography & Spring Physics Engine for WPF UI controls.
/// Uses a single shared CompositionTarget.Rendering handler that attaches ONLY when animations are active
/// and detaches immediately when all springs settle (0.0% idle CPU/GPU drain).
/// Preserves velocity if an element is re-triggered mid-flight.
/// </summary>
public static class AnalyticalSpringEngine
{
    private class ActiveAnimation
    {
        public FrameworkElement Element { get; }
        public AnalyticalSpring1D OpacitySpring;
        public AnalyticalSpring1D TransformSpring;
        public bool IsModalPop { get; }

        public ActiveAnimation(FrameworkElement element, AnalyticalSpring1D opacitySpring, AnalyticalSpring1D transformSpring, bool isModalPop)
        {
            Element = element;
            OpacitySpring = opacitySpring;
            TransformSpring = transformSpring;
            IsModalPop = isModalPop;
        }
    }

    private static readonly Dictionary<FrameworkElement, ActiveAnimation> ActiveAnimations = new();
    private static bool _isHooked = false;
    private static long _lastTickTimestamp = 0;

    /// <summary>
    /// Smoothly reveals a panel with upward translation and opacity fade-in.
    /// </summary>
    public static void AnimatePanelReveal(FrameworkElement element, double offsetY = 14.0, float stiffness = 380f, float damping = 30f)
    {
        if (element == null) return;

        if (!SettingsManager.Current.EnableUiAnimations)
        {
            element.Opacity = 1.0;
            if (element.RenderTransform is TranslateTransform tt)
            {
                tt.Y = 0;
            }
            else
            {
                element.RenderTransform = new TranslateTransform(0, 0);
            }
            ActiveAnimations.Remove(element);
            CheckHookState();
            return;
        }

        TranslateTransform translateTransform;
        if (element.RenderTransform is TranslateTransform existingTt)
        {
            translateTransform = existingTt;
        }
        else
        {
            translateTransform = new TranslateTransform(0, offsetY);
            element.RenderTransform = translateTransform;
        }

        float currentOpacityVel = 0f;
        float currentTransVel = 0f;
        float startOpacity = 0.25f;
        float startY = (float)offsetY;

        if (ActiveAnimations.TryGetValue(element, out var existing))
        {
            currentOpacityVel = existing.OpacitySpring.Velocity;
            currentTransVel = existing.TransformSpring.Velocity;
            startOpacity = (float)element.Opacity;
            startY = (float)translateTransform.Y;
        }
        else
        {
            element.Opacity = 0.25;
            translateTransform.Y = offsetY;
        }

        var opacitySpring = new AnalyticalSpring1D(startOpacity, 1.0f, stiffness, damping)
        {
            Velocity = currentOpacityVel
        };
        var ySpring = new AnalyticalSpring1D(startY, 0.0f, stiffness, damping)
        {
            Velocity = currentTransVel
        };

        ActiveAnimations[element] = new ActiveAnimation(element, opacitySpring, ySpring, false);
        CheckHookState();
    }

    /// <summary>
    /// Tactile scale-up pop and fade-in for modal dialog borders (0.94 -> 1.0).
    /// </summary>
    public static void AnimateModalPop(FrameworkElement dialogBorder, float stiffness = 420f, float damping = 32f)
    {
        if (dialogBorder == null) return;

        dialogBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        if (!SettingsManager.Current.EnableUiAnimations)
        {
            dialogBorder.Opacity = 1.0;
            if (dialogBorder.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
            }
            else
            {
                dialogBorder.RenderTransform = new ScaleTransform(1.0, 1.0);
            }
            ActiveAnimations.Remove(dialogBorder);
            CheckHookState();
            return;
        }

        ScaleTransform scaleTransform;
        if (dialogBorder.RenderTransform is ScaleTransform existingSt)
        {
            scaleTransform = existingSt;
        }
        else
        {
            scaleTransform = new ScaleTransform(0.94, 0.94);
            dialogBorder.RenderTransform = scaleTransform;
        }

        float currentOpacityVel = 0f;
        float currentScaleVel = 0f;
        float startOpacity = 0.0f;
        float startScale = 0.94f;

        if (ActiveAnimations.TryGetValue(dialogBorder, out var existing))
        {
            currentOpacityVel = existing.OpacitySpring.Velocity;
            currentScaleVel = existing.TransformSpring.Velocity;
            startOpacity = (float)dialogBorder.Opacity;
            startScale = (float)scaleTransform.ScaleX;
        }
        else
        {
            dialogBorder.Opacity = 0.0;
            scaleTransform.ScaleX = 0.94;
            scaleTransform.ScaleY = 0.94;
        }

        var opacitySpring = new AnalyticalSpring1D(startOpacity, 1.0f, stiffness, damping)
        {
            Velocity = currentOpacityVel
        };
        var scaleSpring = new AnalyticalSpring1D(startScale, 1.0f, stiffness, damping)
        {
            Velocity = currentScaleVel
        };

        ActiveAnimations[dialogBorder] = new ActiveAnimation(dialogBorder, opacitySpring, scaleSpring, true);
        CheckHookState();
    }

    private static void CheckHookState()
    {
        if (ActiveAnimations.Count > 0 && !_isHooked)
        {
            _lastTickTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnRendering;
            _isHooked = true;
        }
        else if (ActiveAnimations.Count == 0 && _isHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isHooked = false;
        }
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (ActiveAnimations.Count == 0)
        {
            CheckHookState();
            return;
        }

        long currentTick = Stopwatch.GetTimestamp();
        double dt = (double)(currentTick - _lastTickTimestamp) / Stopwatch.Frequency;
        _lastTickTimestamp = currentTick;

        float dtSeconds = (float)Math.Clamp(dt, 0.001, 0.05);
        List<FrameworkElement>? toRemove = null;

        foreach (var kvp in ActiveAnimations)
        {
            var anim = kvp.Value;
            var element = anim.Element;

            bool opActive = anim.OpacitySpring.Step(dtSeconds);
            bool trActive = anim.TransformSpring.Step(dtSeconds);

            element.Opacity = Math.Clamp(anim.OpacitySpring.Position, 0.0, 1.0);

            if (anim.IsModalPop)
            {
                if (element.RenderTransform is ScaleTransform st)
                {
                    st.ScaleX = anim.TransformSpring.Position;
                    st.ScaleY = anim.TransformSpring.Position;
                }
            }
            else
            {
                if (element.RenderTransform is TranslateTransform tt)
                {
                    tt.Y = anim.TransformSpring.Position;
                }
            }

            if (!opActive && !trActive)
            {
                toRemove ??= new List<FrameworkElement>();
                toRemove.Add(element);
            }
        }

        if (toRemove != null)
        {
            foreach (var elem in toRemove)
            {
                ActiveAnimations.Remove(elem);
            }
        }

        CheckHookState();
    }

    public static void FlushAllImmediately()
    {
        foreach (var kvp in ActiveAnimations)
        {
            var anim = kvp.Value;
            anim.Element.Opacity = 1.0;
            if (anim.IsModalPop && anim.Element.RenderTransform is ScaleTransform st)
            {
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
            }
            else if (!anim.IsModalPop && anim.Element.RenderTransform is TranslateTransform tt)
            {
                tt.Y = 0.0;
            }
        }
        ActiveAnimations.Clear();
        CheckHookState();
    }


    private class HoverSpringItem
    {
        public FrameworkElement Element { get; }
        public ScaleTransform ScaleT { get; }
        public TranslateTransform TransT { get; }
        public AnalyticalSpring1D ScaleSpring;
        public AnalyticalSpring1D LiftSpring;

        public HoverSpringItem(FrameworkElement element, ScaleTransform st, TranslateTransform tt)
        {
            Element = element;
            ScaleT = st;
            TransT = tt;
            ScaleSpring = new AnalyticalSpring1D((float)st.ScaleX, 1.0f, 420f, 32f);
            LiftSpring = new AnalyticalSpring1D((float)tt.Y, 0.0f, 420f, 32f);
        }
    }

    private static readonly Dictionary<FrameworkElement, HoverSpringItem> ActiveHoverSprings = new();
    private static bool _isHoverHooked = false;
    private static long _lastHoverTickTimestamp = 0;

    /// <summary>
    /// Animates a single UI element sliding up and fading in with an optional start delay.
    /// Uses RenderTransform without triggering layout invalidation passes.
    /// </summary>
    public static void AnimateElementSlideUp(UIElement el, double offsetY = 14.0, double delaySeconds = 0.0)
    {
        if (el is not FrameworkElement fe) return;

        if (!SettingsManager.Current.EnableUiAnimations || delaySeconds <= 0.001)
        {
            AnimatePanelReveal(fe, offsetY);
            return;
        }

        fe.Opacity = 0.0;
        if (fe.RenderTransform is not TranslateTransform)
        {
            fe.RenderTransform = new TranslateTransform(0, offsetY);
        }

        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(delaySeconds)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            AnimatePanelReveal(fe, offsetY);
        };
        timer.Start();
    }

    /// <summary>
    /// Staggered cascade entrance animation for child elements of a Panel or ItemsControl.
    /// </summary>
    public static void AnimateStaggeredChildren(object container, int maxItems = 12, double stepDelaySeconds = 0.035, double offsetY = 14.0)
    {
        if (container == null) return;

        IEnumerable<UIElement>? children = null;
        if (container is Panel panel)
        {
            children = panel.Children.OfType<UIElement>();
        }
        else if (container is ItemsControl ic)
        {
            var list = new List<UIElement>();
            for (int i = 0; i < ic.Items.Count && i < maxItems; i++)
            {
                if (ic.ItemContainerGenerator.ContainerFromIndex(i) is UIElement containerEl)
                {
                    list.Add(containerEl);
                }
            }
            children = list;
        }
        else if (container is IEnumerable<UIElement> enumElements)
        {
            children = enumElements;
        }

        if (children == null) return;

        int index = 0;
        foreach (var child in children)
        {
            if (index >= maxItems) break;
            AnimateElementSlideUp(child, offsetY, index * stepDelaySeconds);
            index++;
        }
    }

    /// <summary>
    /// Attaches spring-physics tactile hover responses (Scale + Y-Lift) to a FrameworkElement.
    /// Detaches immediately from CompositionTarget.Rendering once physics settle (0.0% idle CPU/GPU drain).
    /// </summary>
    public static void AttachInteractiveHoverSpring(FrameworkElement element, double hoverScale = 1.018, double hoverLiftY = -2.0)
    {
        if (element == null) return;

        element.RenderTransformOrigin = new Point(0.5, 0.5);

        ScaleTransform st;
        TranslateTransform tt;
        if (element.RenderTransform is TransformGroup tg &&
            tg.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform existingSt &&
            tg.Children.OfType<TranslateTransform>().FirstOrDefault() is TranslateTransform existingTt)
        {
            st = existingSt;
            tt = existingTt;
        }
        else
        {
            st = new ScaleTransform(1.0, 1.0);
            tt = new TranslateTransform(0.0, 0.0);
            var group = new TransformGroup();
            group.Children.Add(st);
            group.Children.Add(tt);
            element.RenderTransform = group;
        }

        element.MouseEnter += (s, e) =>
        {
            if (!SettingsManager.Current.EnableUiAnimations) return;

            if (!ActiveHoverSprings.TryGetValue(element, out var item))
            {
                item = new HoverSpringItem(element, st, tt);
                ActiveHoverSprings[element] = item;
            }

            item.ScaleSpring.Target = (float)hoverScale;
            item.LiftSpring.Target = (float)hoverLiftY;
            CheckHoverHookState();
        };

        element.MouseLeave += (s, e) =>
        {
            if (!SettingsManager.Current.EnableUiAnimations)
            {
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
                tt.Y = 0.0;
                return;
            }

            if (!ActiveHoverSprings.TryGetValue(element, out var item))
            {
                item = new HoverSpringItem(element, st, tt);
                ActiveHoverSprings[element] = item;
            }

            item.ScaleSpring.Target = 1.0f;
            item.LiftSpring.Target = 0.0f;
            CheckHoverHookState();
        };
    }

    private static void CheckHoverHookState()
    {
        if (ActiveHoverSprings.Count > 0 && !_isHoverHooked)
        {
            _lastHoverTickTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnHoverRendering;
            _isHoverHooked = true;
        }
        else if (ActiveHoverSprings.Count == 0 && _isHoverHooked)
        {
            CompositionTarget.Rendering -= OnHoverRendering;
            _isHoverHooked = false;
        }
    }

    private static void OnHoverRendering(object? sender, EventArgs e)
    {
        if (ActiveHoverSprings.Count == 0)
        {
            CheckHoverHookState();
            return;
        }

        long currentTick = Stopwatch.GetTimestamp();
        double dt = (double)(currentTick - _lastHoverTickTimestamp) / Stopwatch.Frequency;
        _lastHoverTickTimestamp = currentTick;

        float dtSeconds = (float)Math.Clamp(dt, 0.001, 0.05);
        List<FrameworkElement>? toRemove = null;

        foreach (var kvp in ActiveHoverSprings)
        {
            var item = kvp.Value;
            bool sActive = item.ScaleSpring.Step(dtSeconds);
            bool lActive = item.LiftSpring.Step(dtSeconds);

            item.ScaleT.ScaleX = item.ScaleSpring.Position;
            item.ScaleT.ScaleY = item.ScaleSpring.Position;
            item.TransT.Y = item.LiftSpring.Position;

            if (!sActive && !lActive)
            {
                toRemove ??= new List<FrameworkElement>();
                toRemove.Add(item.Element);
            }
        }

        if (toRemove != null)
        {
            foreach (var elem in toRemove)
            {
                ActiveHoverSprings.Remove(elem);
            }
        }

        CheckHoverHookState();
    }


    // ------------------------------------------------------------------------------------------------
    // 2.0.0 Enhanced Spring Choreography: Backdrop + Dialog Spring & View Transition Glides
    // ------------------------------------------------------------------------------------------------

    private class DialogBackdropAnimation
    {
        public UIElement Backdrop { get; }
        public FrameworkElement Dialog { get; }
        public ScaleTransform ScaleT { get; }
        public TranslateTransform TransT { get; }
        public AnalyticalSpring1D BackdropOpacitySpring;
        public AnalyticalSpring1D DialogOpacitySpring;
        public AnalyticalSpring1D DialogScaleSpring;
        public AnalyticalSpring1D DialogTranslateSpring;
        public bool IsOpening { get; }
        public Action? OnClosed { get; }

        public DialogBackdropAnimation(
            UIElement backdrop,
            FrameworkElement dialog,
            ScaleTransform st,
            TranslateTransform tt,
            bool isOpening,
            Action? onClosed)
        {
            Backdrop = backdrop;
            Dialog = dialog;
            ScaleT = st;
            TransT = tt;
            IsOpening = isOpening;
            OnClosed = onClosed;

            float targetBackdropOp = isOpening ? 1.0f : 0.0f;
            float targetDialogOp = isOpening ? 1.0f : 0.0f;
            float targetScale = isOpening ? 1.0f : 0.93f;
            float targetTransY = isOpening ? 0.0f : 16.0f;

            float currentBackdropOp = isOpening ? 0.0f : (float)backdrop.Opacity;
            float currentDialogOp = isOpening ? 0.0f : (float)dialog.Opacity;
            float currentScale = isOpening ? 0.93f : (float)st.ScaleX;
            float currentTransY = isOpening ? 16.0f : (float)tt.Y;

            BackdropOpacitySpring = new AnalyticalSpring1D(currentBackdropOp, targetBackdropOp, 440f, 34f);
            DialogOpacitySpring = new AnalyticalSpring1D(currentDialogOp, targetDialogOp, 440f, 34f);
            DialogScaleSpring = new AnalyticalSpring1D(currentScale, targetScale, 440f, 34f);
            DialogTranslateSpring = new AnalyticalSpring1D(currentTransY, targetTransY, 440f, 34f);
        }
    }

    private static readonly Dictionary<FrameworkElement, DialogBackdropAnimation> ActiveDialogSprings = new();
    private static bool _isDialogHooked = false;
    private static long _lastDialogTickTimestamp = 0;

    /// <summary>
    /// Smoothly springs dialog Scale (0.93 <-> 1.0), TranslateY (16 <-> 0), and backdrop Opacity (0 <-> 1)
    /// on both open and close without layout thrashing.
    /// </summary>
    public static void AnimateBackdropAndDialog(UIElement backdropOverlay, FrameworkElement dialogBox, bool opening, Action? onClosed = null)
    {
        if (backdropOverlay == null || dialogBox == null) return;

        dialogBox.RenderTransformOrigin = new Point(0.5, 0.5);

        ScaleTransform st;
        TranslateTransform tt;
        if (dialogBox.RenderTransform is TransformGroup tg && tg.Children.Count >= 2 &&
            tg.Children[0] is ScaleTransform s && tg.Children[1] is TranslateTransform t)
        {
            st = s;
            tt = t;
        }
        else
        {
            st = new ScaleTransform(opening ? 0.93 : 1.0, opening ? 0.93 : 1.0);
            tt = new TranslateTransform(0, opening ? 16.0 : 0.0);
            var group = new TransformGroup();
            group.Children.Add(st);
            group.Children.Add(tt);
            dialogBox.RenderTransform = group;
        }

        if (!SettingsManager.Current.EnableUiAnimations)
        {
            if (opening)
            {
                backdropOverlay.Visibility = Visibility.Visible;
                backdropOverlay.Opacity = 1.0;
                dialogBox.Visibility = Visibility.Visible;
                dialogBox.Opacity = 1.0;
                st.ScaleX = 1.0;
                st.ScaleY = 1.0;
                tt.Y = 0.0;
            }
            else
            {
                backdropOverlay.Visibility = Visibility.Collapsed;
                dialogBox.Visibility = Visibility.Collapsed;
                onClosed?.Invoke();
            }
            ActiveDialogSprings.Remove(dialogBox);
            CheckDialogHookState();
            return;
        }

        if (opening)
        {
            backdropOverlay.Visibility = Visibility.Visible;
            backdropOverlay.Opacity = 0.0;
            dialogBox.Visibility = Visibility.Visible;
            dialogBox.Opacity = 0.0;
            st.ScaleX = 0.93;
            st.ScaleY = 0.93;
            tt.Y = 16.0;
        }

        var anim = new DialogBackdropAnimation(backdropOverlay, dialogBox, st, tt, opening, onClosed);
        ActiveDialogSprings[dialogBox] = anim;
        CheckDialogHookState();
    }

    private static void CheckDialogHookState()
    {
        if (ActiveDialogSprings.Count > 0 && !_isDialogHooked)
        {
            _lastDialogTickTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnDialogRendering;
            _isDialogHooked = true;
        }
        else if (ActiveDialogSprings.Count == 0 && _isDialogHooked)
        {
            CompositionTarget.Rendering -= OnDialogRendering;
            _isDialogHooked = false;
        }
    }

    private static void OnDialogRendering(object? sender, EventArgs e)
    {
        if (ActiveDialogSprings.Count == 0)
        {
            CheckDialogHookState();
            return;
        }

        long currentTick = Stopwatch.GetTimestamp();
        double dt = (double)(currentTick - _lastDialogTickTimestamp) / Stopwatch.Frequency;
        _lastDialogTickTimestamp = currentTick;

        float dtSeconds = (float)Math.Clamp(dt, 0.001, 0.05);
        List<FrameworkElement>? toRemove = null;

        foreach (var kvp in ActiveDialogSprings)
        {
            var anim = kvp.Value;
            bool bActive = anim.BackdropOpacitySpring.Step(dtSeconds);
            bool dOpActive = anim.DialogOpacitySpring.Step(dtSeconds);
            bool scActive = anim.DialogScaleSpring.Step(dtSeconds);
            bool trActive = anim.DialogTranslateSpring.Step(dtSeconds);

            anim.Backdrop.Opacity = Math.Clamp(anim.BackdropOpacitySpring.Position, 0.0, 1.0);
            anim.Dialog.Opacity = Math.Clamp(anim.DialogOpacitySpring.Position, 0.0, 1.0);
            anim.ScaleT.ScaleX = anim.DialogScaleSpring.Position;
            anim.ScaleT.ScaleY = anim.DialogScaleSpring.Position;
            anim.TransT.Y = anim.DialogTranslateSpring.Position;

            if (!bActive && !dOpActive && !scActive && !trActive)
            {
                toRemove ??= new List<FrameworkElement>();
                toRemove.Add(anim.Dialog);

                if (!anim.IsOpening)
                {
                    anim.Backdrop.Visibility = Visibility.Collapsed;
                    anim.Dialog.Visibility = Visibility.Collapsed;
                    anim.OnClosed?.Invoke();
                }
            }
        }

        if (toRemove != null)
        {
            foreach (var elem in toRemove)
            {
                ActiveDialogSprings.Remove(elem);
            }
        }

        CheckDialogHookState();
    }

    /// <summary>
    /// Crisp 14px -> 0 directional glide + Opacity 0.3 -> 1.0 analytical spring (stiffness=480, damping=34)
    /// for buttery 60-120 FPS view transitions with zero layout thrash.
    /// </summary>
    public static void AnimateViewTransition(FrameworkElement viewRoot)
    {
        if (viewRoot == null) return;

        if (!SettingsManager.Current.EnableUiAnimations)
        {
            viewRoot.Opacity = 1.0;
            if (viewRoot.RenderTransform is TranslateTransform tt)
            {
                tt.Y = 0.0;
            }
            else
            {
                viewRoot.RenderTransform = new TranslateTransform(0, 0);
            }
            ActiveAnimations.Remove(viewRoot);
            CheckHookState();
            return;
        }

        TranslateTransform translateTransform;
        if (viewRoot.RenderTransform is TranslateTransform existingTt)
        {
            translateTransform = existingTt;
        }
        else
        {
            translateTransform = new TranslateTransform(0, 14.0);
            viewRoot.RenderTransform = translateTransform;
        }

        viewRoot.Opacity = 0.3;
        translateTransform.Y = 14.0;

        var opacitySpring = new AnalyticalSpring1D(0.3f, 1.0f, 480f, 34f);
        var ySpring = new AnalyticalSpring1D(14.0f, 0.0f, 480f, 34f);

        ActiveAnimations[viewRoot] = new ActiveAnimation(viewRoot, opacitySpring, ySpring, false);
        CheckHookState();
    }

}
