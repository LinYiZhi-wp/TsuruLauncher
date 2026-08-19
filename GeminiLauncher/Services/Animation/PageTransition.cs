using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GeminiLauncher.Services.Animation
{
    public enum TransitionType
    {
        SlideIn,
        SlideOut,
        FadeSlideIn,
        FadeSlideOut,
        ScaleIn,
        ScaleOut,
        FadeIn,
        FadeOut,
        SlideUp,
        SlideDown,
        None
    }

    /// <summary>
    /// PCL2-style motion design: fast, feather-light and softly springy.
    /// Entrances overshoot just a little (BackEase) and settle with a gentle
    /// ease-out; exits are quick and unobtrusive.
    /// </summary>
    public static class TransitionConfig
    {
        // Durations — quick enough to feel snappy, long enough to read as "springy"
        public static Duration DefaultDuration => TimeSpan.FromSeconds(0.30);
        public static Duration FastDuration => TimeSpan.FromSeconds(0.18);
        public static Duration SlowDuration => TimeSpan.FromSeconds(0.42);
        public static Duration PageEnterDuration => TimeSpan.FromSeconds(0.32);
        public static Duration PageExitDuration => TimeSpan.FromSeconds(0.14);
        public static Duration StaggerItemDuration => TimeSpan.FromSeconds(0.26);

        // Distances
        public static double SlideDistance => 40;
        public static double SlideDistanceSubtle => 20;
        public static double ScaleFrom => 0.96;
        public static double PageEnterSlideX => 16;
        public static double PageEnterSlideY => 10;
        public static double StaggerSlideY => 14;
        public static double StaggerScaleFrom => 0.97;

        // Easing — the "soft bounce" family
        // BackEase EaseOut overshoots by Amplitude then settles: exactly the
        // PCL2 card/panel feel. Bigger amplitude = more visible bounce.
        public static IEasingFunction SpringEase => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        public static IEasingFunction SoftSpringEase => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.32 };
        public static IEasingFunction GentleSpringEase => new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 };

        // Smooth fallbacks
        public static IEasingFunction SmoothEase => new CubicEase { EasingMode = EasingMode.EaseOut };
        public static IEasingFunction DecelerateEase => new CubicEase { EasingMode = EasingMode.EaseOut };
        public static IEasingFunction AccelerateEase => new CubicEase { EasingMode = EasingMode.EaseIn };

        // A restrained single-oscillation jelly for special moments
        public static IEasingFunction JellyEase => new ElasticEase { EasingMode = EasingMode.EaseOut, Springiness = 4, Oscillations = 1 };

        // Page exit — quick fade+shrink
        public static IEasingFunction PageEnterEase => new CubicEase { EasingMode = EasingMode.EaseOut };
        public static IEasingFunction PageExitEase => new CubicEase { EasingMode = EasingMode.EaseIn };
    }

    public static class PageTransition
    {
        // Track the running storyboard per element so concurrent animations on
        // different elements never cancel each other (the old global field did).
        private static readonly Dictionary<FrameworkElement, Storyboard> _activeStoryboards = new();

        public static void Play(FrameworkElement target, TransitionType type, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            var (scale, translate) = EnsureTransforms(target);

            var sb = new Storyboard();
            var duration = TransitionConfig.DefaultDuration;

            switch (type)
            {
                case TransitionType.FadeSlideIn:
                    AddFadeSlideIn(sb, target, scale, translate, duration);
                    break;
                case TransitionType.FadeSlideOut:
                    AddFadeSlideOut(sb, target, scale, translate, duration);
                    break;
                case TransitionType.SlideIn:
                    AddSlideIn(sb, target, translate, duration);
                    break;
                case TransitionType.SlideOut:
                    AddSlideOut(sb, target, translate, duration);
                    break;
                case TransitionType.ScaleIn:
                    AddScaleIn(sb, target, scale, duration);
                    break;
                case TransitionType.ScaleOut:
                    AddScaleOut(sb, target, scale, duration);
                    break;
                case TransitionType.FadeIn:
                    AddFadeIn(sb, target, duration);
                    break;
                case TransitionType.FadeOut:
                    AddFadeOut(sb, target, duration);
                    break;
                case TransitionType.SlideUp:
                    AddSlideUp(sb, target, translate, duration);
                    break;
                case TransitionType.SlideDown:
                    AddSlideDown(sb, target, translate, duration);
                    break;
                case TransitionType.None:
                    onCompleted?.Invoke();
                    return;
            }

            Begin(sb, target, onCompleted);
        }

        /// <summary>
        /// Pop a card/dialog/panel in with a springy scale — the signature
        /// "soft bounce" entrance.
        /// </summary>
        public static void PlayPopIn(FrameworkElement target, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            var (scale, _) = EnsureTransforms(target);

            target.Opacity = 0;
            scale.ScaleX = scale.ScaleY = 0.92;

            var sb = new Storyboard();

            var fade = new DoubleAnimation(0, 1, TransitionConfig.FastDuration) { EasingFunction = TransitionConfig.DecelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var scaleX = new DoubleAnimation(0.92, 1.0, TransitionConfig.DefaultDuration) { EasingFunction = TransitionConfig.SpringEase };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(0.92, 1.0, TransitionConfig.DefaultDuration) { EasingFunction = TransitionConfig.SpringEase };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleY);

            Begin(sb, target, onCompleted);
        }

        /// <summary>
        /// Page entrance: fade + soft spring scale + a whisper of upward motion.
        /// </summary>
        public static void PlayPageEnter(Page page, bool isForward = true)
        {
            if (page == null) return;
            StopActive(page);

            var (scale, translate) = EnsureTransforms(page);

            page.Opacity = 0;
            scale.ScaleX = scale.ScaleY = 0.955;
            translate.Y = TransitionConfig.PageEnterSlideY;

            var sb = new Storyboard();

            var fade = new DoubleAnimation(0, 1, TransitionConfig.PageEnterDuration) { EasingFunction = TransitionConfig.DecelerateEase };
            Storyboard.SetTarget(fade, page);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var scaleX = new DoubleAnimation(0.955, 1.0, TransitionConfig.PageEnterDuration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(0.955, 1.0, TransitionConfig.PageEnterDuration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleY);

            var slide = new DoubleAnimation(TransitionConfig.PageEnterSlideY, 0, TransitionConfig.PageEnterDuration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));
            sb.Children.Add(slide);

            Begin(sb, page, null);
        }

        public static void PlayPageExit(Page page, bool isForward = true, Action? onCompleted = null)
        {
            if (page == null) { onCompleted?.Invoke(); return; }
            StopActive(page);

            var (scale, _) = EnsureTransforms(page);

            var sb = new Storyboard();

            var fade = new DoubleAnimation(1, 0, TransitionConfig.PageExitDuration) { EasingFunction = TransitionConfig.PageExitEase };
            Storyboard.SetTarget(fade, page);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var scaleX = new DoubleAnimation(1.0, 0.985, TransitionConfig.PageExitDuration) { EasingFunction = TransitionConfig.PageExitEase };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(1.0, 0.985, TransitionConfig.PageExitDuration) { EasingFunction = TransitionConfig.PageExitEase };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleY);

            Begin(sb, page, onCompleted);
        }

        /// <summary>
        /// Container entrance: a barely-there scale with a soft settle.
        /// </summary>
        public static void PlayContainerEnter(FrameworkElement container, bool isForward = true)
        {
            if (container == null) return;

            var scale = new ScaleTransform(0.985, 0.985);
            var translate = new TranslateTransform(isForward ? 12 : -12, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            container.RenderTransform = group;
            container.RenderTransformOrigin = new Point(0.5, 0.5);

            var duration = TransitionConfig.PageEnterDuration;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1.0, duration) { EasingFunction = TransitionConfig.SoftSpringEase });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1.0, duration) { EasingFunction = TransitionConfig.SoftSpringEase });
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translate.X, 0, duration) { EasingFunction = TransitionConfig.SoftSpringEase });

            var timer = new DispatcherTimer { Interval = duration.TimeSpan };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                scale.ScaleX = scale.ScaleY = 1.0;
                translate.X = 0;
            };
            timer.Start();
        }

        public static void PlayContainerExit(FrameworkElement container, bool isForward = true, Action? onCompleted = null)
        {
            if (container == null) { onCompleted?.Invoke(); return; }

            var scale = new ScaleTransform(1.0, 1.0);
            var translate = new TranslateTransform(0, 0);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(translate);
            container.RenderTransform = group;
            container.RenderTransformOrigin = new Point(0.5, 0.5);

            var ease = TransitionConfig.PageExitEase;
            var duration = TransitionConfig.PageExitDuration;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 0.985, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 0.985, duration) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, isForward ? -8 : 8, duration) { EasingFunction = ease });

            var timer = new DispatcherTimer { Interval = duration.TimeSpan };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                scale.ScaleX = scale.ScaleY = 1.0;
                translate.X = 0;
                onCompleted?.Invoke();
            };
            timer.Start();
        }

        /// <summary>
        /// Staggered children entrance: each child fades in, rises a touch and
        /// springs into place — the classic PCL2 list/card feel.
        /// </summary>
        public static void PlayStaggeredIn(Panel container, double staggerMs = 35)
        {
            for (int i = 0; i < container.Children.Count; i++)
            {
                if (container.Children[i] is not FrameworkElement child) continue;

                StopActive(child);

                var (scale, translate) = EnsureTransforms(child);

                child.Opacity = 0;
                scale.ScaleX = scale.ScaleY = TransitionConfig.StaggerScaleFrom;
                translate.Y = TransitionConfig.StaggerSlideY;

                var delay = TimeSpan.FromMilliseconds(i * staggerMs);

                var sb = new Storyboard();
                var duration = TransitionConfig.StaggerItemDuration;

                var fade = new DoubleAnimation(0, 1, duration)
                {
                    BeginTime = delay,
                    EasingFunction = TransitionConfig.DecelerateEase
                };
                Storyboard.SetTarget(fade, child);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                sb.Children.Add(fade);

                var slide = new DoubleAnimation(TransitionConfig.StaggerSlideY, 0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = TransitionConfig.SoftSpringEase
                };
                Storyboard.SetTarget(slide, translate);
                Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));
                sb.Children.Add(slide);

                var scaleX = new DoubleAnimation(TransitionConfig.StaggerScaleFrom, 1.0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = TransitionConfig.SoftSpringEase
                };
                Storyboard.SetTarget(scaleX, scale);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
                sb.Children.Add(scaleX);

                var scaleY = new DoubleAnimation(TransitionConfig.StaggerScaleFrom, 1.0, duration)
                {
                    BeginTime = delay,
                    EasingFunction = TransitionConfig.SoftSpringEase
                };
                Storyboard.SetTarget(scaleY, scale);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
                sb.Children.Add(scaleY);

                Begin(sb, child, null);
            }
        }

        public static void PlayExpandCollapse(FrameworkElement target, bool expand, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            var (scale, translate) = EnsureTransforms(target);

            var sb = new Storyboard();

            if (expand)
            {
                target.Opacity = 0;
                scale.ScaleY = 0.9;
                translate.Y = -8;

                var fade = new DoubleAnimation(0, 1, TransitionConfig.FastDuration)
                {
                    EasingFunction = TransitionConfig.DecelerateEase
                };
                Storyboard.SetTarget(fade, target);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                sb.Children.Add(fade);

                var slide = new DoubleAnimation(-8, 0, TransitionConfig.DefaultDuration)
                {
                    EasingFunction = TransitionConfig.GentleSpringEase
                };
                Storyboard.SetTarget(slide, translate);
                Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));
                sb.Children.Add(slide);

                var scaleY = new DoubleAnimation(0.9, 1.0, TransitionConfig.DefaultDuration)
                {
                    EasingFunction = TransitionConfig.GentleSpringEase
                };
                Storyboard.SetTarget(scaleY, scale);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
                sb.Children.Add(scaleY);
            }
            else
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.12))
                {
                    EasingFunction = TransitionConfig.AccelerateEase
                };
                Storyboard.SetTarget(fade, target);
                Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
                sb.Children.Add(fade);

                var slide = new DoubleAnimation(0, -6, TimeSpan.FromSeconds(0.12))
                {
                    EasingFunction = TransitionConfig.AccelerateEase
                };
                Storyboard.SetTarget(slide, translate);
                Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));
                sb.Children.Add(slide);
            }

            Begin(sb, target, onCompleted);
        }

        public static void PlayScaleBounce(FrameworkElement target, double from = 0.92, double to = 1.0)
        {
            if (target == null) return;
            StopActive(target);

            var (scale, _) = EnsureTransforms(target);

            var sb = new Storyboard();

            var scaleX = new DoubleAnimation(from, to, TransitionConfig.DefaultDuration)
            {
                EasingFunction = TransitionConfig.SpringEase
            };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(from, to, TransitionConfig.DefaultDuration)
            {
                EasingFunction = TransitionConfig.SpringEase
            };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleY);

            Begin(sb, target, null);
        }

        #region Storyboard plumbing

        private static void StopActive(FrameworkElement target)
        {
            if (_activeStoryboards.TryGetValue(target, out var old))
            {
                old.Stop();
                _activeStoryboards.Remove(target);
            }
        }

        private static void Begin(Storyboard sb, FrameworkElement target, Action? onCompleted)
        {
            sb.Completed += (s, e) =>
            {
                _activeStoryboards.Remove(target);
                onCompleted?.Invoke();
            };
            _activeStoryboards[target] = sb;
            sb.Begin(target);
        }

        #endregion

        #region Private Helpers

        private static (ScaleTransform scale, TranslateTransform translate) EnsureTransforms(FrameworkElement element)
        {
            ScaleTransform scale;
            TranslateTransform translate;

            if (element.RenderTransform is TransformGroup group && group.Children.Count >= 2 &&
                group.Children[0] is ScaleTransform st && group.Children[1] is TranslateTransform tt)
            {
                scale = st;
                translate = tt;
            }
            else
            {
                scale = new ScaleTransform(1, 1);
                translate = new TranslateTransform(0, 0);
                var newGroup = new TransformGroup();
                newGroup.Children.Add(scale);
                newGroup.Children.Add(translate);
                element.RenderTransform = newGroup;
                element.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            return (scale, translate);
        }

        private static void AddFadeSlideIn(Storyboard sb, FrameworkElement target, ScaleTransform scale, TranslateTransform translate, Duration duration)
        {
            target.Opacity = 0;
            translate.X = TransitionConfig.SlideDistance;

            var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = TransitionConfig.DecelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(TransitionConfig.SlideDistance, 0, duration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
            sb.Children.Add(slide);
        }

        private static void AddFadeSlideOut(Storyboard sb, FrameworkElement target, ScaleTransform scale, TranslateTransform translate, Duration duration)
        {
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(0, -TransitionConfig.SlideDistanceSubtle, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
            sb.Children.Add(slide);
        }

        private static void AddSlideIn(Storyboard sb, FrameworkElement target, TranslateTransform translate, Duration duration)
        {
            translate.X = TransitionConfig.SlideDistance;

            var slide = new DoubleAnimation(TransitionConfig.SlideDistance, 0, duration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
            sb.Children.Add(slide);
        }

        private static void AddSlideOut(Storyboard sb, FrameworkElement target, TranslateTransform translate, Duration duration)
        {
            var slide = new DoubleAnimation(0, -TransitionConfig.SlideDistance, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("X"));
            sb.Children.Add(slide);
        }

        private static void AddScaleIn(Storyboard sb, FrameworkElement target, ScaleTransform scale, Duration duration)
        {
            target.Opacity = 0;

            var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = TransitionConfig.DecelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var scaleX = new DoubleAnimation(TransitionConfig.ScaleFrom, 1.0, duration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(TransitionConfig.ScaleFrom, 1.0, duration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleY);
        }

        private static void AddScaleOut(Storyboard sb, FrameworkElement target, ScaleTransform scale, Duration duration)
        {
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var scaleX = new DoubleAnimation(1.0, TransitionConfig.ScaleFrom, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(1.0, TransitionConfig.ScaleFrom, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("ScaleY"));
            sb.Children.Add(scaleY);
        }

        private static void AddFadeIn(Storyboard sb, FrameworkElement target, Duration duration)
        {
            target.Opacity = 0;
            var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = TransitionConfig.DecelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
        }

        private static void AddFadeOut(Storyboard sb, FrameworkElement target, Duration duration)
        {
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);
        }

        private static void AddSlideUp(Storyboard sb, FrameworkElement target, TranslateTransform translate, Duration duration)
        {
            target.Opacity = 0;
            translate.Y = TransitionConfig.SlideDistance;

            var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = TransitionConfig.DecelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(TransitionConfig.SlideDistance, 0, duration) { EasingFunction = TransitionConfig.SoftSpringEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));
            sb.Children.Add(slide);
        }

        private static void AddSlideDown(Storyboard sb, FrameworkElement target, TranslateTransform translate, Duration duration)
        {
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(fade, target);
            Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            sb.Children.Add(fade);

            var slide = new DoubleAnimation(0, TransitionConfig.SlideDistance, duration) { EasingFunction = TransitionConfig.AccelerateEase };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, new PropertyPath("Y"));
            sb.Children.Add(slide);
        }

        #endregion
    }
}
