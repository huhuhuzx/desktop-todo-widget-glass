using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

// Native WPF counterparts of transitions-dev's timing and state rules. BlurEffect is
// deliberately omitted: on WPF it repaints text and whole panels on every frame.
internal static class Motion
{
 public const int Stagger=40,Micro=80,Quick=150,Fast=250,Medium=350,Slow=400,VerySlow=500;
 public static bool Enabled { get { return SystemParameters.ClientAreaAnimation; } }
 static readonly IEasingFunction Smooth = new CubicEase { EasingMode = EasingMode.EaseOut };

 public static void Tween(DependencyObject target, DependencyProperty property, double from, double to, int ms, bool spring = false, Action completed = null)
 {
  UIElement ui = target as UIElement;
  Animatable animatable = target as Animatable;
  if (ui == null && animatable == null) { if(completed!=null)completed(); return; }
  if (ui != null) ui.BeginAnimation(property, null);
  else animatable.BeginAnimation(property, null);
  target.SetValue(property, from);
  if (!Enabled || ms <= 0) { target.SetValue(property, to); if(completed!=null)completed(); return; }
  var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Smooth, FillBehavior = FillBehavior.HoldEnd };
  animation.Completed += delegate { target.SetValue(property, to); if (ui != null) ui.BeginAnimation(property, null); else animatable.BeginAnimation(property, null); if(completed!=null)completed(); };
  if (ui != null) ui.BeginAnimation(property, animation);
  else animatable.BeginAnimation(property, animation);
 }

 public static TransformGroup Transforms(UIElement element)
 {
  TransformGroup group = element.RenderTransform as TransformGroup;
  if (group != null && group.Children.Count == 3 && group.Children[0] is ScaleTransform && group.Children[1] is RotateTransform && group.Children[2] is TranslateTransform) return group;
  group = new TransformGroup(); group.Children.Add(new ScaleTransform(1, 1)); group.Children.Add(new RotateTransform()); group.Children.Add(new TranslateTransform());
  element.RenderTransform = group; element.RenderTransformOrigin = new Point(.5, .5); return group;
 }
 public static ScaleTransform Scale(UIElement element) { return (ScaleTransform)Transforms(element).Children[0]; }
 public static RotateTransform Rotate(UIElement element) { return (RotateTransform)Transforms(element).Children[1]; }
 public static TranslateTransform Translate(UIElement element) { return (TranslateTransform)Transforms(element).Children[2]; }
 public static void Enter(UIElement element, int ms = Fast, double x = 0, double y = 8, double scale = .96)
 {
  if (!Enabled) { element.Opacity = 1; Translate(element).X=0; Translate(element).Y=0; Scale(element).ScaleX=1; Scale(element).ScaleY=1; return; }
  var t = Translate(element); var s = Scale(element);
  Tween(element, UIElement.OpacityProperty, 0, 1, ms);
  Tween(t, TranslateTransform.XProperty, x, 0, ms);
  Tween(t, TranslateTransform.YProperty, y, 0, ms);
  Tween(s, ScaleTransform.ScaleXProperty, scale, 1, ms);
  Tween(s, ScaleTransform.ScaleYProperty, scale, 1, ms);
 }
 public static void Exit(UIElement element, Action completed, int ms = Quick, double x = 0, double y = -4, double scale = .98)
 {
  if (!Enabled) { if(completed!=null)completed(); return; }
  Tween(element, UIElement.OpacityProperty, element.Opacity, 0, ms, false, completed);
  Tween(Translate(element), TranslateTransform.XProperty, Translate(element).X, x, ms);
  Tween(Translate(element), TranslateTransform.YProperty, Translate(element).Y, y, ms);
  Tween(Scale(element), ScaleTransform.ScaleXProperty, Scale(element).ScaleX, scale, ms);
  Tween(Scale(element), ScaleTransform.ScaleYProperty, Scale(element).ScaleY, scale, ms);
 }
 public static void Shake(UIElement element)
 {
  var t = Translate(element);
  if (!Enabled) return;
  var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(280), FillBehavior = FillBehavior.Stop };
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromPercent(.2)));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromPercent(.43)));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(4, KeyTime.FromPercent(.68)));
  animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
  t.BeginAnimation(TranslateTransform.XProperty, animation);
 }
 public static void Lift(UIElement element, double y, double scale, bool spring = false)
 {
  var t = Translate(element); var s = Scale(element);
  Tween(t, TranslateTransform.YProperty, t.Y, y, Fast, spring);
  Tween(s, ScaleTransform.ScaleXProperty, s.ScaleX, scale, Fast, spring);
  Tween(s, ScaleTransform.ScaleYProperty, s.ScaleY, scale, Fast, spring);
 }
}
