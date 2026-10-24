using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OMniX.WinUI.Services;
using Windows.Foundation;

namespace OMniX.WinUI.Helpers.Xaml;

/// <summary>
///     二开：页面切换动画。以前这段写在 ShellPage.xaml.cs 的 OnFrameNavigated 里，
///     抽出来独立成类，切页面的地方复用起来不用再抄一遍。
///
///     新页面从下方 16px 处淡入，scale 0.98 → 1，380ms，
///     曲线是 cubic-bezier(.22,1,.36,1)（KeySpline 才能表达自定义控制点，
///     内置的 CubicEase 只有现成的那几条）。
///
///     旧页面淡出不做：Frame 的 Content 一换旧页面立刻就没了，
///     抢在导航前给它做动画会被紧接着的替换打断，看不出效果还容易闪。
///     现在这样"新页面自己滑进来"读起来就是一次切换。
///
///     简易版直接跳过动画，但要保证页面是完全可见的状态。
/// </summary>
public static class PageTransitionFx
{
    private const int DurationMs = 380;

    private const double FromTranslateY = 16;
    private const double FromScale = 0.98;

    /// <summary>挂到 Frame 上，之后每次 Navigated 自动播</summary>
    public static void Attach(Frame frame)
    {
        frame.Navigated -= OnNavigated;
        frame.Navigated += OnNavigated;
    }

    private static void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (sender is Frame frame)
            Play(frame);
    }

    public static void Play(Frame frame)
    {
        // 用 CompositeTransform：缩放和位移要一起做
        var transform = frame.RenderTransform as CompositeTransform ?? new CompositeTransform();
        frame.RenderTransformOrigin = new Point(0.5, 0.5);
        frame.RenderTransform = transform;

        void Restore()
        {
            frame.Opacity = 1;
            transform.ScaleX = 1;
            transform.ScaleY = 1;
            transform.TranslateY = 0;
        }

        if (ThemeStyleService.Instance is { MotionEnabled: false })
        {
            Restore();
            return;
        }

        try
        {
            transform.ScaleX = FromScale;
            transform.ScaleY = FromScale;
            transform.TranslateY = FromTranslateY;
            frame.Opacity = 0;

            var storyboard = new Storyboard();

            storyboard.Children.Add(Spline(transform, nameof(CompositeTransform.ScaleX), 1.0));
            storyboard.Children.Add(Spline(transform, nameof(CompositeTransform.ScaleY), 1.0));
            storyboard.Children.Add(Spline(transform, nameof(CompositeTransform.TranslateY), 0.0));
            storyboard.Children.Add(Spline(frame, "Opacity", 1.0));

            // 明确保底：万一动画没跑完，也要把页面恢复成完全可见
            storyboard.Completed += (_, _) => Restore();
            storyboard.Begin();
        }
        catch
        {
            Restore();
        }
    }

    private static DoubleAnimationUsingKeyFrames Spline(DependencyObject target, string property, double to)
    {
        var animation = new DoubleAnimationUsingKeyFrames();

        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(DurationMs)),
            Value = to,
            KeySpline = new KeySpline
            {
                ControlPoint1 = new Point(0.22, 1),
                ControlPoint2 = new Point(0.36, 1)
            }
        });

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }
}
