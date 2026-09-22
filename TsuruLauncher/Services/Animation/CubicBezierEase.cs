using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TsuruLauncher.Services.Animation
{
    /// <summary>
    /// CSS <c>cubic-bezier(x1, y1, x2, y2)</c> 的 1:1 移植。
    ///
    /// WPF 自带的 <see cref="CubicEase"/> / <see cref="QuadraticEase"/> 只能表达
    /// 「幂曲线」，无法表达 Axolotl（Tailwind / 手写 SCSS）里真正在用的
    /// <c>cubic-bezier(0.4, 0, 0.2, 1)</c>、<c>cubic-bezier(0.15, 1.4, 0.64, 0.96)</c>
    /// 这类带过冲的贝塞尔曲线。这里用 WebKit UnitBezier 的同一套求解法
    /// （先 Newton-Raphson 迭代，失败再二分）把 x 轴参数化还原成 y(t)。
    ///
    /// <b>终止性与数值安全（本次加固）</b>：
    ///   * 牛顿迭代硬上限 <see cref="MaxNewtonIterations"/>、二分硬上限
    ///     <see cref="MaxBisectionIterations"/>、细分兜底硬上限 <see cref="MaxRefineIterations"/>，
    ///     超限一律返回当前近似值 —— 求解器**不可能**不返回；
    ///   * 改前二分是 <c>while (lo &lt; hi)</c>，终止性只靠 double 精度「自然收敛」：
    ///     当 <c>t = (hi - lo) * 0.5 + lo</c> 舍入回 <c>lo</c> 时，<c>lo</c>/<c>hi</c> 都不再前进，
    ///     而 <c>x &gt; SampleCurveX(t)</c> 恒为真 —— 就是死循环。现在改成计数上限。
    ///   * NaN / ±Infinity 一律拒绝：输入 NaN 返回 0，y 控制点非有限值夹取到合法区间，
    ///     求出的 y 再做一次有限性兜底 —— 动画属性永远不会被写成 NaN。
    ///   * y 控制点允许超出 [0, 1]（过冲 / 回抽是 CSS 的合法写法），只夹掉离谱值。
    /// </summary>
    public sealed class CubicBezierEase : EasingFunctionBase
    {
        public static readonly DependencyProperty X1Property =
            DependencyProperty.Register(nameof(X1), typeof(double), typeof(CubicBezierEase),
                new PropertyMetadata(0.25, OnCoefficientChanged));

        public static readonly DependencyProperty Y1Property =
            DependencyProperty.Register(nameof(Y1), typeof(double), typeof(CubicBezierEase),
                new PropertyMetadata(0.1, OnCoefficientChanged));

        public static readonly DependencyProperty X2Property =
            DependencyProperty.Register(nameof(X2), typeof(double), typeof(CubicBezierEase),
                new PropertyMetadata(0.25, OnCoefficientChanged));

        public static readonly DependencyProperty Y2Property =
            DependencyProperty.Register(nameof(Y2), typeof(double), typeof(CubicBezierEase),
                new PropertyMetadata(1.0, OnCoefficientChanged));

        /// <summary>控制点 1 的 x（CSS 的第一个参数），必须落在 [0, 1]。</summary>
        public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }

        /// <summary>控制点 1 的 y（CSS 的第二个参数），允许超出 [0, 1] 以表达回弹/过冲。</summary>
        public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }

        /// <summary>控制点 2 的 x（CSS 的第三个参数），必须落在 [0, 1]。</summary>
        public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }

        /// <summary>控制点 2 的 y（CSS 的第四个参数），允许超出 [0, 1]。</summary>
        public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

        public CubicBezierEase()
        {
            // EasingFunctionBase.Ease() 会按 EasingMode 二次包装 EaseInCore；
            // 这里要的是「原样输出曲线」，所以固定成 EaseIn 让包装退化为恒等。
            EasingMode = EasingMode.EaseIn;
        }

        public CubicBezierEase(double x1, double y1, double x2, double y2) : this()
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }

        private static void OnCoefficientChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // Freezable 缓存：系数变了就丢掉采样缓存
            if (d is CubicBezierEase ease) ease._sampler = null;
        }

        private Sampler? _sampler;

        #region 终止性 / 数值安全常量

        /// <summary>牛顿迭代硬上限。</summary>
        public const int MaxNewtonIterations = 8;

        /// <summary>二分迭代硬上限（改前是 <c>while (lo &lt; hi)</c>，没有上限）。</summary>
        public const int MaxBisectionIterations = 20;

        /// <summary>二分之后的细分兜底硬上限。</summary>
        public const int MaxRefineIterations = 20;

        /// <summary>单次求解的迭代总数硬上限 —— 求解器一定会在这么多步之内返回。</summary>
        public const int MaxTotalIterations = MaxNewtonIterations + MaxBisectionIterations + MaxRefineIterations;

        /// <summary>y 控制点的合法下界（CSS 允许回抽，但不允许离谱值）。</summary>
        public const double MinControlY = -4.0;

        /// <summary>y 控制点的合法上界。</summary>
        public const double MaxControlY = 4.0;

        #endregion

        /// <summary>一次求解的迭代统计。热路径不做任何分配，只在诊断/单测里读。</summary>
        public readonly struct SolveDiagnostics
        {
            /// <summary>求解结果 y（保证有限）。</summary>
            public readonly double Value;

            /// <summary>本轮牛顿迭代次数。</summary>
            public readonly int NewtonIterations;

            /// <summary>本轮二分 + 细分迭代次数。</summary>
            public readonly int BisectionIterations;

            /// <summary>牛顿 + 二分总迭代次数。</summary>
            public readonly int TotalIterations;

            /// <summary>true = 迭代预算用尽仍未达到 Epsilon，返回的是当前近似值。</summary>
            public readonly bool HitIterationLimit;

            public SolveDiagnostics(double value, int newtonIterations, int bisectionIterations, bool hitIterationLimit)
            {
                Value = value;
                NewtonIterations = newtonIterations;
                BisectionIterations = bisectionIterations;
                TotalIterations = newtonIterations + bisectionIterations;
                HitIterationLimit = hitIterationLimit;
            }
        }

        private Sampler GetSampler()
        {
            double x1 = Clamp01(X1);
            double x2 = Clamp01(X2);
            // y 允许过冲，但 NaN / ±Infinity / 离谱值必须挡在采样器外面：
            // 它们会让 SampleCurveY 对**所有** t 返回 NaN，进而把 Opacity / Scale 写成 NaN。
            double y1 = SanitizeControlY(Y1, 0.0);
            double y2 = SanitizeControlY(Y2, 1.0);
            return _sampler ??= new Sampler(x1, y1, x2, y2);
        }

        private static double Clamp01(double v)
            => double.IsNaN(v) ? 0.0 : (v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v));

        /// <summary>把 y 控制点夹到合法过冲区间；NaN 用 <paramref name="fallback"/> 顶替（±Infinity 走夹取）。</summary>
        private static double SanitizeControlY(double v, double fallback)
            => double.IsNaN(v) ? fallback : (v < MinControlY ? MinControlY : (v > MaxControlY ? MaxControlY : v));

        protected override double EaseInCore(double normalizedTime)
        {
            // NaN 必须最先挡掉：它与任何值比较都是 false，会同时穿过下面两个判断，
            // 一路流进 DoubleAnimation -> Opacity/Scale 变成 NaN -> 渲染与布局一起崩。
            if (double.IsNaN(normalizedTime)) return 0.0;
            if (normalizedTime <= 0.0) return 0.0;
            if (normalizedTime >= 1.0) return 1.0;
            return GetSampler().Solve(normalizedTime);
        }

        /// <summary>
        /// 与 <see cref="EaseInCore"/> 完全同一条代码路径，额外返回迭代统计。
        /// 供离线单测断言「迭代次数有上限 / 返回值有限 / 单次耗时 &lt; 1ms」。
        /// </summary>
        public SolveDiagnostics SolveWithDiagnostics(double normalizedTime)
        {
            if (double.IsNaN(normalizedTime)) return new SolveDiagnostics(0.0, 0, 0, false);
            if (normalizedTime <= 0.0) return new SolveDiagnostics(0.0, 0, 0, false);
            if (normalizedTime >= 1.0) return new SolveDiagnostics(1.0, 0, 0, false);

            var sampler = GetSampler();
            double y = sampler.Solve(normalizedTime);
            return new SolveDiagnostics(y, sampler.LastNewtonIterations, sampler.LastBisectionIterations,
                sampler.LastHitIterationLimit);
        }

        protected override Freezable CreateInstanceCore() => new CubicBezierEase();

        /// <summary>WebKit UnitBezier 的等价实现：x(t) 三次多项式 + 导数求根。</summary>
        private sealed class Sampler
        {
            private const double Epsilon = 1e-7;

            private readonly double _ax, _bx, _cx;
            private readonly double _ay, _by, _cy;

            /// <summary>上一轮求解的迭代统计（诊断用；同一实例只在 UI 线程上被求值）。</summary>
            public int LastNewtonIterations;
            public int LastBisectionIterations;
            public bool LastHitIterationLimit;

            public Sampler(double x1, double y1, double x2, double y2)
            {
                _cx = 3.0 * x1;
                _bx = 3.0 * (x2 - x1) - _cx;
                _ax = 1.0 - _cx - _bx;

                _cy = 3.0 * y1;
                _by = 3.0 * (y2 - y1) - _cy;
                _ay = 1.0 - _cy - _by;
            }

            public double Solve(double x)
            {
                LastNewtonIterations = 0;
                LastBisectionIterations = 0;
                LastHitIterationLimit = false;

                if (double.IsNaN(x)) return 0.0;   // 不放大 NaN

                double y = SampleCurveY(SolveCurveX(x));

                // 有限系数 + 有限 t 下多项式必然有限；这里是最后一道闸，
                // 保证「动画属性绝不会被写成 NaN / Infinity」。
                if (double.IsNaN(y)) return 0.0;
                if (double.IsInfinity(y)) return y > 0.0 ? 1.0 : 0.0;
                return y;
            }

            private double SampleCurveX(double t) => ((_ax * t + _bx) * t + _cx) * t;
            private double SampleCurveY(double t) => ((_ay * t + _by) * t + _cy) * t;
            private double SampleCurveDerivativeX(double t) => (3.0 * _ax * t + 2.0 * _bx) * t + _cx;

            private double SolveCurveX(double x)
            {
                // 1) Newton-Raphson —— 硬上限 MaxNewtonIterations，跑飞就交给二分
                double t = x;
                for (int i = 0; i < MaxNewtonIterations; i++)
                {
                    LastNewtonIterations++;

                    double error = SampleCurveX(t) - x;
                    if (Math.Abs(error) < Epsilon) return t;

                    double d = SampleCurveDerivativeX(t);
                    if (!IsFinite(d) || Math.Abs(d) < 1e-6) break;   // 平坦段 / 跑飞
                    t -= error / d;
                    if (!IsFinite(t)) break;
                }

                // 2) 二分兜底 —— 硬上限 MaxBisectionIterations
                //    改前是 while (lo < hi)：没有迭代上限，只靠 double 精度自然收敛。
                //    对当前所有曲线它都会提前返回，但「靠精度收敛」不是终止性证明：
                //    一旦 (hi - lo) * 0.5 + lo 舍入回 lo，lo/hi 都不再前进而 x > cur 恒真 -> 死循环。
                double lo = 0.0, hi = 1.0;
                t = x;
                if (double.IsNaN(t)) return lo;
                if (t < lo) return lo;
                if (t > hi) return hi;

                for (int i = 0; i < MaxBisectionIterations && lo < hi; i++)
                {
                    LastBisectionIterations++;

                    double current = SampleCurveX(t);
                    if (Math.Abs(current - x) < Epsilon) return t;
                    if (x > current) lo = t; else hi = t;
                    t = (hi - lo) * 0.5 + lo;
                }

                // 3) 细分兜底 —— 硬上限 MaxRefineIterations，超限直接返回当前近似值
                for (int i = 0; i < MaxRefineIterations; i++)
                {
                    LastBisectionIterations++;

                    double current = SampleCurveX(t);
                    if (Math.Abs(current - x) < Epsilon) return t;
                    if (x > current) lo = t; else hi = t;
                    t = (hi - lo) * 0.5 + lo;
                }

                LastHitIterationLimit = true;
                return IsFinite(t) ? t : x;
            }

            private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
