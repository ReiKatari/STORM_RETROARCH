using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace StormRetroArch;

/// <summary>
/// 1D Analytical Damped Spring using Hooke's Law with semi-implicit Euler integration and 2x sub-stepping.
/// Frame-rate independent physics across 60–120+ Hz displays.
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

}
